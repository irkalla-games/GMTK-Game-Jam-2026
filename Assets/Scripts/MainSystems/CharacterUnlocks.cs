using UnityEngine;

/// <summary>
/// Which heroes the player has earned, across every run and every session. Read by the
/// character-select screen to decide whether a CharacterOption.Locked hero may be picked yet.
///
/// The character counterpart to DeckUnlocks, deliberately identical in shape rather than merged with
/// it: a deck and a hero are unlocked by different things and stored under different key prefixes,
/// and one class answering both would have to be told which kind it was being asked about at every
/// call site.
///
/// PlayerPrefs rather than a settings ScriptableObject, for the two reasons GameSettings gives: a SO
/// written at runtime persists into the .asset on disk once Play Mode exits, so testing an unlock in
/// the Editor would silently edit the asset - and a built game has no writable Assets folder, so it
/// would not survive a player's install either.
///
/// Static, with no MonoBehaviour and no scene presence - nothing here updates per frame or needs to
/// stay alive across a load, PlayerPrefs already persists on its own.
///
/// Awarded by RunManager.AwardRunComplete when a campaign is cleared; the Tools/Unlocks menu commands
/// in CharacterSelectWiring drive it by hand for testing.
/// </summary>
public static class CharacterUnlocks
{
    private const string KeyPrefix = "unlock.character.";

    /// <summary>
    /// Whether `option` may be picked at the select screen right now. A null option or an
    /// unlocked-by-default one (Locked == false) is always available - the PlayerPrefs lookup only
    /// happens for a hero that actually gates on it.
    /// </summary>
    public static bool IsUnlocked(CharacterOption option) =>
        option == null || !option.Locked || PlayerPrefs.GetInt(KeyPrefix + option.UnlockId, 0) != 0;

    /// <summary>
    /// Earns `option`, and reports whether that was news. False means it was already unlocked (or was
    /// never locked), which is what lets a run-complete award name only what actually changed rather
    /// than congratulating a player for a hero they have had for three runs.
    /// </summary>
    public static bool Unlock(CharacterOption option)
    {
        if (option == null) { return false; }

        if (IsUnlocked(option)) { return false; }

        PlayerPrefs.SetInt(KeyPrefix + option.UnlockId, 1);

        // Written through immediately rather than left to Unity's own flush on quit - same reasoning
        // as GameSettings.TutorialEnabled, an earned unlock should survive an alt-F4.
        PlayerPrefs.Save();

        return true;
    }

    /// Testing-only counterpart to Unlock - re-locks a hero that was unlocked for a playtest.
    public static void Lock(CharacterOption option)
    {
        if (option == null) { return; }

        PlayerPrefs.DeleteKey(KeyPrefix + option.UnlockId);
        PlayerPrefs.Save();
    }
}
