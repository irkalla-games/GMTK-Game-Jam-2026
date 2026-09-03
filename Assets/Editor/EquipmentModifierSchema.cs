using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

/// <summary>
/// Reflects over every EquipmentModifier and CardModifier subclass and writes
/// Tools/EquipmentSheet/modifier-schema.json - the type/field catalogue Tools/EquipmentSheet's
/// PowerShell scripts read to build the Modifiers and Card Tuning tabs' columns and dropdowns.
///
/// This is what lets a brand new modifier type appear in the sheet with no edit to any .ps1 file:
/// write the C# class, focus the Editor (or run the menu item below), sync. See CLAUDE.md's
/// "Design sheets" section and Tools/EquipmentSheet/Export-EquipmentSheet.ps1's header for the rest
/// of the round trip this feeds.
///
/// Also writes Tools/EquipmentSheet/describe-cache.json - every EquipmentData's modifiers' and nested
/// CardModifiers' Describe() text, keyed by a hash of the same canonical string
/// Format-EquipmentModifiers produces in PowerShell, so the sheet's read-only Effect Preview column
/// can show real C# output without PowerShell having to reimplement every Describe() body.
///
/// Editor-only, and not covered by Tools/compile-check.ps1 without -IncludeEditor - see CLAUDE.md.
/// </summary>
public static class EquipmentModifierSchema
{
    private const string SchemaPath = "Tools/EquipmentSheet/modifier-schema.json";
    private const string DescribeCachePath = "Tools/EquipmentSheet/describe-cache.json";
    private const string EquipmentFolder = "Assets/Data/Equipment";
    private const int SchemaVersion = 1;
    private const int MaxDepth = 4;

    // ------------------------------------------------------------------------------------------
    // Schema JSON shape
    // ------------------------------------------------------------------------------------------

    // Public: EquipmentSheetImporter.cs reads a modifier's own field list (isList/objectType/enum
    // names) straight off the same schema this class writes to disk, rather than the equipment.json
    // work order re-transmitting it per leaf - one definition of "what a field IS", read by both the
    // writer and the consumer.

    [Serializable]
    public class Schema
    {
        public int schemaVersion;
        public string writtenUtc;
        public string unityVersion;
        public List<TypeSpec> types = new();
    }

    [Serializable]
    public class TypeSpec
    {
        public string name;
        public string kind;         // "equipment" | "card"
        public string menuName;
        public List<FieldSpec> fields = new();
    }

    [Serializable]
    public class FieldSpec
    {
        public string path;             // dotted, property-friendly (backing-field name resolved)
        public string serializedPath;   // dotted, exactly what SerializedObject.FindProperty needs
        public string kind;             // "int" | "float" | "bool" | "string" | "enum" | "object" | "list"
        public string role;             // "nestedCardModifiers" when this field IS the List<CardModifier>
        public bool isList;
        public string listEncoding;     // "packedHex" | "sequence" | null
        public string enumType;
        public List<string> enumNames;
        public List<int> enumValues;
        public bool isFlags;
        public string objectType;
        public string tooltip;
        public bool truncated;
    }

    /// <summary>
    /// Reads modifier-schema.json straight off disk (not rebuilt from reflection) - what
    /// EquipmentSheetImporter uses so it interprets each work-order leaf against exactly the schema
    /// PowerShell built the sheet's columns from, not whatever the types look like at this instant.
    /// </summary>
    public static Schema ReadFromDisk()
    {
        string full = ToRepoPath(SchemaPath);
        if (!File.Exists(full))
        {
            throw new InvalidOperationException($"{SchemaPath} not found. Run Tools > Equipment > Write Modifier Schema first.");
        }
        string text = File.ReadAllText(full).TrimStart('﻿', '​');
        return JsonUtility.FromJson<Schema>(text);
    }

    public static TypeSpec GetType(Schema schema, string typeName)
    {
        foreach (TypeSpec t in schema.types) { if (t.name == typeName) { return t; } }
        return null;
    }

    public static FieldSpec GetField(Schema schema, string typeName, string path)
    {
        TypeSpec t = GetType(schema, typeName);
        if (t == null) { return null; }
        foreach (FieldSpec f in t.fields) { if (f.path == path) { return f; } }
        return null;
    }

    // ------------------------------------------------------------------------------------------
    // Describe cache shape
    // ------------------------------------------------------------------------------------------

    [Serializable]
    private class DescribeCache
    {
        public string writtenUtc;
        public List<DescribeItem> items = new();
    }

    [Serializable]
    private class DescribeItem
    {
        public string guid;
        public string preview;   // every modifier's Describe(), joined for the whole item
        public string hash;      // sha1 of the same canonical string Format-EquipmentModifiers builds
        public List<DescribeMod> modifiers = new();
    }

    [Serializable]
    private class DescribeMod
    {
        public string modId;
        public string describe;
        public List<DescribeMod> children = new();   // CardTuningModifier's nested CardModifiers
    }

    // ------------------------------------------------------------------------------------------
    // Menu / auto-run
    // ------------------------------------------------------------------------------------------

    [MenuItem("Tools/Equipment/Write Modifier Schema")]
    public static void WriteModifierSchemaMenuItem()
    {
        bool schemaChanged = WriteIfChanged();
        WriteDescribeCache();
        Debug.Log(schemaChanged
            ? "Equipment modifier schema: written (types or fields changed)."
            : "Equipment modifier schema: already up to date.");
    }

    /// <summary>
    /// Keeps the schema fresh after every domain reload - the mechanism that makes a new modifier
    /// subclass show up in the sheet without anyone remembering to run the menu item by hand.
    /// </summary>
    [DidReloadScripts]
    private static void OnScriptsReloaded()
    {
        WriteIfChanged();
    }

    // ------------------------------------------------------------------------------------------
    // Schema: build, compare, write
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the schema in memory and writes it only if it differs from what is already on disk
    /// (ignoring writtenUtc), so an ordinary domain reload with no modifier changes produces no git
    /// diff. Returns true if the file was written.
    /// </summary>
    public static bool WriteIfChanged()
    {
        Schema schema = Build();
        string newJson = ToJson(schema);

        string full = ToRepoPath(SchemaPath);
        if (File.Exists(full))
        {
            string existing = File.ReadAllText(full);
            if (NormalizeForCompare(existing) == NormalizeForCompare(newJson)) { return false; }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full) !);
        File.WriteAllText(full, newJson, new UTF8Encoding(false));
        return true;
    }

    private static string NormalizeForCompare(string json)
    {
        // Strip the one field expected to change every run so an unrelated timestamp does not force a
        // rewrite (and a git diff) on every focus.
        int i = json.IndexOf("\"writtenUtc\"", StringComparison.Ordinal);
        if (i < 0) { return json; }
        int lineEnd = json.IndexOf('\n', i);
        return lineEnd < 0 ? json.Substring(0, i) : json.Remove(i, lineEnd - i);
    }

    private static Schema Build()
    {
        Schema schema = new()
        {
            schemaVersion = SchemaVersion,
            writtenUtc = DateTime.UtcNow.ToString("o"),
            unityVersion = Application.unityVersion,
        };

        foreach (Type t in TypeCache.GetTypesDerivedFrom<EquipmentModifier>()
                     .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition)
                     .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            schema.types.Add(BuildTypeSpec(t, "equipment"));
        }

        foreach (Type t in TypeCache.GetTypesDerivedFrom<CardModifier>()
                     .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition)
                     .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            schema.types.Add(BuildTypeSpec(t, "card"));
        }

        return schema;
    }

    private static TypeSpec BuildTypeSpec(Type type, string kind)
    {
        TypeSpec spec = new() { name = type.Name, kind = kind, menuName = GetMenuName(type) };

        Type stopAt = kind == "equipment" ? typeof(EquipmentModifier) : typeof(CardModifier);
        List<FieldInfo> ownFields = new();

        // Base-first: walk from the root down to the leaf type, collecting each level's own declared
        // fields, so field order matches declaration order top-to-bottom the way the Inspector shows
        // it - and so a field re-declared with `new` at a derived level (not used today, but cheap to
        // get right) is still visited base-first rather than reversed.
        List<Type> chain = new();
        for (Type t = type; t != null && t != stopAt; t = t.BaseType) { chain.Add(t); }
        chain.Reverse();

        foreach (Type level in chain)
        {
            foreach (FieldInfo f in level.GetFields(BindingFlags.Instance | BindingFlags.Public
                                                      | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!IsSerialized(f)) { continue; }
                ownFields.Add(f);
            }
        }

        HashSet<Type> onPath = new() { type };
        foreach (FieldInfo f in ownFields)
        {
            WalkField(f, PropertyPathFor(f), SerializedPathFor(f), 1, onPath, spec.fields);
        }

        return spec;
    }

    private static bool IsSerialized(FieldInfo f)
    {
        if (f.IsStatic || f.IsInitOnly || f.IsLiteral) { return false; }
        if (f.GetCustomAttribute<NonSerializedAttribute>() != null) { return false; }
        if (f.IsPublic) { return true; }
        return f.GetCustomAttribute<SerializeField>() != null;
    }

    /// <summary>
    /// The Inspector-friendly name for a field: a [field: SerializeField] auto-property backing field
    /// (name "&lt;Foo&gt;k__BackingField") resolves to "Foo"; an ordinary field keeps its own name.
    /// </summary>
    private static string PropertyPathFor(FieldInfo f)
    {
        string n = f.Name;
        if (n.Length > 0 && n[0] == '<')
        {
            int close = n.IndexOf('>');
            if (close > 1 && n.EndsWith("k__BackingField", StringComparison.Ordinal)) { return n.Substring(1, close - 1); }
        }
        return n;
    }

    /// <summary>
    /// What SerializedObject.FindProperty actually needs - the raw field name, backing field included.
    /// </summary>
    private static string SerializedPathFor(FieldInfo f) => f.Name;

    private static void WalkField(FieldInfo f, string path, string serializedPath, int depth,
                                   HashSet<Type> onPath, List<FieldSpec> into)
    {
        Type fieldType = f.FieldType;
        TooltipAttribute tooltipAttr = f.GetCustomAttribute<TooltipAttribute>();
        string tooltip = tooltipAttr != null ? tooltipAttr.tooltip : "";

        // List<T> / T[] - classify by element type. Two special cases: CardTuningModifier.modifiers is
        // a List<CardModifier> and is NOT a leaf column - it is the whole Card Tuning tab for this
        // item, so it is recorded with role=nestedCardModifiers and no further descent. Every other
        // list (enum, bool, object) becomes one leaf column, comma-joined at read time.
        Type elementType = GetEnumerableElementType(fieldType);
        if (elementType != null)
        {
            if (typeof(CardModifier).IsAssignableFrom(elementType))
            {
                into.Add(new FieldSpec
                {
                    path = path, serializedPath = serializedPath, kind = "list",
                    role = "nestedCardModifiers", objectType = "CardModifier", tooltip = tooltip,
                });
                return;
            }

            FieldSpec listSpec = new() { path = path, serializedPath = serializedPath, isList = true, tooltip = tooltip };

            if (elementType.IsEnum)
            {
                listSpec.kind = "enum";
                listSpec.listEncoding = "packedHex";
                FillEnum(listSpec, elementType);
            }
            else if (elementType == typeof(bool))
            {
                listSpec.kind = "bool";
                listSpec.listEncoding = "packedHex";
            }
            else if (typeof(UnityEngine.Object).IsAssignableFrom(elementType))
            {
                listSpec.kind = "object";
                listSpec.listEncoding = "sequence";
                listSpec.objectType = elementType.Name;
            }
            else
            {
                // Not a shape used anywhere today (a List<int> or a List<[Serializable] struct>).
                // Recorded as truncated rather than guessed at - see the README legend / Balance tab.
                listSpec.kind = "unsupported";
                listSpec.truncated = true;
            }

            into.Add(listSpec);
            return;
        }

        if (fieldType.IsEnum)
        {
            FieldSpec spec = new() { path = path, serializedPath = serializedPath, kind = "enum", tooltip = tooltip };
            FillEnum(spec, fieldType);
            into.Add(spec);
            return;
        }

        if (fieldType == typeof(int)) { into.Add(new FieldSpec { path = path, serializedPath = serializedPath, kind = "int", tooltip = tooltip }); return; }
        if (fieldType == typeof(float)) { into.Add(new FieldSpec { path = path, serializedPath = serializedPath, kind = "float", tooltip = tooltip }); return; }
        if (fieldType == typeof(bool)) { into.Add(new FieldSpec { path = path, serializedPath = serializedPath, kind = "bool", tooltip = tooltip }); return; }
        if (fieldType == typeof(string)) { into.Add(new FieldSpec { path = path, serializedPath = serializedPath, kind = "string", tooltip = tooltip }); return; }

        if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
        {
            into.Add(new FieldSpec
            {
                path = path, serializedPath = serializedPath, kind = "object",
                objectType = fieldType.Name, tooltip = tooltip,
            });
            return;
        }

        // A [Serializable] struct/class - recurse into its own fields, dotted onto this path. Depth is
        // counted in dotted segments: AddEntryModifier.entry.area.radius.shape is 4 - entry(1),
        // area(2), radius(3), shape(4) - so the cap has to be 4, not 3, or those four columns vanish.
        bool serializable = fieldType.IsValueType || fieldType.GetCustomAttribute<SerializableAttribute>() != null;
        if (serializable && depth < MaxDepth && !onPath.Contains(fieldType))
        {
            onPath.Add(fieldType);
            foreach (FieldInfo nested in fieldType.GetFields(BindingFlags.Instance | BindingFlags.Public
                                                               | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!IsSerialized(nested)) { continue; }
                WalkField(nested, path + "." + PropertyPathFor(nested), serializedPath + "." + SerializedPathFor(nested),
                          depth + 1, onPath, into);
            }
            onPath.Remove(fieldType);
            return;
        }

        // Depth cap hit, a cycle, or a type this walker does not know how to flatten (a Dictionary, a
        // non-serializable class). Recorded so the README legend can say so rather than the column
        // silently never existing.
        into.Add(new FieldSpec { path = path, serializedPath = serializedPath, kind = "unsupported", tooltip = tooltip, truncated = true });
    }

    private static void FillEnum(FieldSpec spec, Type enumType)
    {
        spec.enumType = enumType.Name;
        spec.isFlags = enumType.GetCustomAttribute<FlagsAttribute>() != null;
        Array values = Enum.GetValues(enumType);
        spec.enumNames = new List<string>();
        spec.enumValues = new List<int>();
        foreach (object v in values)
        {
            spec.enumNames.Add(Enum.GetName(enumType, v));
            spec.enumValues.Add(Convert.ToInt32(v));
        }
    }

    private static Type GetEnumerableElementType(Type t)
    {
        if (t.IsArray) { return t.GetElementType(); }
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) { return t.GetGenericArguments()[0]; }
        return null;
    }

    private static string GetMenuName(Type type)
    {
        CreateAssetMenuAttribute attr = type.GetCustomAttribute<CreateAssetMenuAttribute>();
        return attr != null ? attr.menuName : "";
    }

    // ------------------------------------------------------------------------------------------
    // Describe cache
    // ------------------------------------------------------------------------------------------

    public static void WriteDescribeCache()
    {
        DescribeCache cache = new() { writtenUtc = DateTime.UtcNow.ToString("o") };

        foreach (string guid in AssetDatabase.FindAssets("t:EquipmentData", new[] { EquipmentFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EquipmentData item = AssetDatabase.LoadAssetAtPath<EquipmentData>(path);
            if (item == null) { continue; }

            DescribeItem entry = new() { guid = guid };
            List<string> previewParts = new();
            List<string> canonicalParts = new();

            foreach (EquipmentModifier mod in item.modifiers)
            {
                if (mod == null) { continue; }

                string describe = SafeDescribe(mod);
                previewParts.Add(describe);

                DescribeMod modEntry = new()
                {
                    modId = LocalFileId(mod).ToString(),
                    describe = describe,
                };

                // Structural token only - "TypeName:localFileId", never Describe() text. This has to be
                // computable identically in PowerShell (Get-ModifierTreeHash in
                // EquipmentSheet.Common.psm1) so a bare, Unity-less export can tell a stale cache from a
                // fresh one; PowerShell has no access to a C# Describe() body to fold into the hash, so
                // neither side does. See that function's own comment for exactly what this does and does
                // not catch.
                string canonicalMod = $"{mod.GetType().Name}:{LocalFileId(mod)}";

                if (mod is CardTuningModifier tuning)
                {
                    List<string> childCanonical = new();
                    foreach (CardModifier child in tuning.NestedModifiers)
                    {
                        if (child == null) { continue; }
                        modEntry.children.Add(new DescribeMod
                        {
                            modId = LocalFileId(child).ToString(),
                            describe = SafeDescribe(child),
                        });
                        childCanonical.Add($"{child.GetType().Name}:{LocalFileId(child)}");
                    }
                    canonicalMod += "[" + string.Join(";", childCanonical) + "]";
                }

                canonicalParts.Add(canonicalMod);
                entry.modifiers.Add(modEntry);
            }

            entry.preview = string.Join(" | ", previewParts);
            entry.hash = Sha1(string.Join("|", canonicalParts));
            cache.items.Add(entry);
        }

        string full = ToRepoPath(DescribeCachePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full) !);
        File.WriteAllText(full, ToJson(cache), new UTF8Encoding(false));
    }

    private static string SafeDescribe(object mod)
    {
        try
        {
            return mod switch
            {
                EquipmentModifier em => em.Describe(),
                CardModifier cm => cm.Describe(),
                _ => "",
            };
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Equipment modifier schema: Describe() threw on {mod.GetType().Name} - {e.Message}");
            return "(error)";
        }
    }

    private static long LocalFileId(UnityEngine.Object obj)
    {
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out _, out long localId)) { return localId; }
        return 0;
    }

    private static string Sha1(string text)
    {
        using System.Security.Cryptography.SHA1 sha = System.Security.Cryptography.SHA1.Create();
        byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        StringBuilder sb = new();
        foreach (byte b in bytes) { sb.Append(b.ToString("x2")); }
        return sb.ToString();
    }

    // ------------------------------------------------------------------------------------------
    // Plumbing
    // ------------------------------------------------------------------------------------------

    private static string ToRepoPath(string relative) => Path.Combine(Directory.GetCurrentDirectory(), relative);

    private static string ToJson<T>(T obj) => JsonUtility.ToJson(obj, prettyPrint: true) + "\n";
}
