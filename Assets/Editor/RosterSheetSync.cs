using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The shared body of both roster sheet importers - EnemySheetImporter (Docs/EnemySheets.xlsx) and
/// BossSheetImporter (Docs/BossDesign.xlsx). Those two are reduced to their [MenuItem]s plus a
/// <see cref="Config"/>; everything below is identical between them.
///
/// This is shared rather than copy-pasted for a reason the other sheet importers do not have: enemies
/// and bosses are the SAME Character component, so the work-order payload and the eleven
/// SerializedProperty writes are identical, not merely similar. CardSheetImporter, LevelSheetImporter
/// and EquipmentSheetImporter each describe a different asset shape and rightly keep their own copies.
///
/// Same PowerShell/Unity split as those: Unity's batch mode refuses to run while the Editor holds
/// Temp/UnityLockfile, so PowerShell owns reading the .xlsx and diffing it, and Unity owns every prefab
/// write so SerializedObject and PrefabUtility stay in charge of serialization.
///
/// Health, Actions Per Turn, Brain, Targeting, Loot Table, Display Name, Role and Deck are designer-
/// authored and merge-arbitrated on the PowerShell side (a column changed on both sides since the last
/// sync is a conflict, left untouched). PowerLevel and Boss are different: they are DERIVED (Brandon's-
/// or-Estimated, and which folder the prefab lives in respectively) and always written one-way whenever
/// they differ from what is already on the prefab, regardless of whether anything merge-arbitrated also
/// changed - see BodySpec.changed, which lists both kinds together. The rest of a body tab (Card Facts,
/// the averages) is derived in a different sense - pure sheet arithmetic this importer never reads.
///
/// Bodies are never created, renamed or deleted by a sheet: EnemyRosterGenerator (see its own guardrail)
/// is what brings a new body into existence, and from then on this keeps its tuned numbers in sync.
///
/// Editor-only, and not covered by Tools/compile-check.ps1 unless you pass -IncludeEditor.
/// </summary>
public static class RosterSheetSync
{
    private const int ScriptTimeoutMs = 180_000;

    /// <summary>
    /// Everything that differs between the two roster workbooks. Mirrors Get-RosterDomain in
    /// Tools/EnemySheet/EnemySheet.Common.psm1 - the PowerShell side is the authority on paths, this is
    /// only what Unity needs to find the work order and word its own console messages.
    /// </summary>
    public class Config
    {
        /// <summary>Repo-relative work order written by the Import script, e.g. Tools/BossSheet/bosses.json.</summary>
        public string JsonPath;

        /// <summary>Repo-relative Import-*.ps1.</summary>
        public string ImportScript;

        /// <summary>Repo-relative Export-*.ps1.</summary>
        public string ExportScript;

        /// <summary>How console messages name this sheet, e.g. "Enemy sheet" / "Boss sheet".</summary>
        public string LogPrefix;

        /// <summary>Progress bar and dialog title, e.g. "Sync Bosses With Sheet".</summary>
        public string SyncTitle;

        /// <summary>The workbook this drives, e.g. Docs/BossDesign.xlsx.</summary>
        public string WorkbookName;

        /// <summary>The refresh menu path, quoted back at the user when a sync cannot resolve a difference.</summary>
        public string RefreshMenuPath;
    }

    // ----------------------------------------------------------------------------------------------
    // The work order's shape. Flat and List-based because JsonUtility handles nothing else.
    // ----------------------------------------------------------------------------------------------

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
        public int battleRole;     // BattleRole bitmask - merge-arbitrated, see the class doc comment
        public float powerLevel;   // Derived (Brandon's ?? Estimated) - always a one-way overwrite
        public bool isBoss;        // Derived (prefab's own folder) - always a one-way overwrite
        public List<string> changed = new();
    }

    // ----------------------------------------------------------------------------------------------
    // The two buttons
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The everyday action: reconcile the workbook and the prefabs in both directions. Same three-step
    /// shape as CardSheetImporter.SyncWithSheet, and the same reason the export only runs when nothing
    /// conflicted or failed - refreshing the sheet from a prefab that never received the change would
    /// silently overwrite the numbers you just typed with what Unity still has.
    /// </summary>
    public static void SyncWithSheet(Config config)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{config.LogPrefix} sync: exit Play Mode first.");
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar(config.SyncTitle, "Reading the workbook...", 0.1f);
            if (!SheetSyncProcess.Run(config.ImportScript, string.Empty, ScriptTimeoutMs)) { return; }

            EditorUtility.DisplayProgressBar(config.SyncTitle, "Applying changes to prefabs...", 0.45f);
            if (!Apply(config, out int conflicts, out int failures)) { return; }

            if (conflicts > 0 || failures > 0)
            {
                List<string> reasons = new();
                if (conflicts > 0) { reasons.Add($"{conflicts} row(s) changed on both sides"); }
                if (failures > 0) { reasons.Add($"{failures} row(s) could not be applied - see the errors above"); }

                Debug.LogWarning($"{config.LogPrefix} sync: stopped after applying everything else - {string.Join(", ", reasons)}. "
                                 + "The sheet was NOT refreshed, so your edits are still there. Fix the cause and sync again.");
                return;
            }

            EditorUtility.DisplayProgressBar(config.SyncTitle, "Refreshing the workbook...", 0.8f);
            SheetSyncProcess.Run(config.ExportScript, "-WriteBaseline", ScriptTimeoutMs);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>
    /// Escape hatch: throw away whatever is in the sheet and rebuild it from the prefabs. Only for when
    /// the sheet is known to be wrong - the normal path is the sync, which keeps both sides.
    /// </summary>
    public static void RefreshSheetFromUnity(Config config)
    {
        if (!EditorUtility.DisplayDialog(
                "Refresh Sheet From Unity",
                $"This rebuilds {config.WorkbookName} from the prefabs and DISCARDS any edit in the sheet "
                + $"that has not been synced.\n\n{config.SyncTitle} keeps both sides. Use that unless "
                + "the sheet is known to be wrong.",
                "Discard sheet edits", "Cancel"))
        {
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Refresh Sheet", "Rebuilding the workbook...", 0.5f);
            SheetSyncProcess.Run(config.ExportScript, "-Force -WriteBaseline", ScriptTimeoutMs);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ----------------------------------------------------------------------------------------------
    // Applying the plan
    // ----------------------------------------------------------------------------------------------

    private static bool Apply(Config config, out int conflictCount, out int failureCount)
    {
        conflictCount = 0;
        failureCount = 0;

        string full = Path.Combine(Directory.GetCurrentDirectory(), config.JsonPath);
        if (!File.Exists(full))
        {
            Debug.LogError($"{config.LogPrefix} sync: {config.JsonPath} not found.");
            return false;
        }

        Payload payload;
        try
        {
            payload = JsonUtility.FromJson<Payload>(SheetSyncProcess.ReadJsonFile(config.JsonPath));
        }
        catch (Exception e)
        {
            Debug.LogError($"{config.LogPrefix} sync: could not read {config.JsonPath} - {e.Message}");
            return false;
        }

        if (payload == null)
        {
            Debug.LogError($"{config.LogPrefix} sync: {config.JsonPath} was empty or malformed.");
            return false;
        }

        foreach (string problem in payload.problems) { Debug.LogWarning($"{config.LogPrefix}: {problem}"); }

        conflictCount = payload.conflicts.Count + payload.unresolved.Count;
        ReportConflicts(config, payload);

        if (payload.bodies.Count == 0)
        {
            Debug.Log($"{config.LogPrefix} sync: no sheet edits to apply.");
            return true;
        }

        int updated = 0;
        int failed = 0;

        // Not wrapped in StartAssetEditing/StopAssetEditing, on the same reasoning CardSheetImporter
        // documents: the volume here is a couple of dozen prefabs at most, and FindByName below needs
        // the asset database current.
        foreach (BodySpec spec in payload.bodies)
        {
            if (WriteBody(config, spec)) { updated++; }
            else { failed++; }
        }

        failureCount = failed;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"{config.LogPrefix} sync: {updated} updated"
                  + (failed > 0 ? $", {failed} FAILED - see errors above." : "."));

        return true;
    }

    private static void ReportConflicts(Config config, Payload payload)
    {
        foreach (ConflictSpec c in payload.conflicts)
        {
            System.Text.StringBuilder sb = new();
            sb.AppendLine($"{config.LogPrefix} CONFLICT: '{c.key}' changed in BOTH Unity and the sheet since the "
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
            Debug.LogWarning($"{config.LogPrefix}: '{c.key}' differs between Unity and the sheet, but there is no "
                             + "baseline recording which side moved, so neither was written. Use "
                             + $"{config.RefreshMenuPath} if the prefab is correct.");
        }
    }

    /// <summary>
    /// Writes one body's synced fields onto its prefab. Every referenced asset (targeting, loot, every
    /// deck card) is resolved BEFORE anything is written, so a typo cannot leave the prefab half-updated.
    /// </summary>
    private static bool WriteBody(Config config, BodySpec spec)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.assetPath) == null)
        {
            Debug.LogError($"{config.LogPrefix}: no prefab at {spec.assetPath} for '{spec.prefab}'.");
            return false;
        }

        TargetingPattern targeting = null;
        if (!string.IsNullOrWhiteSpace(spec.targeting))
        {
            targeting = FindByName<TargetingPattern>(spec.targeting);
            if (targeting == null)
            {
                Debug.LogError($"{config.LogPrefix}: '{spec.prefab}' names targeting '{spec.targeting}', which was not found.");
                return false;
            }
        }

        LootTable loot = null;
        if (!string.IsNullOrWhiteSpace(spec.lootTable))
        {
            loot = FindByName<LootTable>(spec.lootTable);
            if (loot == null)
            {
                Debug.LogError($"{config.LogPrefix}: '{spec.prefab}' names loot table '{spec.lootTable}', which was not found.");
                return false;
            }
        }

        List<CardData> deck = new();
        foreach (string name in spec.deck)
        {
            CardData card = FindByName<CardData>(name);
            if (card == null)
            {
                Debug.LogError($"{config.LogPrefix}: '{spec.prefab}' deck names '{name}', which was not found - the prefab was left unchanged.");
                return false;
            }
            deck.Add(card);
        }

        using PrefabUtility.EditPrefabContentsScope scope = new(spec.assetPath);
        GameObject root = scope.prefabContentsRoot;
        Character character = root.GetComponent<Character>();

        if (character == null)
        {
            Debug.LogError($"{config.LogPrefix}: {spec.assetPath} has no Character component - the template must have changed.");
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
        so.FindProperty("battleRole").intValue = spec.battleRole;
        so.FindProperty("powerLevel").floatValue = spec.powerLevel;
        so.FindProperty("isBoss").boolValue = spec.isBoss;

        SerializedProperty deckProp = so.FindProperty("deck");
        deckProp.arraySize = deck.Count;
        for (int i = 0; i < deck.Count; i++) { deckProp.GetArrayElementAtIndex(i).objectReferenceValue = deck[i]; }

        so.ApplyModifiedProperties();

        Debug.Log($"{config.LogPrefix}: updated {spec.assetPath}"
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
