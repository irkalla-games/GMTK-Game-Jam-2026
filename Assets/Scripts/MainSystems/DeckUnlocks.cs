using UnityEngine;

/// <summary>
/// Which starting decks the player has earned, across every run and every session. Read by the
/// character-select screen to decide whether a DeckData.Locked deck may be picked yet.
///
/// PlayerPrefs rather than a settings ScriptableObject, for the same two reasons GameSettings gives:
/// a SO written at runtime persists into the .asset on disk once Play Mode exits, so testing an unlock
/// in the Editor would silently edit the asset - and a built game has no writable Assets folder, so it
/// would not survive a player's install either.
///
/// Static, with no MonoBehaviour and no scene presence - nothing here updates per frame or needs to
/// stay alive across a load, PlayerPrefs already persists on its own.
///
/// Nothing in the game awards an unlock yet - see DeckData.Locked's tooltip and the plan this shipped
/// under. Unlock/Lock are the public surface a future reward hook (a run-complete bonus, say) calls
/// into; today only the Tools/Unlocks menu commands in CharacterSelectWiring call them, for testing.
/// </summary>
public static class DeckUnlocks
{
    private const string KeyPrefix = "unlock.deck.";

    /// <summary>
    /// Whether `deck` may be picked at the select screen right now. A null deck or an unlocked-by-
    /// default one (Locked == false) is always available - the PlayerPrefs lookup only happens for a
    /// deck that actually gates on it.
    /// </summary>
    public static bool IsUnlocked(DeckData deck) =>
        deck == null || !deck.Locked || PlayerPrefs.GetInt(KeyPrefix + deck.UnlockId, 0) != 0;

    public static void Unlock(DeckData deck)
    {
        if (deck == null) { return; }

        PlayerPrefs.SetInt(KeyPrefix + deck.UnlockId, 1);

        // Written through immediately rather than left to Unity's own flush on quit - same reasoning
        // as GameSettings.TutorialEnabled, an earned unlock should survive an alt-F4.
        PlayerPrefs.Save();
    }

    /// Testing-only counterpart to Unlock - re-locks a deck that was unlocked for a playtest.
    public static void Lock(DeckData deck)
    {
        if (deck == null) { return; }

        PlayerPrefs.DeleteKey(KeyPrefix + deck.UnlockId);
        PlayerPrefs.Save();
    }
}
