using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates and updates EquipmentData assets (and their modifier sub-assets) from the design workbook,
/// via the equipment.json work order Tools/EquipmentSheet/Import-EquipmentSheet.ps1 writes. Same split
/// as CardSheetImporter: PowerShell reads the .xlsx and does the diffing (Unity's batch mode cannot run
/// while the Editor holds Temp/UnityLockfile), Unity does every write, because GUIDs, local fileIDs and
/// sub-asset surgery are its business.
///
/// The one thing this importer does that CardSheetImporter does not: an EquipmentData's "effects" are
/// polymorphic ScriptableObject sub-assets embedded in the same .asset file (EquipmentModifier, and a
/// CardTuningModifier's own nested CardModifier list) - see SyncModifiers, which finds, creates, retypes
/// and orphan-sweeps them by their Unity local fileID (surfaced to the sheet as the read-only Mod
/// Id/Sub Id columns). Every leaf value the sheet ships for a modifier covers every field that
/// modifier's TYPE declares (Build-ModifierPayload in EquipmentSheet.Common.psm1 always emits the full
/// set), so a retype (destroy the old sub-asset, create a new one of the target type) never needs a
/// separate field-carryover step - ApplyLeaves populates every field the new type has immediately after.
///
/// Never deletes an EquipmentData asset. A modifier/CardModifier sub-asset IS deleted when it is no
/// longer referenced by anything the sheet still lists for that item - see the orphan sweep at the end
/// of SyncModifiers - because a sub-asset has no independent identity for a human to intentionally keep
/// around the way a whole EquipmentData asset does.
///
/// Editor-only, and not covered by Tools/compile-check.ps1 without -IncludeEditor - verify by focusing
/// the Editor and reading the console, per CLAUDE.md.
/// </summary>
public static class EquipmentSheetImporter
{
    private const string JsonPath = "Tools/EquipmentSheet/equipment.json";
    private const string ImportScript = "Tools/EquipmentSheet/Import-EquipmentSheet.ps1";
    private const string ExportScript = "Tools/EquipmentSheet/Export-EquipmentSheet.ps1";
    private const string EquipmentRoot = "Assets/Data/Equipment";

    // ------------------------------------------------------------------------------------------
    // equipment.json shape. Flat and List-based - JsonUtility handles nothing else.
    // ------------------------------------------------------------------------------------------

    [Serializable] private class Payload
    {
        public string generatedUtc;
        public string source;
        public List<ItemSpec> items = new();
        public List<ConflictSpec> conflicts = new();
        public List<ConflictSpec> unresolved = new();
        public List<string> removedRows = new();
        public List<string> newInUnity = new();
        public List<string> problems = new();
    }

    [Serializable] private class ConflictSpec { public string key; public List<ConflictColumn> columns = new(); }
    [Serializable] private class ConflictColumn { public string column; public string unity; public string sheet; public string wasLast; }

    [Serializable] private class ItemSpec
    {
        public string key;
        public string assetPath;
        public string newAssetPath;
        public string guid;
        public string action;   // create | update | move
        public string itemName;
        public string description;
        public int rarity;
        public int requiredClass;
        public int slot;
        public bool excludeFromRewards;
        public bool modifiersProvided;
        public List<ModifierSpec> modifiers = new();
        public List<string> changed = new();
    }

    [Serializable] private class ModifierSpec
    {
        public string modId;
        public string typeName;
        public List<LeafValue> leaves = new();
        public List<CardTuningSpec> cardTuning = new();
    }

    [Serializable] private class CardTuningSpec
    {
        public string subId;
        public string typeName;
        public List<LeafValue> leaves = new();
    }

    [Serializable] private class LeafValue
    {
        public string path;
        public string kind;
        public int intValue;
        public float floatValue;
        public bool boolValue;
        public string stringValue;
        public bool isNull;
        public List<int> intList = new();
        public List<string> stringList = new();
        public string problem;
    }

    // ------------------------------------------------------------------------------------------
    // Menu
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The everyday action - see CardSheetImporter.SyncWithSheet's identical three-step shape and the
    /// same reasoning for why the workbook is deliberately NOT refreshed when anything conflicted or
    /// failed to apply.
    /// </summary>
    [MenuItem("Tools/Sync Equipment With Sheet", priority = -700)]
    public static void SyncEquipmentWithSheet()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Equipment sheet sync: exit Play Mode first.");
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Sync Equipment With Sheet", "Refreshing the modifier schema...", 0.05f);
            EquipmentModifierSchema.WriteIfChanged();

            EditorUtility.DisplayProgressBar("Sync Equipment With Sheet", "Reading the workbook...", 0.15f);
            if (!SheetSyncProcess.Run(ImportScript, string.Empty)) { return; }

            EditorUtility.DisplayProgressBar("Sync Equipment With Sheet", "Applying changes to assets...", 0.45f);
            if (!Apply(out int conflicts, out int failures)) { return; }

            if (conflicts > 0 || failures > 0)
            {
                List<string> reasons = new();
                if (conflicts > 0) { reasons.Add($"{conflicts} item(s) changed on both sides"); }
                if (failures > 0) { reasons.Add($"{failures} item(s) could not be applied - see the errors above"); }

                Debug.LogWarning($"Equipment sheet sync: stopped after applying everything else - {string.Join(", ", reasons)}. "
                                 + "The sheet was NOT refreshed, so your edits are still there. Fix the cause and sync again.");
                return;
            }

            EditorUtility.DisplayProgressBar("Sync Equipment With Sheet", "Writing effect previews...", 0.7f);
            EquipmentModifierSchema.WriteDescribeCache();

            EditorUtility.DisplayProgressBar("Sync Equipment With Sheet", "Refreshing the workbook...", 0.85f);
            SheetSyncProcess.Run(ExportScript, "-WriteBaseline");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("Tools/Equipment/Refresh Sheet From Unity (discards sheet edits)")]
    public static void RefreshSheetFromUnity()
    {
        if (!EditorUtility.DisplayDialog(
                "Refresh Sheet From Unity",
                "This rebuilds Docs/EquipmentDesign.xlsx from the assets and DISCARDS any edit in the "
                + "sheet that has not been synced.\n\nSync Equipment With Sheet keeps both sides. Use "
                + "that unless the sheet is known to be wrong.",
                "Discard sheet edits", "Cancel"))
        {
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Refresh Sheet", "Refreshing the modifier schema...", 0.2f);
            EquipmentModifierSchema.WriteIfChanged();
            EquipmentModifierSchema.WriteDescribeCache();

            EditorUtility.DisplayProgressBar("Refresh Sheet", "Rebuilding the workbook...", 0.6f);
            SheetSyncProcess.Run(ExportScript, "-Force -WriteBaseline");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ------------------------------------------------------------------------------------------
    // Applying the plan
    // ------------------------------------------------------------------------------------------

    private static bool Apply(out int conflictCount, out int failureCount)
    {
        conflictCount = 0;
        failureCount = 0;

        string full = Path.Combine(Directory.GetCurrentDirectory(), JsonPath);
        if (!File.Exists(full))
        {
            Debug.LogError($"Equipment sheet sync: {JsonPath} not found.");
            return false;
        }

        Payload payload;
        EquipmentModifierSchema.Schema schema;
        try
        {
            string text = SheetSyncProcess.ReadJsonFile(JsonPath);
            payload = JsonUtility.FromJson<Payload>(text);
            schema = EquipmentModifierSchema.ReadFromDisk();
        }
        catch (Exception e)
        {
            Debug.LogError($"Equipment sheet sync: could not read {JsonPath} or the modifier schema - {e.Message}");
            return false;
        }

        if (payload == null)
        {
            Debug.LogError($"Equipment sheet sync: {JsonPath} was empty or malformed.");
            return false;
        }

        foreach (string problem in payload.problems) { Debug.LogWarning($"Equipment sheet: {problem}"); }

        conflictCount = payload.conflicts.Count + payload.unresolved.Count;
        ReportConflicts(payload);

        if (payload.items.Count == 0)
        {
            Debug.Log("Equipment sheet sync: no sheet edits to apply.");
            return true;
        }

        int created = 0, updated = 0, moved = 0, failed = 0;

        try
        {
            // Deliberately not wrapped in StartAssetEditing/StopAssetEditing - see CardSheetImporter's
            // identical note. AddObjectToAsset requires the owner asset to already exist on disk, and
            // the volume here (a few dozen items) is nothing to win by batching.
            foreach (ItemSpec spec in payload.items)
            {
                if (spec.action == "move" && !MoveItem(spec)) { failed++; continue; }

                if (!WriteItem(spec, schema)) { failed++; continue; }

                if (spec.action == "move") { moved++; }
                else if (spec.action == "update") { updated++; }
                else { created++; }
            }

            failureCount = failed;

            Debug.Log($"Equipment sheet sync: {created} created, {updated} updated, {moved} moved"
                      + (failed > 0 ? $", {failed} FAILED - see errors above." : "."));
        }
        finally
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        if (payload.removedRows.Count > 0)
        {
            Debug.LogWarning($"Equipment sheet: {payload.removedRows.Count} row(s) were deleted from the sheet. "
                             + "The assets were KEPT - delete them in Unity if that was the intent: "
                             + string.Join(", ", payload.removedRows));
        }

        if (payload.newInUnity.Count > 0)
        {
            Debug.Log($"Equipment sheet: {payload.newInUnity.Count} item(s) authored in Unity will be added to "
                      + $"the sheet - {string.Join(", ", payload.newInUnity)}");
        }

        return true;
    }

    private static void ReportConflicts(Payload payload)
    {
        foreach (ConflictSpec c in payload.conflicts)
        {
            System.Text.StringBuilder sb = new();
            sb.AppendLine($"Equipment sheet CONFLICT: '{c.key}' changed in BOTH Unity and the sheet since the "
                          + "last sync. Nothing was written to it on either side.");
            foreach (ConflictColumn col in c.columns)
            {
                sb.AppendLine($"    {col.column}:");
                sb.AppendLine($"        was    '{col.wasLast}'");
                sb.AppendLine($"        Unity  '{col.unity}'");
                sb.AppendLine($"        sheet  '{col.sheet}'");
            }
            sb.Append("    Make both sides agree, or change only one of them, then sync again.");
            Debug.LogWarning(sb.ToString());
        }

        foreach (ConflictSpec c in payload.unresolved)
        {
            Debug.LogWarning($"Equipment sheet: '{c.key}' differs between Unity and the sheet, but there is no "
                             + "baseline recording which side moved, so neither was written. Use "
                             + "Tools > Equipment > Refresh Sheet From Unity if the assets are correct.");
        }
    }

    /// <summary>
    /// Renames/refiles an item because its Key or Folder changed - AssetDatabase.MoveAsset so the .meta
    /// (and therefore the GUID and every reference to it, including PartyMember run records) survives.
    /// </summary>
    private static bool MoveItem(ItemSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.newAssetPath) || spec.newAssetPath == spec.assetPath) { return true; }

        if (AssetDatabase.LoadAssetAtPath<EquipmentData>(spec.assetPath) == null)
        {
            Debug.LogError($"Equipment sheet: cannot move '{spec.key}' - nothing at {spec.assetPath}.");
            return false;
        }
        if (AssetDatabase.LoadAssetAtPath<EquipmentData>(spec.newAssetPath) != null)
        {
            Debug.LogError($"Equipment sheet: cannot move '{spec.key}' to {spec.newAssetPath} - an item is already there.");
            return false;
        }

        EnsureFolder(Path.GetDirectoryName(spec.newAssetPath).Replace('\\', '/'));

        string error = AssetDatabase.MoveAsset(spec.assetPath, spec.newAssetPath);
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError($"Equipment sheet: could not move {spec.assetPath} -> {spec.newAssetPath}: {error}");
            return false;
        }

        Debug.Log($"Equipment sheet: moved {spec.assetPath}  ->  {spec.newAssetPath}");
        spec.assetPath = spec.newAssetPath;
        return true;
    }

    private static bool WriteItem(ItemSpec spec, EquipmentModifierSchema.Schema schema)
    {
        string path = spec.assetPath;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

        EquipmentData item = AssetDatabase.LoadAssetAtPath<EquipmentData>(path);
        bool isNew = item == null;
        if (isNew)
        {
            item = ScriptableObject.CreateInstance<EquipmentData>();
            AssetDatabase.CreateAsset(item, path);
        }

        // Validate every modifier reference BEFORE writing anything, including the scalar fields below
        // - a problem here must leave the whole item untouched, the same "resolve everything, then
        // write" rule WriteCard follows for card effects.
        if (spec.modifiersProvided)
        {
            if (!ValidateModifierPlan(item, spec, out string problem))
            {
                Debug.LogError($"Equipment sheet: '{spec.key}' - {problem} - item left unchanged.");
                return false;
            }
        }

        SerializedObject so = new(item);
        so.FindProperty("<equipmentName>k__BackingField").stringValue = spec.itemName;
        so.FindProperty("<description>k__BackingField").stringValue = spec.description;
        so.FindProperty("<rarity>k__BackingField").intValue = spec.rarity;
        so.FindProperty("<requiredClass>k__BackingField").intValue = spec.requiredClass;
        so.FindProperty("<slot>k__BackingField").intValue = spec.slot;
        so.FindProperty("<excludeFromRewards>k__BackingField").boolValue = spec.excludeFromRewards;
        // Deliberately never touches <icon>k__BackingField - there is no Icon column on the sheet, and
        // addressing a property this importer does not own would wipe any icon art the moment it is
        // added in the Inspector.
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(item);

        if (spec.modifiersProvided && !SyncModifiers(item, spec, schema))
        {
            Debug.LogError($"Equipment sheet: '{spec.key}' - modifier sync failed - see errors above.");
            return false;
        }

        Debug.Log($"Equipment sheet: {(isNew ? "created" : "updated")} {path}"
                  + (spec.changed is { Count: > 0 } ? $" ({string.Join("; ", spec.changed)})" : ""));
        return true;
    }

    // ------------------------------------------------------------------------------------------
    // Modifier sub-asset lifecycle
    // ------------------------------------------------------------------------------------------

    private static Dictionary<string, UnityEngine.Object> IndexSubAssetsByFileId(string assetPath)
    {
        Dictionary<string, UnityEngine.Object> byFileId = new();
        foreach (UnityEngine.Object obj in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out _, out long localId))
            {
                byFileId[localId.ToString()] = obj;
            }
        }
        return byFileId;
    }

    /// <summary>
    /// Checks every Mod Id/Sub Id the sheet named for this item resolves to a real sub-asset that
    /// already exists, and that no two rows claim the same one - before SyncModifiers changes anything.
    /// A mistyped id must never be silently treated as "new" (that would duplicate a modifier instead
    /// of editing it).
    /// </summary>
    private static bool ValidateModifierPlan(EquipmentData item, ItemSpec spec, out string problem)
    {
        string path = AssetDatabase.GetAssetPath(item);
        Dictionary<string, UnityEngine.Object> byFileId = IndexSubAssetsByFileId(path);
        HashSet<string> claimed = new();

        foreach (ModifierSpec m in spec.modifiers)
        {
            if (!string.IsNullOrEmpty(m.modId))
            {
                if (!byFileId.ContainsKey(m.modId)) { problem = $"Modifiers row for '{m.typeName}' names Mod Id {m.modId}, which does not exist on this asset"; return false; }
                if (!claimed.Add(m.modId)) { problem = $"two Modifiers rows both claim Mod Id {m.modId}"; return false; }
            }

            foreach (CardTuningSpec s in m.cardTuning)
            {
                if (!string.IsNullOrEmpty(s.subId))
                {
                    if (!byFileId.ContainsKey(s.subId)) { problem = $"Card Tuning row for '{s.typeName}' names Sub Id {s.subId}, which does not exist on this asset"; return false; }
                    if (!claimed.Add(s.subId)) { problem = $"two Card Tuning rows both claim Sub Id {s.subId}"; return false; }
                }
            }
        }

        problem = "";
        return true;
    }

    private static bool SyncModifiers(EquipmentData item, ItemSpec spec, EquipmentModifierSchema.Schema schema)
    {
        string path = AssetDatabase.GetAssetPath(item);
        Dictionary<string, UnityEngine.Object> byFileId = IndexSubAssetsByFileId(path);
        HashSet<UnityEngine.Object> live = new();

        List<EquipmentModifier> topLevel = new();

        foreach (ModifierSpec m in spec.modifiers)
        {
            EquipmentModifier mod = (EquipmentModifier)ResolveOrCreateOrRetype(m.modId, m.typeName, byFileId, item, live);
            if (mod == null) { return false; }

            ApplyLeaves(new SerializedObject(mod), m.leaves, schema, m.typeName);

            if (mod is CardTuningModifier)
            {
                List<CardModifier> children = new();
                foreach (CardTuningSpec s in m.cardTuning)
                {
                    CardModifier child = (CardModifier)ResolveOrCreateOrRetype(s.subId, s.typeName, byFileId, item, live);
                    if (child == null) { return false; }
                    ApplyLeaves(new SerializedObject(child), s.leaves, schema, s.typeName);
                    children.Add(child);
                }

                SerializedObject tuningSo = new(mod);
                SerializedProperty childList = tuningSo.FindProperty("modifiers");
                childList.arraySize = children.Count;
                for (int i = 0; i < children.Count; i++) { childList.GetArrayElementAtIndex(i).objectReferenceValue = children[i]; }
                tuningSo.ApplyModifiedProperties();
                EditorUtility.SetDirty(mod);
            }

            topLevel.Add(mod);
        }

        SerializedObject itemSo = new(item);
        SerializedProperty modsProp = itemSo.FindProperty("<modifiers>k__BackingField");
        modsProp.arraySize = topLevel.Count;
        for (int i = 0; i < topLevel.Count; i++) { modsProp.GetArrayElementAtIndex(i).objectReferenceValue = topLevel[i]; }
        itemSo.ApplyModifiedProperties();
        EditorUtility.SetDirty(item);

        // Orphan sweep - anything left in the file that is a modifier type and was not touched above.
        // Runs only after every list has been rebuilt, so a CardTuningModifier's nested child is never
        // judged orphaned in the window between its parent's list being cleared and refilled (there is
        // no such window here - both are set in one ApplyModifiedProperties per object - but the rule
        // is kept as the ordering guarantee regardless of how this method evolves).
        foreach (UnityEngine.Object obj in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (obj == item || live.Contains(obj)) { continue; }
            if (obj is not EquipmentModifier && obj is not CardModifier) { continue; }
            AssetDatabase.RemoveObjectFromAsset(obj);
            UnityEngine.Object.DestroyImmediate(obj, true);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return true;
    }

    private static UnityEngine.Object ResolveOrCreateOrRetype(
        string id, string typeName, Dictionary<string, UnityEngine.Object> byFileId, EquipmentData owner, HashSet<UnityEngine.Object> live)
    {
        Type targetType = FindModifierType(typeName);
        if (targetType == null)
        {
            Debug.LogError($"Equipment sheet: '{typeName}' is not a known EquipmentModifier/CardModifier type.");
            return null;
        }

        if (!string.IsNullOrEmpty(id) && byFileId.TryGetValue(id, out UnityEngine.Object existing))
        {
            if (existing.GetType() == targetType)
            {
                live.Add(existing);
                return existing;
            }

            // Retype: a ScriptableObject cannot change class, so this is destroy-and-recreate. Every
            // field the new type declares is about to be written by ApplyLeaves from the sheet's own
            // payload (which always covers the target type's whole field set), so nothing needs to be
            // carried over by hand - the new object starts blank and ends up fully authored regardless.
            Debug.Log($"Equipment sheet: retyping a modifier on {owner.name} - {existing.GetType().Name} -> {typeName}.");
            AssetDatabase.RemoveObjectFromAsset(existing);
            UnityEngine.Object.DestroyImmediate(existing, true);
        }

        ScriptableObject created = ScriptableObject.CreateInstance(targetType);
        created.name = targetType.Name;
        AssetDatabase.AddObjectToAsset(created, owner);
        EditorUtility.SetDirty(created);
        live.Add(created);
        return created;
    }

    private static Dictionary<string, Type> s_modifierTypesByName;

    private static Type FindModifierType(string typeName)
    {
        if (s_modifierTypesByName == null)
        {
            s_modifierTypesByName = new Dictionary<string, Type>();
            foreach (Type t in TypeCache.GetTypesDerivedFrom<EquipmentModifier>()) { s_modifierTypesByName[t.Name] = t; }
            foreach (Type t in TypeCache.GetTypesDerivedFrom<CardModifier>()) { s_modifierTypesByName[t.Name] = t; }
        }
        return s_modifierTypesByName.TryGetValue(typeName, out Type t2) ? t2 : null;
    }

    private static void ApplyLeaves(SerializedObject so, List<LeafValue> leaves, EquipmentModifierSchema.Schema schema, string typeName)
    {
        foreach (LeafValue leaf in leaves)
        {
            EquipmentModifierSchema.FieldSpec fs = EquipmentModifierSchema.GetField(schema, typeName, leaf.path);
            if (fs == null)
            {
                Debug.LogWarning($"Equipment sheet: '{typeName}.{leaf.path}' is not in the modifier schema - skipped.");
                continue;
            }

            SerializedProperty prop = ResolveProperty(so, fs.serializedPath);
            if (prop == null)
            {
                Debug.LogWarning($"Equipment sheet: '{typeName}.{fs.serializedPath}' could not be found on the asset - skipped.");
                continue;
            }

            switch (fs.kind)
            {
                case "int": prop.intValue = leaf.intValue; break;
                case "float": prop.floatValue = leaf.floatValue; break;
                case "bool": prop.boolValue = leaf.boolValue; break;
                case "string": prop.stringValue = leaf.stringValue; break;
                case "enum":
                    if (fs.isList)
                    {
                        prop.arraySize = leaf.intList.Count;
                        for (int i = 0; i < leaf.intList.Count; i++) { prop.GetArrayElementAtIndex(i).intValue = leaf.intList[i]; }
                    }
                    else { prop.intValue = leaf.intValue; }
                    break;
                case "object":
                    if (fs.isList)
                    {
                        prop.arraySize = leaf.stringList.Count;
                        for (int i = 0; i < leaf.stringList.Count; i++)
                        {
                            prop.GetArrayElementAtIndex(i).objectReferenceValue = FindByNameUnique(fs.objectType, leaf.stringList[i]);
                        }
                    }
                    else
                    {
                        prop.objectReferenceValue = leaf.isNull ? null : FindByNameUnique(fs.objectType, leaf.stringValue);
                    }
                    break;
                default:
                    Debug.LogWarning($"Equipment sheet: '{typeName}.{leaf.path}' has schema kind '{fs.kind}', which is not writable - skipped.");
                    break;
            }
        }

        so.ApplyModifiedProperties();
    }

    private static SerializedProperty ResolveProperty(SerializedObject so, string dottedPath)
    {
        string[] segments = dottedPath.Split('.');
        SerializedProperty prop = so.FindProperty(segments[0]);
        for (int i = 1; i < segments.Length && prop != null; i++) { prop = prop.FindPropertyRelative(segments[i]); }
        return prop;
    }

    /// <summary>
    /// Resolves an asset by its exact name within a given type - t:&lt;typeName&gt; works from a bare
    /// class name string, so no generic type parameter (and therefore no per-objectType switch) is
    /// needed here. More than one match is refused rather than guessed at, unlike CardSheetImporter's
    /// FindByName - a wrong CardData bound into a relic's filter is a silent gameplay bug, not a cosmetic
    /// one.
    /// </summary>
    private static UnityEngine.Object FindByNameUnique(string typeName, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { return null; }

        List<UnityEngine.Object> matches = new();
        foreach (string guid in AssetDatabase.FindAssets($"t:{typeName}"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null && asset.name == name) { matches.Add(asset); }
        }

        if (matches.Count == 1) { return matches[0]; }
        if (matches.Count == 0)
        {
            Debug.LogWarning($"Equipment sheet: no {typeName} named '{name}' - left empty.");
            return null;
        }

        Debug.LogError($"Equipment sheet: {matches.Count} assets of type {typeName} are named '{name}' - "
                       + "left empty rather than guessing which one was meant.");
        return null;
    }

    private static void EnsureFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) { return; }

        string parent = path[..path.LastIndexOf('/')];
        string leaf = path[(path.LastIndexOf('/') + 1)..];

        if (!AssetDatabase.IsValidFolder(parent)) { EnsureFolder(parent); }

        AssetDatabase.CreateFolder(parent, leaf);
    }
}
