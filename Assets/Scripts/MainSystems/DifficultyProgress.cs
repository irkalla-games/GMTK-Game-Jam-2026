using UnityEngine;

/// <summary>
/// How far up the difficulty ladder the player has climbed, and which rung they last chose. Survives
/// every run and every session.
///
/// Same PlayerPrefs-and-static shape as GameSettings and DeckUnlocks, and for the same reasons - see
/// GameSettings' summary. This is progress rather than a setting, but it is stored and read
/// identically, and a second mechanism would be a second thing to keep working.
///
/// Two numbers, not one. HighestUnlocked is earned and only ever goes up; Selected is a preference
/// the player changes freely below it. Collapsing them would mean picking Normal for a relaxed run
/// threw away the fact that Hard IV had been beaten.
/// </summary>
public static class DifficultyProgress
{
    private const string HighestUnlockedKey = "progress.difficulty.highestUnlocked";

    private const string SelectedKey = "progress.difficulty.selected";

    /// <summary>
    /// The hardest tier index the player may currently pick. 0 - Normal only - on a fresh save, which
    /// is what makes the difficulty selector stay hidden until there is a choice to make.
    /// </summary>
    public static int HighestUnlocked => Mathf.Max(0, PlayerPrefs.GetInt(HighestUnlockedKey, 0));

    /// <summary>
    /// The tier the next run starts on.
    ///
    /// Clamped on *read* as well as on write. A save carrying a tier that is no longer reachable -
    /// progress cleared for a playtest, a ladder that lost a rung - must never start an ungated run,
    /// and the getter is the only place that can promise it for every caller.
    /// </summary>
    public static int Selected
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(SelectedKey, 0), 0, HighestUnlocked);

        set
        {
            PlayerPrefs.SetInt(SelectedKey, Mathf.Clamp(value, 0, HighestUnlocked));
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Records a cleared run on `tier`, opening the rung above it, and reports whether that was news.
    ///
    /// Max rather than assignment: clearing Normal after having already beaten Hard III must not walk
    /// the ladder back down. False means this tier had already been cleared, which is what lets the
    /// victory modal stay quiet about difficulty on a repeat clear.
    /// </summary>
    public static bool RecordClear(int tier)
    {
        int opened = Mathf.Max(0, tier) + 1;

        if (opened <= HighestUnlocked) { return false; }

        PlayerPrefs.SetInt(HighestUnlockedKey, opened);
        PlayerPrefs.Save();

        return true;
    }

    /// Testing-only: back to a fresh save's Normal-only state.
    public static void Reset()
    {
        PlayerPrefs.DeleteKey(HighestUnlockedKey);
        PlayerPrefs.DeleteKey(SelectedKey);
        PlayerPrefs.Save();
    }

    /// Testing-only: opens every rung of `ladder` without playing them.
    public static void UnlockAll(DifficultyLadder ladder)
    {
        if (ladder == null) { return; }

        PlayerPrefs.SetInt(HighestUnlockedKey, Mathf.Max(0, ladder.Tiers.Count - 1));
        PlayerPrefs.Save();
    }
}
