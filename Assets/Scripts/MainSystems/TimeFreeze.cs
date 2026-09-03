using UnityEngine;

/// <summary>
/// Reference-counted ownership of Time.timeScale, so more than one thing can want the game frozen
/// without either one thawing it out from under the other.
///
/// Before this, NotificationManager wrote Time.timeScale directly - 0 on Show, 1 on Hide. That is
/// correct while it is the only writer and wrong the moment there is a second: a pause menu opened
/// underneath a notification would be thawed by dismissing the notification, even though the pause is
/// still up. It is the same problem BattleManager.InputLocked solves by being a pulled query rather
/// than a pushed bool - "one bool cannot remember that two things wanted it held" - except timeScale is
/// a single global that cannot be computed on demand, so it gets a count instead.
///
/// Static, with no MonoBehaviour: there is nothing to update per frame, and a freeze has to outlive any
/// one panel that asked for it.
/// </summary>
public static class TimeFreeze
{
    private static int holders;

    public static bool Frozen => holders > 0;

    /// <summary>
    /// Claims a freeze. Every Acquire must be matched by exactly one Release.
    /// </summary>
    public static void Acquire()
    {
        holders++;

        Apply();
    }

    /// <summary>
    /// Drops one claim. Clamped at zero so an unmatched Release - a panel closing twice, a Hide called
    /// on a notification that was never shown - cannot drive the count negative and leave the next
    /// genuine Acquire unable to freeze anything.
    /// </summary>
    public static void Release()
    {
        holders = Mathf.Max(0, holders - 1);

        Apply();
    }

    /// <summary>
    /// Drops every claim at once. **Call this before any SceneManager.LoadScene.**
    ///
    /// holders is a static, so it survives a scene load while the panels that incremented it do not.
    /// Leaving a claim behind freezes the next scene solid with nothing left alive to release it - a
    /// frozen Main Menu is a hard lockup with no way out, which is exactly why BattleManager.Finish has
    /// always set timeScale back to 1 before loading.
    /// </summary>
    public static void ReleaseAll()
    {
        holders = 0;

        Apply();
    }

    private static void Apply()
    {
        Time.timeScale = holders > 0 ? 0f : 1f;
    }
}
