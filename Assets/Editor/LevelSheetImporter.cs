using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Applies Docs/LevelDesign.xlsx edits to the LevelData and RunData assets, via the levels.json work
/// order that Tools/LevelSheet/Import-LevelSheet.ps1 writes.
///
/// Same three-part split as CardSheetImporter and EnemySheetImporter: PowerShell reads the .xlsx and
/// diffs it (Unity's batch mode refuses to run while the Editor holds Temp/UnityLockfile), Unity owns
/// every asset write. LevelData and RunData are plain ScriptableObjects, not prefabs, so writing one is
/// a plain SerializedObject edit - no PrefabUtility scope needed.
///
/// A level's board (enemies, waves, party spawn cells, deck overrides) is applied as one unit: the
/// payload's `placements` list is regrouped here into `enemies` (turn 0) and `waves` (one EnemyWave per
/// distinct turn > 0, sorted ascending purely for a readable .asset diff - TryNextWave does not care
/// about wave order). Levels and runs are never created or renamed by the sheet - see
/// Get-DiscoveredRoster and Read-LevelAsset in Tools/LevelSheet/LevelSheet.Common.psm1, which is what
/// decides the tab set on the export side.
///
/// Editor-only, and not covered by Tools/compile-check.ps1 by default - verify with -IncludeEditor.
/// </summary>
public static class LevelSheetImporter
{
    private const string JsonPath = "Tools/LevelSheet/levels.json";
    private const string ImportScript = "Tools/LevelSheet/Import-LevelSheet.ps1";
    private const string ExportScript = "Tools/LevelSheet/Export-LevelSheet.ps1";
    private const int ScriptTimeoutMs = 180_000;

    // ------------------------------------------------------------------------------------------
    // The levels.json shape. Flat and List-based because JsonUtility handles nothing else.
    // ------------------------------------------------------------------------------------------

    [Serializable]
    private class Payload
    {
        public string generatedUtc;
        public string source;
        public List<LevelSpec> levels = new();
        public List<RunSpec> runs = new();
        public List<ConflictSpec> conflicts = new();
        public List<ConflictSpec> unresolved = new();
        public List<string> problems = new();
    }

    [Serializable]
    private class ConflictSpec
    {
        public string key;
        public string tab;
        public List<ConflictColumn> columns = new();
    }

    [Serializable]
    private class ConflictColumn
    {
        public string column;
        public string unity;
        public string sheet;
        public string wasLast;
    }

    [Serializable]
    private class PlacementSpec
    {
        public int turn;   // 0 = the opening roster; > 0 = the wave arriving that turn
        public int col;    // one-based, as authored on the sheet
        public int row;
        public string prefab;
        public List<string> deck = new();
    }

    [Serializable]
    private class SpawnSpec
    {
        public int col;
        public int row;
    }

    [Serializable]
    private class LevelSpec
    {
        public string level;
        public string assetPath;
        public string guid;
        public int boardWidth;
        public int boardHeight;
        public int turnsToSurvive;
        public int handSize;
        public string lootTable;
        public string clearRewardTable;
        public List<string> tileSets = new();
        public List<PlacementSpec> placements = new();
        public List<SpawnSpec> partySpawn = new();
        public List<string> changed = new();
    }

    [Serializable]
    private class RunSpec
    {
        public string run;
        public string assetPath;
        public string guid;
        public List<string> levels = new();
        public bool carryDamageBetweenLevels;
        public List<string> changed = new();
    }

    // ------------------------------------------------------------------------------------------
    // The button
    // ------------------------------------------------------------------------------------------

    [MenuItem("Tools/Sync Levels With Sheet", priority = -800)]
    public static void SyncWithSheet()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Level sheet sync: exit Play Mode first.");
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Sync Levels With Sheet", "Reading the workbook...", 0.1f);
            if (!SheetSyncProcess.Run(ImportScript, string.Empty, ScriptTimeoutMs)) { return; }

            EditorUtility.DisplayProgressBar("Sync Levels With Sheet", "Applying changes to assets...", 0.45f);
            if (!Apply(out int conflicts, out int failures)) { return; }

            if (conflicts > 0 || failures > 0)
            {
                List<string> reasons = new();
                if (conflicts > 0) { reasons.Add($"{conflicts} row(s) changed on both sides"); }
                if (failures > 0) { reasons.Add($"{failures} row(s) could not be applied - see the errors above"); }

                Debug.LogWarning($"Level sheet sync: stopped after applying everything else - {string.Join(", ", reasons)}. "
                                 + "The sheet was NOT refreshed, so your edits are still there. Fix the cause and sync again.");
                return;
            }

            EditorUtility.DisplayProgressBar("Sync Levels With Sheet", "Refreshing the workbook...", 0.8f);
            SheetSyncProcess.Run(ExportScript, "-WriteBaseline", ScriptTimeoutMs);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("Tools/Levels/Refresh Sheet From Unity (discards sheet edits)")]
    public static void RefreshSheetFromUnity()
    {
        if (!EditorUtility.DisplayDialog(
                "Refresh Sheet From Unity",
                "This rebuilds Docs/LevelDesign.xlsx from the assets and DISCARDS any edit in the sheet "
                + "that has not been synced.\n\nSync Levels With Sheet keeps both sides. Use that unless "
                + "the sheet is known to be wrong.",
                "Discard sheet edits", "Cancel"))
        {
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Refresh Sheet", "Rebuilding the workbook...", 0.5f);
            SheetSyncProcess.Run(ExportScript, "-Force -WriteBaseline", ScriptTimeoutMs);
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
            Debug.LogError($"Level sheet sync: {JsonPath} not found.");
            return false;
        }

        Payload payload;
        try
        {
            payload = JsonUtility.FromJson<Payload>(SheetSyncProcess.ReadJsonFile(JsonPath));
        }
        catch (Exception e)
        {
            Debug.LogError($"Level sheet sync: could not read {JsonPath} - {e.Message}");
            return false;
        }

        if (payload == null)
        {
            Debug.LogError($"Level sheet sync: {JsonPath} was empty or malformed.");
            return false;
        }

        foreach (string problem in payload.problems) { Debug.LogWarning($"Level sheet: {problem}"); }

        conflictCount = payload.conflicts.Count + payload.unresolved.Count;
        ReportConflicts(payload);

        if (payload.levels.Count == 0 && payload.runs.Count == 0)
        {
            Debug.Log("Level sheet sync: no sheet edits to apply.");
            return true;
        }

        int updated = 0;
        int failed = 0;

        foreach (LevelSpec spec in payload.levels)
        {
            if (WriteLevel(spec)) { updated++; } else { failed++; }
        }
        foreach (RunSpec spec in payload.runs)
        {
            if (WriteRun(spec)) { updated++; } else { failed++; }
        }

        failureCount = failed;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Level sheet sync: {updated} updated" + (failed > 0 ? $", {failed} FAILED - see errors above." : "."));

        return true;
    }

    private static void ReportConflicts(Payload payload)
    {
        foreach (ConflictSpec c in payload.conflicts)
        {
            System.Text.StringBuilder sb = new();
            sb.AppendLine($"Level sheet CONFLICT: '{c.key}' changed in BOTH Unity and the sheet since the "
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
            Debug.LogWarning($"Level sheet: '{c.key}' differs between Unity and the sheet, but there is no "
                             + "baseline recording which side moved, so neither was written. Use "
                             + "Tools > Levels > Refresh Sheet From Unity if the asset is correct.");
        }
    }

    /// <summary>
    /// Writes one level's synced fields. Every referenced asset - loot tables, tile sets, every
    /// placement's prefab, every deck override's cards - is resolved BEFORE anything is written, so a
    /// typo cannot leave the level half-updated.
    /// </summary>
    private static bool WriteLevel(LevelSpec spec)
    {
        LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(spec.assetPath);
        if (level == null)
        {
            Debug.LogError($"Level sheet: no LevelData at {spec.assetPath} for '{spec.level}'.");
            return false;
        }

        LootTable loot = null;
        if (!string.IsNullOrWhiteSpace(spec.lootTable))
        {
            loot = FindByName<LootTable>(spec.lootTable);
            if (loot == null)
            {
                Debug.LogError($"Level sheet: '{spec.level}' names loot table '{spec.lootTable}', which was not found.");
                return false;
            }
        }

        LootTable clearReward = null;
        if (!string.IsNullOrWhiteSpace(spec.clearRewardTable))
        {
            clearReward = FindByName<LootTable>(spec.clearRewardTable);
            if (clearReward == null)
            {
                Debug.LogError($"Level sheet: '{spec.level}' names clear reward table '{spec.clearRewardTable}', which was not found.");
                return false;
            }
        }

        List<TileSetData> tileSets = new();
        foreach (string name in spec.tileSets)
        {
            TileSetData ts = FindByName<TileSetData>(name);
            if (ts == null)
            {
                Debug.LogError($"Level sheet: '{spec.level}' names tile set '{name}', which was not found.");
                return false;
            }
            tileSets.Add(ts);
        }

        List<ResolvedPlacement> resolved = new();
        foreach (PlacementSpec p in spec.placements)
        {
            GameObject prefab = FindByName<GameObject>(p.prefab);
            if (prefab == null)
            {
                Debug.LogError($"Level sheet: '{spec.level}' places '{p.prefab}', which was not found - the level was left unchanged.");
                return false;
            }

            List<CardData> deck = new();
            foreach (string cardName in p.deck)
            {
                CardData card = FindByName<CardData>(cardName);
                if (card == null)
                {
                    Debug.LogError($"Level sheet: '{spec.level}' deck override names '{cardName}', which was not found - the level was left unchanged.");
                    return false;
                }
                deck.Add(card);
            }

            resolved.Add(new ResolvedPlacement { Turn = p.turn, Col = p.col, Row = p.row, Prefab = prefab, Deck = deck });
        }

        SerializedObject so = new(level);

        SerializedProperty boardSize = so.FindProperty("boardSize");
        boardSize.FindPropertyRelative("x").intValue = spec.boardWidth;
        boardSize.FindPropertyRelative("y").intValue = spec.boardHeight;

        so.FindProperty("turnsToSurvive").intValue = spec.turnsToSurvive;
        so.FindProperty("handSize").intValue = spec.handSize;
        so.FindProperty("lootTable").objectReferenceValue = loot;
        so.FindProperty("clearRewardTable").objectReferenceValue = clearReward;

        SerializedProperty tileSetsProp = so.FindProperty("tileSets");
        tileSetsProp.arraySize = tileSets.Count;
        for (int i = 0; i < tileSets.Count; i++) { tileSetsProp.GetArrayElementAtIndex(i).objectReferenceValue = tileSets[i]; }

        List<ResolvedPlacement> start = resolved.Where(r => r.Turn <= 0).ToList();
        var waveGroups = resolved.Where(r => r.Turn > 0).GroupBy(r => r.Turn).OrderBy(g => g.Key).ToList();

        WritePlacementArray(so.FindProperty("enemies"), start);

        SerializedProperty wavesProp = so.FindProperty("waves");
        wavesProp.arraySize = waveGroups.Count;
        for (int i = 0; i < waveGroups.Count; i++)
        {
            SerializedProperty waveElem = wavesProp.GetArrayElementAtIndex(i);
            waveElem.FindPropertyRelative("turn").intValue = waveGroups[i].Key;
            WritePlacementArray(waveElem.FindPropertyRelative("enemies"), waveGroups[i].ToList());
        }

        SerializedProperty spawnProp = so.FindProperty("partySpawnCells");
        spawnProp.arraySize = spec.partySpawn.Count;
        for (int i = 0; i < spec.partySpawn.Count; i++)
        {
            SerializedProperty cell = spawnProp.GetArrayElementAtIndex(i);
            cell.FindPropertyRelative("x").intValue = spec.partySpawn[i].col - 1;
            cell.FindPropertyRelative("y").intValue = spec.partySpawn[i].row - 1;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(level);

        Debug.Log($"Level sheet: updated {spec.assetPath}"
                  + (spec.changed is { Count: > 0 } ? $" ({string.Join("; ", spec.changed)})" : ""));

        return true;
    }

    private struct ResolvedPlacement
    {
        public int Turn;
        public int Col;
        public int Row;
        public GameObject Prefab;
        public List<CardData> Deck;
    }

    /// <summary>
    /// Writes one List&lt;EnemyPlacement&gt; - shared between LevelData.enemies (turn 0) and one
    /// EnemyWave.enemies, the same nested-struct shape. Cell coordinates convert one-based (the sheet,
    /// the Inspector) to zero-based (what the asset stores) here, in exactly this one place.
    /// </summary>
    private static void WritePlacementArray(SerializedProperty arrayProp, List<ResolvedPlacement> items)
    {
        arrayProp.arraySize = items.Count;
        for (int i = 0; i < items.Count; i++)
        {
            SerializedProperty elem = arrayProp.GetArrayElementAtIndex(i);
            elem.FindPropertyRelative("prefab").objectReferenceValue = items[i].Prefab;

            SerializedProperty cell = elem.FindPropertyRelative("cell");
            cell.FindPropertyRelative("x").intValue = items[i].Col - 1;
            cell.FindPropertyRelative("y").intValue = items[i].Row - 1;

            SerializedProperty deckProp = elem.FindPropertyRelative("deckOverride");
            deckProp.arraySize = items[i].Deck.Count;
            for (int j = 0; j < items[i].Deck.Count; j++)
            {
                deckProp.GetArrayElementAtIndex(j).objectReferenceValue = items[i].Deck[j];
            }
        }
    }

    private static bool WriteRun(RunSpec spec)
    {
        RunData run = AssetDatabase.LoadAssetAtPath<RunData>(spec.assetPath);
        if (run == null)
        {
            Debug.LogError($"Level sheet: no RunData at {spec.assetPath} for '{spec.run}'.");
            return false;
        }

        List<LevelData> levels = new();
        foreach (string name in spec.levels)
        {
            LevelData level = FindByName<LevelData>(name);
            if (level == null)
            {
                Debug.LogError($"Level sheet: run '{spec.run}' names level '{name}', which was not found - the run was left unchanged.");
                return false;
            }
            levels.Add(level);
        }

        SerializedObject so = new(run);
        SerializedProperty levelsProp = so.FindProperty("levels");
        levelsProp.arraySize = levels.Count;
        for (int i = 0; i < levels.Count; i++) { levelsProp.GetArrayElementAtIndex(i).objectReferenceValue = levels[i]; }

        so.FindProperty("carryDamageBetweenLevels").boolValue = spec.carryDamageBetweenLevels;

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(run);

        Debug.Log($"Level sheet: updated {spec.assetPath}"
                  + (spec.changed is { Count: > 0 } ? $" ({string.Join("; ", spec.changed)})" : ""));

        return true;
    }

    private static T FindByName<T>(string name) where T : UnityEngine.Object
    {
        foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null && asset.name == name) { return asset; }
        }

        return null;
    }
}
