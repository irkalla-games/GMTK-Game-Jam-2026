using UnityEngine;

/// <summary>
/// Player-facing options that outlive both a run and a session. Read by the Main Menu, which is the
/// only place they can be changed - a run snapshots what it needs at StartRun so nothing can shift
/// underneath a battle already in progress.
///
/// PlayerPrefs rather than a settings ScriptableObject, for two reasons. A SO written at runtime
/// persists into the .asset on disk once Play Mode exits, so testing the toggle in the Editor would
/// silently edit the asset - the trap that applies to every ScriptableObject here. And a built game
/// has no writable Assets folder, so it would not survive a player's install either.
///
/// Static, with no MonoBehaviour and no scene presence, because there is nothing to update per frame
/// and nothing to keep alive across a load: PlayerPrefs is already the thing that persists.
/// </summary>
public static class GameSettings
{
    private const string TutorialKey = "settings.tutorialEnabled";

    private const string IntentDamageKey = "settings.showIntentDamage";

    /// <summary>
    /// Whether Play should run the tutorial on the first level. Defaults on: a player who has never
    /// touched the toggle is by definition the one who needs it.
    /// </summary>
    public static bool TutorialEnabled
    {
        get => PlayerPrefs.GetInt(TutorialKey, 1) != 0;

        set
        {
            PlayerPrefs.SetInt(TutorialKey, value ? 1 : 0);

            // Written through immediately rather than left to Unity's own flush on quit - a player
            // who alt-F4s from the menu still gets the choice they just made.
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Whether an enemy's overhead intent icon also shows the raw damage its locked attack card would
    /// swing for - see Card.OutgoingDamage and CharacterOverheadViewer. Defaults on.
    ///
    /// Unlike TutorialEnabled, this is read live rather than snapshotted into RunManager at StartRun:
    /// it changes nothing about how a battle plays, only what the player is shown, so there is nothing
    /// a run in progress needs protecting from.
    /// </summary>
    public static bool ShowIntentDamage
    {
        get => PlayerPrefs.GetInt(IntentDamageKey, 1) != 0;

        set
        {
            PlayerPrefs.SetInt(IntentDamageKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
