using UnityEditor;

/// <summary>
/// The two Tools menu entries for Docs/BossDesign.xlsx - the eight prefabs under Assets/Prefabs/Bosses.
///
/// All the work is in <see cref="RosterSheetSync"/>, shared with <see cref="EnemySheetImporter"/> - see
/// that class for why the two rosters share one engine, and Tools/BossSheet/Export-BossSheet.ps1 for
/// the two things that are genuinely boss-specific (a PowerLevel tab mirrored from the enemy workbook,
/// and the read-only Summoned tab that lets a boss score the enemy it summons without owning its tab).
///
/// Priority -850 slots this between Sync Enemies With Sheet (-900) and Sync Levels With Sheet (-800),
/// keeping the 100-apart spacing the four sheet tools already use.
///
/// Editor-only, and not covered by Tools/compile-check.ps1 unless you pass -IncludeEditor.
/// </summary>
public static class BossSheetImporter
{
    private static readonly RosterSheetSync.Config Bosses = new()
    {
        JsonPath = "Tools/BossSheet/bosses.json",
        ImportScript = "Tools/BossSheet/Import-BossSheet.ps1",
        ExportScript = "Tools/BossSheet/Export-BossSheet.ps1",
        LogPrefix = "Boss sheet",
        SyncTitle = "Sync Bosses With Sheet",
        WorkbookName = "Docs/BossDesign.xlsx",
        RefreshMenuPath = "Tools > Bosses > Refresh Sheet From Unity",
        GoogleWorkbook = GoogleBridgeSync.Workbook.Bosses,
    };

    /// <summary>The everyday action: reconcile the workbook and the prefabs in both directions.</summary>
    [MenuItem("Tools/Sync Bosses With Sheet", priority = -850)]
    public static void SyncWithSheet() => RosterSheetSync.SyncWithSheet(Bosses);

    /// <summary>Escape hatch: rebuild the sheet from the prefabs, discarding unsynced sheet edits.</summary>
    [MenuItem("Tools/Bosses/Refresh Sheet From Unity (discards sheet edits)")]
    public static void RefreshSheetFromUnity() => RosterSheetSync.RefreshSheetFromUnity(Bosses);
}
