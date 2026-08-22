using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Applies Docs/EnemySheets.xlsx edits to the enemy/boss/ally prefabs, via the enemies.json work order
/// that Tools/EnemySheet/Import-EnemySheet.ps1 writes.
///
/// Same split as CardSheetImporter and the same reason: Unity's batch mode refuses to run while the
/// Editor holds Temp/UnityLockfile, so PowerShell owns reading the .xlsx and diffing it, and Unity owns
/// every prefab write so SerializedObject and PrefabUtility stay in charge of serialization.
///
/// Only Health, Actions Per Turn, Brain, Targeting, Loot Table, Display Name and Deck are synced - the
/// rest of a body tab (Card Facts, the averages, Estimated Power Level) is derived and this importer
/// never reads it. Bodies are never created, renamed or deleted by the sheet: EnemyRosterGenerator (see
/// its own guardrail) is what brings a new body into existence, and from then on this is what keeps its
/// tuned numbers in sync. A tab with no matching prefab is impossible in normal use - the export only
/// ever writes one tab per prefab Get-DiscoveredRoster finds - so Import-EnemySheet.ps1 reports it as a
/// problem rather than silently skipping it.
///
/// Editor-only, and not covered by Tools/compile-check.ps1 - verify by focusing the Editor and reading
/// the console.
/// </summary>
public static class EnemySheetImporter
{
    private const string JsonPath = "Tools/EnemySheet/enemies.json";
    private const string ImportScript = "Tools/EnemySheet/Import-EnemySheet.ps1";
    private const string ExportScript = "Tools/EnemySheet/Export-EnemySheet.ps1";
    private const int ScriptTimeoutMs = 180_000;

    // ------------------------------------------------------------------------------------------
    // The enemies.json shape. Flat and List-based because JsonUtility handles nothing else.
    // ------------------------------------------------------------------------------------------

    [Serializable]
    private class Payload
    {
        public string generatedUtc;
        public string source;
        public List<BodySpec> bodies = new();
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
    private class BodySpec
    {
        public string prefab;
        public string assetPath;
        public string guid;
        public string displayName;
        public int maxHealth;
        public int actionPoints;
        public int brain;
        public string targeting;   // TargetingPattern asset name, or "" for none
        public string lootTable;   // LootTable asset name, or "" to inherit the level's
        public List<string> deck = new();
        public List<string> changed = new();
    }

    // ------------------------------------------------------------------------------------------
    // The button
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The everyday action: reconcile the workbook and the prefabs in both directions. Same three-step
    /// shape as CardSheetImporter.SyncWithSheet, and the same reason the export only runs when nothing
    /// conflicted or failed - refreshing the sheet from a prefab that never received the change would
    /// silently overwrite the wording you just typed with what Unity still has.
    /// </summary>
    [MenuItem("Tools/Sync Enemies With Sheet", priority = -900)]
    public static void SyncWithSheet()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Enemy sheet sync: exit Play Mode first.");
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Sync Enemies With Sheet", "Reading the workbook...", 0.1f);
            if (!SheetSyncProcess.Run(ImportScript, string.Empty, ScriptTimeoutMs)) { return; }

            EditorUtility.DisplayProgressBar("Sync Enemies With Sheet", "Applying changes to prefabs...", 0.45f);
            if (!Apply(out int conflicts, out int failures)) { return; }

            if (conflicts > 0 || failures > 0)
            {
                List<string> reasons = new();
                if (conflicts > 0) { reasons.Add($"{conflicts} row(s) changed on both sides"); }
                if (failures > 0) { reasons.Add($"{failures} row(s) could not be applied - see the errors above"); }

                Debug.LogWarning($"Enemy sheet sync: stopped after applying everything else - {string.Join(", ", reasons)}. "
                                 + "The sheet was NOT refreshed, so your edits are still there. Fix the cause and sync again.");
                return;
            }

            EditorUtility.DisplayProgressBar("Sync Enemies With Sheet", "Refreshing the workbook...", 0.8f);
            SheetSyncProcess.Run(ExportScript, "-WriteBaseline", ScriptTimeoutMs);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>
    /// Escape hatch: throw away whatever is in the sheet and rebuild it from the prefabs. Only for when
    /// the sheet is known to be wrong - the normal path is Sync Enemies With Sheet, which keeps both.
    /// </summary>
    [MenuItem("Tools/Enemies/Refresh Sheet From Unity (discards sheet edits)")]
    public static void RefreshSheetFromUnity()
    {
        if (!EditorUtility.DisplayDialog(
                "Refresh Sheet From Unity",
                "This rebuilds Docs/EnemySheets.xlsx from the prefabs and DISCARDS any edit in the sheet "
                + "that has not been synced.\n\nSync Enemies With Sheet keeps both sides. Use that unless "
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
            Debug.LogError($"Enemy sheet sync: {JsonPath} not found.");
            return false;
        }

        Payload payload;
        try
        {
            payload = JsonUtility.FromJson<Payload>(SheetSyncProcess.ReadJsonFile(JsonPath));
        }
        catch (Exception e)
        {
            Debug.LogError($"Enemy sheet sync: could not read {JsonPath} - {e.Message}");
            return false;
        }

        if (payload == null)
        {
            Debug.LogError($"Enemy sheet sync: {JsonPath} was empty or malformed.");
            return false;
        }

        foreach (string problem in payload.problems) { Debug.LogWarning($"Enemy sheet: {problem}"); }

        conflictCount = payload.conflicts.Count + payload.unresolved.Count;
        ReportConflicts(payload);

        if (payload.bodies.Count == 0)
        {
            Debug.Log("Enemy sheet sync: no sheet edits to apply.");
            return true;
        }

        int updated = 0;
        int failed = 0;

        // Not wrapped in StartAssetEditing/StopAssetEditing, on the same reasoning CardSheetImporter
        // documents: the volume here is a couple of dozen prefabs at most, and FindByName below needs
        // the asset database current.
        foreach (BodySpec spec in payload.bodies)
        {
            if (WriteBody(spec)) { updated++; }
            else { failed++; }
        }

        failureCount = failed;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Enemy sheet sync: {updated} updated"
                  + (failed > 0 ? $", {failed} FAILED - see errors above." : "."));

        return true;
    }

    private static void ReportConflicts(Payload payload)
    {
        foreach (ConflictSpec c in payload.conflicts)
        {
            System.Text.StringBuilder sb = new();
            sb.AppendLine($"Enemy sheet CONFLICT: '{c.key}' changed in BOTH Unity and the sheet since the "
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
            Debug.LogWarning($"Enemy sheet: '{c.key}' differs between Unity and the sheet, but there is no "
                             + "baseline recording which side moved, so neither was written. Use "
                             + "Tools > Enemies > Refresh Sheet From Unity if the prefab is correct.");
        }
    }

    /// <summary>
    /// Writes one body's synced fields onto its prefab. Every referenced asset (targeting, loot, every
    /// deck card) is resolved BEFORE anything is written, so a typo cannot leave the prefab half-updated.
    /// </summary>
    private static bool WriteBody(BodySpec spec)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.assetPath) == null)
        {
            Debug.LogError($"Enemy sheet: no prefab at {spec.assetPath} for '{spec.prefab}'.");
            return false;
        }

        TargetingPattern targeting = null;
        if (!string.IsNullOrWhiteSpace(spec.targeting))
        {
            targeting = FindByName<TargetingPattern>(spec.targeting);
            if (targeting == null)
            {
                Debug.LogError($"Enemy sheet: '{spec.prefab}' names targeting '{spec.targeting}', which was not found.");
                return false;
            }
        }

        LootTable loot = null;
        if (!string.IsNullOrWhiteSpace(spec.lootTable))
        {
            loot = FindByName<LootTable>(spec.lootTable);
            if (loot == null)
            {
                Debug.LogError($"Enemy sheet: '{spec.prefab}' names loot table '{spec.lootTable}', which was not found.");
                return false;
            }
        }

        List<CardData> deck = new();
        foreach (string name in spec.deck)
        {
            CardData card = FindByName<CardData>(name);
            if (card == null)
            {
                Debug.LogError($"Enemy sheet: '{spec.prefab}' deck names '{name}', which was not found - the prefab was left unchanged.");
                return false;
            }
            deck.Add(card);
        }

        using PrefabUtility.EditPrefabContentsScope scope = new(spec.assetPath);
        GameObject root = scope.prefabContentsRoot;
        Character character = root.GetComponent<Character>();

        if (character == null)
        {
            Debug.LogError($"Enemy sheet: {spec.assetPath} has no Character component - the template must have changed.");
            return false;
        }

        SerializedObject so = new(character);

        so.FindProperty("displayName").stringValue = spec.displayName;
        so.FindProperty("maxHealth").intValue = spec.maxHealth;
        // The live readout, not authoring data (see Character.Health's doc comment) - kept in step here
        // purely so the Inspector does not show a stale value until the next Play Mode Awake.
        so.FindProperty("<Health>k__BackingField").intValue = spec.maxHealth;
        so.FindProperty("actionPoints").intValue = spec.actionPoints;
        so.FindProperty("brain").intValue = spec.brain;
        so.FindProperty("targetingPattern").objectReferenceValue = targeting;
        so.FindProperty("lootTable").objectReferenceValue = loot;

        SerializedProperty deckProp = so.FindProperty("deck");
        deckProp.arraySize = deck.Count;
        for (int i = 0; i < deck.Count; i++) { deckProp.GetArrayElementAtIndex(i).objectReferenceValue = deck[i]; }

        so.ApplyModifiedProperties();

        Debug.Log($"Enemy sheet: updated {spec.assetPath}"
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
