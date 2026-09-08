using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// The Google Sheets side of a sheet sync: pulling phone edits into a workbook before Import-*.ps1 reads
/// it, and pushing the workbook's current cells back out to Google after Export-*.ps1 rebuilds it.
///
/// Every Sync ... With Sheet button (CardSheetImporter, RosterSheetSync, LevelSheetImporter,
/// EquipmentSheetImporter) calls Pull first and Push last, so pressing the one button you already press
/// is what picks up a phone edit and hands the result back to the phone - nothing else about those
/// buttons' shape changes. A failed pull aborts the sync outright rather than continuing against a stale
/// workbook, which is the one thing this bridge exists to prevent; a failed push only logs a warning,
/// since by then every asset write already landed and the workbook already reflects it - only the
/// phone's copy is stale, and the next sync's pull would refuse a real edit rather than silently losing
/// one, so pushing again later is always safe.
///
/// Tools/GoogleBridge/Push-GoogleSheet.ps1 and Pull-GoogleSheet.ps1 are the ones with the actual bridge
/// logic (a cell-level mirror against each workbook's Google Sheets copy, guarded by a per-tab content
/// hash and a row-identity check - see that module's header comment); this class is only the menu items
/// and the same shell-out SheetSyncProcess.Run already provides for every other sheet tool.
///
/// Editor-only, and not covered by Tools/compile-check.ps1 unless you pass -IncludeEditor.
/// </summary>
public static class GoogleBridgeSync
{
    private const int ScriptTimeoutMs = 180_000;
    private const string PullScript = "Tools/GoogleBridge/Pull-GoogleSheet.ps1";
    private const string PushScript = "Tools/GoogleBridge/Push-GoogleSheet.ps1";
    private const string ConnectScript = "Tools/GoogleBridge/Connect-GoogleSheets.ps1";

    /// <summary>The five workbooks Tools/GoogleBridge/workbooks.psd1 knows about.</summary>
    public enum Workbook { Cards, Enemies, Bosses, Levels, Equipment }

    /// <summary>
    /// Pulls one workbook's phone edits into its .xlsx. Called at the START of every Sync ... With
    /// Sheet, before Import-*.ps1 - a failure here means the caller should stop rather than sync
    /// against a workbook that may be missing a phone edit.
    /// </summary>
    public static bool Pull(Workbook workbook) => SheetSyncProcess.Run(PullScript, $"-Workbook {workbook}", ScriptTimeoutMs);

    /// <summary>
    /// Pushes one workbook's current cells to Google. Called at the END of every Sync ... With Sheet,
    /// right after Export-*.ps1 - deliberately only reached when nothing conflicted or failed, same
    /// reasoning as why the export itself is skipped in that case: refreshing Google from a workbook
    /// that never actually received the sheet's own edits would show the phone a state it never agreed
    /// to.
    /// </summary>
    public static bool Push(Workbook workbook) => SheetSyncProcess.Run(PushScript, $"-Workbook {workbook}", ScriptTimeoutMs);

    // ------------------------------------------------------------------------------------------
    // Menu
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// One-time setup: opens a REAL interactive console (not the captured, -NonInteractive shell-out
    /// every other sheet tool uses) because Connect-GoogleSheets.ps1 prompts for a client id/secret and
    /// waits on a browser redirect - neither works with output piped back into the Unity console.
    /// </summary>
    [MenuItem("Tools/Google Sheets/Connect...")]
    public static void Connect()
    {
        string root = System.IO.Directory.GetCurrentDirectory();
        string script = System.IO.Path.Combine(root, ConnectScript.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(script))
        {
            Debug.LogError($"Google Sheets: {ConnectScript} not found.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -NoExit -File \"{script}\"",
                WorkingDirectory = root,
                UseShellExecute = true,
            });
            Debug.Log("Google Sheets: opened a terminal for Connect-GoogleSheets.ps1 - follow the prompts there.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Google Sheets: could not open a terminal - {e.Message}. Run Tools/GoogleBridge/Connect-GoogleSheets.ps1 yourself instead.");
        }
    }

    [MenuItem("Tools/Google Sheets/Pull All")]
    public static void PullAll() => RunAll(Pull, "pull");

    [MenuItem("Tools/Google Sheets/Push All")]
    public static void PushAll() => RunAll(Push, "push");

    /// <summary>Escape hatch: pull even though a tab's hash guard would refuse it, discarding whatever
    /// changed in the .xlsx since the last push. Never overrides the row-identity guard - a misaligned
    /// row has no safe "discard" side, so that one always refuses regardless.</summary>
    [MenuItem("Tools/Google Sheets/Force Pull All (discards Excel edits)")]
    public static void ForcePullAll()
    {
        if (!EditorUtility.DisplayDialog(
                "Force Pull All",
                "This applies every workbook's Google Sheets edits even where the workbook changed since "
                + "the last push, DISCARDING that Excel-side change.\n\nUse this only when you know the "
                + "sheet, not Excel, is the one that's right.",
                "Discard Excel edits", "Cancel"))
        {
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Force Pull All", "Pulling from Google Sheets...", 0.5f);
            foreach (Workbook wb in (Workbook[])System.Enum.GetValues(typeof(Workbook)))
            {
                SheetSyncProcess.Run(PullScript, $"-Workbook {wb} -Force", ScriptTimeoutMs);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static void RunAll(System.Func<Workbook, bool> action, string verb)
    {
        try
        {
            Workbook[] all = (Workbook[])System.Enum.GetValues(typeof(Workbook));
            for (int i = 0; i < all.Length; i++)
            {
                EditorUtility.DisplayProgressBar($"Google Sheets: {verb} all", $"{all[i]}...", (float)i / all.Length);
                action(all[i]);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }
}
