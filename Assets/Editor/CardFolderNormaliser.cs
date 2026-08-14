using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Renames the Knight card folders onto the same convention Mage and Rogue already use:
/// "Defensive Buff" -> "Buff (Defensive)", "Offensive Buffs" -> "Buff (Offensive)".
///
/// Folder names carry no meaning in code - nothing reads a card's path except CardLibraryEditor's
/// root-folder filter, and DeckData/CardLibrary reference cards by GUID. So this is purely about the
/// Inspector and the design workbook reading consistently: with two spellings live, the workbook's
/// Folder column offers both in its dropdown and new cards land in whichever one you happened to pick.
///
/// Goes through AssetDatabase.MoveAsset rather than moving files on disk. A .asset carries its GUID in
/// the sibling .meta, and if Unity reimports while the pair is briefly separated it mints a fresh GUID
/// instead - which silently empties every deck referencing that card. MoveAsset moves both together
/// under Unity's own lock and never opens that window.
///
/// Idempotent: folders that are already on the convention, or absent, are skipped. Safe to re-run.
///
/// Editor-only, and not covered by Tools/compile-check.ps1 unless you pass -IncludeEditor.
/// </summary>
public static class CardFolderNormaliser
{
    private const string CardRoot = "Assets/Data/CardData";

    /// Old name -> new name. Applied inside every class folder under CardRoot that has one.
    private static readonly Dictionary<string, string> Renames = new()
    {
        { "Defensive Buff", "Buff (Defensive)" },
        { "Offensive Buffs", "Buff (Offensive)" },
        { "Offensive Buff", "Buff (Offensive)" },
        { "Defensive Buffs", "Buff (Defensive)" },
    };

    [MenuItem("Tools/Cards/Normalise Card Folders")]
    public static void Normalise()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Normalise card folders: exit Play Mode first.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(CardRoot))
        {
            Debug.LogError($"Normalise card folders: {CardRoot} not found.");
            return;
        }

        int movedAssets = 0;
        int movedFolders = 0;
        int failed = 0;

        foreach (string classFolder in AssetDatabase.GetSubFolders(CardRoot))
        {
            foreach (KeyValuePair<string, string> rename in Renames)
            {
                string from = $"{classFolder}/{rename.Key}";
                string to = $"{classFolder}/{rename.Value}";

                if (!AssetDatabase.IsValidFolder(from)) { continue; }

                if (!AssetDatabase.IsValidFolder(to))
                {
                    // No destination yet, so the whole folder can be renamed in one move - which keeps
                    // the folder's own GUID too, not just the cards inside it.
                    string error = AssetDatabase.MoveAsset(from, to);
                    if (string.IsNullOrEmpty(error))
                    {
                        Debug.Log($"Normalise card folders: {from}  ->  {to}");
                        movedFolders++;
                    }
                    else
                    {
                        Debug.LogError($"Normalise card folders: could not move {from} -> {to}: {error}");
                        failed++;
                    }
                    continue;
                }

                // Destination already exists, so merge: move the cards across one at a time, then drop
                // the empty source folder.
                string[] guids = AssetDatabase.FindAssets("t:CardData", new[] { from });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string leaf = System.IO.Path.GetFileName(path);
                    string target = $"{to}/{leaf}";

                    if (AssetDatabase.LoadAssetAtPath<CardData>(target) != null)
                    {
                        Debug.LogWarning($"Normalise card folders: {target} already exists - left {path} where it is.");
                        failed++;
                        continue;
                    }

                    string error = AssetDatabase.MoveAsset(path, target);
                    if (string.IsNullOrEmpty(error))
                    {
                        Debug.Log($"Normalise card folders: {path}  ->  {target}");
                        movedAssets++;
                    }
                    else
                    {
                        Debug.LogError($"Normalise card folders: could not move {path}: {error}");
                        failed++;
                    }
                }

                // Only remove the source folder once it is genuinely empty - never delete content.
                // Asked of the filesystem rather than AssetDatabase.FindAssets with a blank filter,
                // which is not a documented way to mean "everything" and behaves inconsistently.
                string absolute = System.IO.Path.Combine(
                    System.IO.Directory.GetCurrentDirectory(), from.Replace('/', System.IO.Path.DirectorySeparatorChar));

                bool empty = !System.IO.Directory.Exists(absolute)
                             || !System.IO.Directory.EnumerateFileSystemEntries(absolute)
                                    .Any(entry => !entry.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase));

                if (empty)
                {
                    if (AssetDatabase.DeleteAsset(from)) { Debug.Log($"Normalise card folders: removed empty {from}"); }
                }
                else
                {
                    Debug.LogWarning($"Normalise card folders: {from} still has contents - left in place.");
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (movedAssets == 0 && movedFolders == 0 && failed == 0)
        {
            Debug.Log("Normalise card folders: nothing to do - every card folder already uses the "
                      + "\"Buff (Defensive)\" / \"Buff (Offensive)\" convention.");
            return;
        }

        Debug.Log($"Normalise card folders: {movedFolders} folder(s) renamed, {movedAssets} card(s) moved"
                  + (failed > 0 ? $", {failed} FAILED - see errors above." : ".")
                  + " Re-run Tools/CardSheet/Export-CardSheet.ps1 to refresh the workbook.");
    }
}
