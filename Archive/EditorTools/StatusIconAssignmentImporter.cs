using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Applies a work order written by the Status Icon Bench page (an Artifact where every StatusType is
/// reviewed against its current glyph and reassigned from the full Dark UI pack) into StatusIcons.asset.
///
/// The page keeps its own store of overrides - nothing it does touches this project directly. Claude
/// reads that store back, writes it out as Tools/StatusIcons/assignments.json in the shape below, and
/// this command is what actually moves it into the asset every other Status Icon wiring script reads
/// from. Same three-step shape as the CardSheet/EnemySheet/LevelSheet round trip, minus the Excel leg -
/// a sprite is an object reference, not a cell value, so there is nothing for a spreadsheet to hold.
///
/// Modelled directly on StatusIconArtWiring.cs: same SerializedObject/FindProperty("entries") walk, same
/// insert-or-overwrite FindEntry, same repair-not-skip idempotency. The difference is where the mapping
/// comes from - a JSON work order here instead of a hardcoded table there - so both can keep existing
/// side by side; nothing about this file makes that one redundant.
///
/// A row whose icon name does not resolve to a sprite is logged and skipped; every other row in the
/// batch still applies. Icon names are resolved by filename stem within IconFolder, matching how the
/// page's own manifest addresses them - it never sends a path, only a name.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class StatusIconAssignmentImporter
{
    private const string StatusIconsAssetPath = "Assets/Scripts/Statuses/StatusIconData/StatusIcons.asset";
    private const string IconFolder = "Assets/Extra Assets/Dark UI/New Icons/";
    private const string WorkOrderPath = "Tools/StatusIcons/assignments.json";

    /// StatusIconNormalizer repoints StatusIcons.asset at re-rendered copies here and records which pack
    /// original each one came from. It measures raw pack art, so an assignment landed after normalizing
    /// un-normalizes that entry until Normalize is run again - worth a log line, not a refusal, since
    /// running it again is one menu click.
    private const string NormalizedFolder = "Assets/Sprites/StatusIcons";

    [Serializable]
    private class AssignmentSpec
    {
        public int type;
        public string status;
        public string icon;
    }

    [Serializable]
    private class Payload
    {
        public string generatedUtc;
        public List<AssignmentSpec> assignments;
    }

    [MenuItem("Tools/Battle HUD/Apply Status Icon Assignments")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Status icon assignments: exit Play Mode first - asset edits made in play do not persist.");
            return;
        }

        if (!File.Exists(WorkOrderPath))
        {
            Debug.LogError($"Status icon assignments: {WorkOrderPath} not found - nothing to apply. "
                + "Ask Claude to write it from the Status Icon Bench page's saved changes first.");
            return;
        }

        StatusIcons icons = AssetDatabase.LoadAssetAtPath<StatusIcons>(StatusIconsAssetPath);

        if (icons == null)
        {
            Debug.LogError($"Status icon assignments: {StatusIconsAssetPath} not found.");
            return;
        }

        Payload payload;

        try
        {
            payload = JsonUtility.FromJson<Payload>(SheetSyncProcess.ReadJsonFile(WorkOrderPath));
        }
        catch (Exception e)
        {
            Debug.LogError($"Status icon assignments: could not parse {WorkOrderPath} - {e.Message}");
            return;
        }

        if (payload?.assignments == null || payload.assignments.Count == 0)
        {
            Debug.LogWarning($"Status icon assignments: {WorkOrderPath} has no rows - nothing to apply.");
            return;
        }

        bool normalizedFolderPresent = AssetDatabase.IsValidFolder(NormalizedFolder);

        SerializedObject so = new(icons);
        SerializedProperty entries = so.FindProperty("entries");
        int added = 0;
        int updated = 0;
        int skipped = 0;

        foreach (AssignmentSpec spec in payload.assignments)
        {
            if (string.IsNullOrEmpty(spec.status) || string.IsNullOrEmpty(spec.icon))
            {
                Debug.LogError($"Status icon assignments: row for type {spec.type} is missing a status or "
                    + "icon name - skipping.");
                skipped++;
                continue;
            }

            string spritePath = IconFolder + spec.icon + ".png";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);

            if (sprite == null)
            {
                Debug.LogError($"Status icon assignments: no sprite at {spritePath} for {spec.status} - "
                    + "skipping that row.");
                skipped++;
                continue;
            }

            int existingIndex = FindEntry(entries, spec.type);

            if (existingIndex >= 0)
            {
                entries.GetArrayElementAtIndex(existingIndex).FindPropertyRelative("icon").objectReferenceValue = sprite;
                updated++;
            }
            else
            {
                int index = entries.arraySize;
                entries.InsertArrayElementAtIndex(index);
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("type").intValue = spec.type;
                entry.FindPropertyRelative("icon").objectReferenceValue = sprite;
                added++;
            }
        }

        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();

        string summary = $"Status icon assignments: done - {added} added, {updated} updated"
            + (skipped > 0 ? $", {skipped} skipped (see errors above)" : "") + ".";

        if (normalizedFolderPresent)
        {
            summary += $" {NormalizedFolder} exists - re-run Tools/Battle HUD/Normalize Status Icons so "
                + "the entries just touched get the same optical treatment as the rest.";
        }

        Debug.Log(summary);
    }

    private static int FindEntry(SerializedProperty entries, int type)
    {
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);

            if (entry.FindPropertyRelative("type").intValue == type) { return i; }
        }

        return -1;
    }
}
