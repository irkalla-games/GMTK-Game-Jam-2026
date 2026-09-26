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

    private const string DebugRunKey = "settings.debugRunEnabled";

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

    /// <summary>
    /// Whether Play drops straight into the debug testbed instead of a real run - one 8×8 board, the
    /// whole party, every totem in hand reach and a card that refills energy on demand.
    ///
    /// Defaults **off**, unlike the two above: this is the only setting whose on-state is not a way to
    /// play the game, and a player who has never touched it must never end up in it. Checked before
    /// TutorialEnabled in MainMenu.playButton, so turning it on wins over a tutorial that is also on.
    ///
    /// Always false in a Playable build (BuildMode.DebugTools), whatever was saved. Every debug tool -
    /// the testbed, DebugPanel, the pause menu's hint - asks this and nothing else, so this one getter is
    /// what shuts all of them out.
    /// </summary>
    public static bool DebugRunEnabled
    {
        get => BuildMode.DebugTools && PlayerPrefs.GetInt(DebugRunKey, 0) != 0;

        set
        {
            PlayerPrefs.SetInt(DebugRunKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    // ---- Audio --------------------------------------------------------------------------------
    // Stored as plain 0..1 linear values, which is what a Slider hands over and what a player means by
    // "half volume". AudioManager is what turns them into AudioSource.volume; nothing here knows an
    // AudioSource exists, so this file stays a store rather than becoming a second place that decides
    // how loud things are.

    private const string MasterVolumeKey = "settings.volume.master";

    private const string MusicVolumeKey = "settings.volume.music";

    private const string SfxVolumeKey = "settings.volume.sfx";

    /// <summary>
    /// Overall level, multiplied into both channels below. Defaults to 0.8 rather than 1 so there is
    /// somewhere to go up as well as down.
    /// </summary>
    public static float MasterVolume
    {
        get => PlayerPrefs.GetFloat(MasterVolumeKey, 0.8f);

        set => SetVolume(MasterVolumeKey, value);
    }

    public static float MusicVolume
    {
        get => PlayerPrefs.GetFloat(MusicVolumeKey, 0.7f);

        set => SetVolume(MusicVolumeKey, value);
    }

    public static float SfxVolume
    {
        get => PlayerPrefs.GetFloat(SfxVolumeKey, 0.8f);

        set => SetVolume(SfxVolumeKey, value);
    }

    /// Clamped on the way in rather than on the way out: a value written out of range would otherwise
    /// persist and every reader would have to defend against it.
    private static void SetVolume(string key, float value)
    {
        PlayerPrefs.SetFloat(key, Mathf.Clamp01(value));
        PlayerPrefs.Save();
    }

    // ---- Display ------------------------------------------------------------------------------

    private const string FullscreenKey = "settings.display.fullscreen";

    private const string ResolutionKey = "settings.display.resolution";

    /// <summary>
    /// Defaults on, which is what a player launching a game expects. DisplaySettings is what applies
    /// it; this only remembers the choice.
    /// </summary>
    public static bool Fullscreen
    {
        get => PlayerPrefs.GetInt(FullscreenKey, 1) != 0;

        set
        {
            PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Index into DisplaySettings.Resolutions, or -1 for "whatever the display is already using".
    ///
    /// -1 rather than a stored width and height, and -1 as the default, because the list is built from
    /// Screen.resolutions at runtime: a saved index taken on one monitor can be out of range on another,
    /// and DisplaySettings.Apply treats anything out of range as -1 rather than refusing to start.
    /// </summary>
    public static int ResolutionIndex
    {
        get => PlayerPrefs.GetInt(ResolutionKey, -1);

        set
        {
            PlayerPrefs.SetInt(ResolutionKey, value);
            PlayerPrefs.Save();
        }
    }
}
