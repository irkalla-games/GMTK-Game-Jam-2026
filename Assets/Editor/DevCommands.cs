using UnityEditor;
using UnityEngine;

/// <summary>
/// The testing/dev shelf: commands that mutate PlayerPrefs-backed unlock state or top up level
/// authoring, safe to run at any time against any project state. Pulled out of CharacterSelectWiring
/// and ProgressionContentGenerator when those files were archived to Archive/EditorTools/ - the
/// wiring and content-authoring those files did was one-shot, but these seven commands are the reason
/// DeckUnlocks.Unlock, CharacterUnlocks.Unlock and DifficultyProgress.Reset have callers at all, and
/// stay useful for as long as the game does.
///
/// Editor-only, and only covered by Tools/compile-check.ps1 when it is run with -IncludeEditor.
/// </summary>
public static class DevCommands
{
    private const string LadderPath = "Assets/Data/DifficultyLadder/StandardLadder.asset";

    /// Mirrors CharacterRoster's default MinPartySize/MaxPartySize (2..4). Not read from the roster
    /// asset itself - a level's spawn cells are board authoring, independent of whatever roster
    /// happens to be assigned at the menu.
    private const int MaxPartySize = 4;

    /// <summary>
    /// Adds cells to every LevelData short of MaxPartySize, so a party larger than a level's authored
    /// spawn points still has somewhere requested for each member - BattleManager.SpawnParty falls back
    /// to GridManager.NearestFreeSpawnTile for anyone past the authored list, so a cell landing near the
    /// edge of the board (or nudged off it) is not a bug, just a starting request the fallback resolves.
    /// A level author can always drag the new cells elsewhere by hand afterwards; the point is nobody
    /// loses a 3rd or 4th party member for want of a spawn cell existing at all.
    /// </summary>
    [MenuItem("Tools/Level/Top Up Party Spawn Cells")]
    public static void TopUpPartySpawnCells()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Dev commands: exit Play Mode first - asset edits made in play are not reliable.");
            return;
        }

        int updated = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:LevelData"))
        {
            LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(guid));

            if (level == null) { continue; }

            SerializedObject so = new(level);
            SerializedProperty cells = so.FindProperty("partySpawnCells");

            if (cells.arraySize >= MaxPartySize) { continue; }

            int previousCount = cells.arraySize;

            Vector2Int last = previousCount > 0
                ? cells.GetArrayElementAtIndex(previousCount - 1).vector2IntValue
                : Vector2Int.zero;

            cells.arraySize = MaxPartySize;

            for (int i = previousCount; i < MaxPartySize; i++)
            {
                cells.GetArrayElementAtIndex(i).vector2IntValue = last + new Vector2Int(i - previousCount + 1, 0);
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(level);
            updated++;

            Debug.Log($"Dev commands: {level.name} partySpawnCells {previousCount} -> {MaxPartySize}.");
        }

        AssetDatabase.SaveAssets();

        Debug.Log(updated > 0
            ? $"Dev commands: topped up {updated} LevelData asset(s)."
            : "Dev commands: every LevelData already has enough party spawn cells - nothing to do.");
    }

    // ---- Deck unlocks ---------------------------------------------------------------------------

    /// Testing-only. Unlocks every DeckData.Locked deck for whoever's PlayerPrefs this Editor is
    /// running under, so the select screen's lock UI can be exercised without a real unlock hook.
    [MenuItem("Tools/Unlocks/Unlock All Decks")]
    public static void UnlockAllDecks() => ForEveryDeck(DeckUnlocks.Unlock, "unlocked");

    /// Re-locks everything Unlock All Decks unlocked, for the same testing reason.
    [MenuItem("Tools/Unlocks/Clear All Deck Unlocks")]
    public static void ClearAllDeckUnlocks() => ForEveryDeck(DeckUnlocks.Lock, "cleared the unlock state of");

    private static void ForEveryDeck(System.Action<DeckData> apply, string pastTenseVerb)
    {
        int count = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:DeckData"))
        {
            DeckData deck = AssetDatabase.LoadAssetAtPath<DeckData>(AssetDatabase.GUIDToAssetPath(guid));

            if (deck == null) { continue; }

            apply(deck);
            count++;
        }

        Debug.Log($"Dev commands: {pastTenseVerb} {count} deck(s).");
    }

    // ---- Character unlocks and difficulty progress -----------------------------------------------

    [MenuItem("Tools/Unlocks/Unlock All Characters")]
    private static void UnlockAllCharacters() =>
        ForEveryCharacter(option => CharacterUnlocks.Unlock(option), "unlocked");

    [MenuItem("Tools/Unlocks/Clear All Character Unlocks")]
    private static void ClearAllCharacterUnlocks() =>
        ForEveryCharacter(CharacterUnlocks.Lock, "cleared the unlock state of");

    [MenuItem("Tools/Unlocks/Unlock All Difficulty Tiers")]
    private static void UnlockAllTiers()
    {
        DifficultyLadder ladder = AssetDatabase.LoadAssetAtPath<DifficultyLadder>(LadderPath);

        if (ladder == null)
        {
            Debug.LogWarning($"Dev commands: no ladder at {LadderPath} - run Generate Difficulty Ladder "
                             + "first (see Archive/EditorTools/ProgressionContentGenerator.cs).");
            return;
        }

        DifficultyProgress.UnlockAll(ladder);

        Debug.Log($"Dev commands: every rung up to {ladder.NameAt(ladder.Tiers.Count - 1)} is selectable.");
    }

    [MenuItem("Tools/Unlocks/Reset Difficulty Progress")]
    private static void ResetDifficultyProgress()
    {
        DifficultyProgress.Reset();

        Debug.Log("Dev commands: difficulty progress cleared - Normal only, as on a fresh save.");
    }

    private static void ForEveryCharacter(System.Action<CharacterOption> apply, string pastTenseVerb)
    {
        int count = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:CharacterOption"))
        {
            CharacterOption option =
                AssetDatabase.LoadAssetAtPath<CharacterOption>(AssetDatabase.GUIDToAssetPath(guid));

            if (option == null) { continue; }

            apply(option);
            count++;
        }

        Debug.Log($"Dev commands: {pastTenseVerb} {count} character(s).");
    }
}
