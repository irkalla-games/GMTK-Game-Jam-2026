using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Rebuilds Assets/Data/EnemyRegistry.asset from the actual prefabs under Assets/Prefabs/{Enemies,
/// Bosses,Allies} - the same folder scope and Tutorial-variant exclusion Tools/EnemySheet's
/// Get-DiscoveredRoster uses in EnemySheet.Common.psm1, kept in step by hand since this is C# scanning
/// prefabs directly rather than sharing that PowerShell function.
///
/// Repairs rather than skips (see CLAUDE.md): reuses the existing asset if present, but rewrites every
/// entry on every run, so a stale registry can always be fixed by running this again rather than only
/// by deleting the asset first.
/// </summary>
public static class EnemyRegistryGenerator
{
    private const string RegistryPath = "Assets/Data/EnemyRegistry.asset";

    private static readonly (string folder, bool eligible)[] Sources =
    {
        ("Assets/Prefabs/Enemies", true),
        ("Assets/Prefabs/Bosses", true),
        // Allies are listed (summon chains reference them, e.g. SkeletonAlly) but never drawn at
        // random - a summoned ally is not itself an encounter pick.
        ("Assets/Prefabs/Allies", false),
    };

    [MenuItem("Tools/Enemies/Rebuild Enemy Registry")]
    public static void Rebuild()
    {
        EnemyRegistry registry = AssetDatabase.LoadAssetAtPath<EnemyRegistry>(RegistryPath);

        if (registry == null)
        {
            registry = ScriptableObject.CreateInstance<EnemyRegistry>();
            AssetDatabase.CreateAsset(registry, RegistryPath);
        }

        List<EnemyRegistryEntry> entries = new();

        foreach ((string folder, bool eligible) in Sources)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // Same exclusion Get-DiscoveredRoster applies: a Tutorial/* prefab variant carries no
                // Character component of its own, only a PrefabInstance modifications list pointing at
                // the base prefab - which this same scan already covers under its own folder.
                if (path.Replace('\\', '/').Contains("/Tutorial/")) { continue; }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponent<Character>() == null) { continue; }

                entries.Add(new EnemyRegistryEntry { prefab = prefab, eligibleForRandomDraw = eligible });
            }
        }

        entries.Sort((a, b) => string.Compare(a.prefab.name, b.prefab.name, System.StringComparison.Ordinal));

        SerializedObject so = new(registry);
        SerializedProperty entriesProp = so.FindProperty("entries");
        entriesProp.arraySize = entries.Count;

        for (int i = 0; i < entries.Count; i++)
        {
            SerializedProperty entry = entriesProp.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("prefab").objectReferenceValue = entries[i].prefab;
            entry.FindPropertyRelative("eligibleForRandomDraw").boolValue = entries[i].eligibleForRandomDraw;
        }

        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();

        int eligibleCount = entries.Count(e => e.eligibleForRandomDraw);
        Debug.Log($"Enemy registry: {entries.Count} bodies ({eligibleCount} eligible for random draw).");
    }
}
