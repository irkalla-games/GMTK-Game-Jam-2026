using UnityEditor;

/// <summary>
/// The two Tools menu entries for Docs/EnemySheets.xlsx - the enemy and ally roster.
///
/// All the work is in <see cref="RosterSheetSync"/>, shared with <see cref="BossSheetImporter"/>:
/// enemies and bosses are the same Character component, so the work order and every prefab write are
/// identical and only the paths and labels below differ. Bosses were split into their own workbook so a
/// boss retune and an enemy retune keep separate baselines and cannot conflict with each other; which
/// workbook owns a prefab is decided purely by its folder under Assets/Prefabs.
///
/// Editor-only, and not covered by Tools/compile-check.ps1 unless you pass -IncludeEditor.
/// </summary>
public static class EnemySheetImporter
{
    private static readonly RosterSheetSync.Config Enemies = new()
    {
        JsonPath = "Tools/EnemySheet/enemies.json",
        ImportScript = "Tools/EnemySheet/Import-EnemySheet.ps1",
        ExportScript = "Tools/EnemySheet/Export-EnemySheet.ps1",
        LogPrefix = "Enemy sheet",
        SyncTitle = "Sync Enemies With Sheet",
        WorkbookName = "Docs/EnemySheets.xlsx",
        RefreshMenuPath = "Tools > Enemies > Refresh Sheet From Unity",
    };

    /// <summary>The everyday action: reconcile the workbook and the prefabs in both directions.</summary>
    [MenuItem("Tools/Sync Enemies With Sheet", priority = -900)]
    public static void SyncWithSheet() => RosterSheetSync.SyncWithSheet(Enemies);

    /// <summary>Escape hatch: rebuild the sheet from the prefabs, discarding unsynced sheet edits.</summary>
    [MenuItem("Tools/Enemies/Refresh Sheet From Unity (discards sheet edits)")]
    public static void RefreshSheetFromUnity() => RosterSheetSync.RefreshSheetFromUnity(Enemies);
}
