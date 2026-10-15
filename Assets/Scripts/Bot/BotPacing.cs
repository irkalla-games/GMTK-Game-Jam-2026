#if UNITY_EDITOR || BOT_RUNNER
using UnityEngine;

/// <summary>
/// How fast the game runs while the bot drives it. Every setting touched is saved first and put back by
/// Restore - called when the pilot is destroyed, and again by the Editor bridge on returning to Edit
/// Mode, so a crashed batch cannot leave the Editor muted or fast-forwarding.
///
/// Turbo leans on Time.captureDeltaTime: game time advances by a fixed step every frame, however long
/// the frame really took, and frames run as fast as the CPU allows - the mechanism Unity Recorder uses.
/// Every WaitForSeconds in the action queue (0.15-0.4 s) then finishes in a frame or two. Nothing here
/// touches Time.timeScale, which TimeFreeze owns (it forces it back to 1 whenever a notification closes).
///
/// Deliberately nothing that lives in ProjectSettings - fixedDeltaTime, physics simulation mode,
/// QualitySettings in the Editor. Changing those during Play Mode can dirty the settings assets, and a
/// balance run must not leave a diff behind.
/// </summary>
public static class BotPacing
{
    private static bool saved;
    private static float savedCaptureDelta;
    private static int savedTargetFrameRate;
    private static float savedVolume;
    private static LogType savedLogFilter;
    private static int savedVSync;

    private static BotSpeed speed;
    private static float watchSpeed = 1f;
    private static float lastRealtime;

    public static bool IsTurbo => saved && speed == BotSpeed.Turbo;

    public static void Apply(BotJob job)
    {
        Save();

        speed = job.speed;
        watchSpeed = Mathf.Clamp(job.watchSpeed, 1f, 4f);
        lastRealtime = Time.realtimeSinceStartup;

        if (speed == BotSpeed.Turbo)
        {
            Time.captureDeltaTime = Mathf.Clamp(job.turboStep, 0.02f, 0.25f);
            Application.targetFrameRate = -1;

            // A player's vsync is its own business; the Editor's lives in QualitySettings.asset.
            if (!Application.isEditor) { QualitySettings.vSyncCount = 0; }

            AudioListener.volume = 0f;

            // The game logs every intent and every play; at thousands of frames a second that logging is
            // most of the cost. Warnings and errors still reach Application.logMessageReceived.
            Debug.unityLogger.filterLogType = LogType.Warning;
        }
        else
        {
            Time.captureDeltaTime = 0f;
        }
    }

    /// Watch above 1x: feed captureDeltaTime a multiple of the real frame time each frame, which speeds
    /// the game up without touching timeScale. Measured from realtimeSinceStartup rather than
    /// unscaledDeltaTime, which captureDeltaTime itself would feed back into.
    public static void PerFrame()
    {
        if (!saved || speed != BotSpeed.Watch) { return; }

        float now = Time.realtimeSinceStartup;
        float real = now - lastRealtime;
        lastRealtime = now;

        Time.captureDeltaTime = watchSpeed > 1.01f ? Mathf.Clamp(real * watchSpeed, 0.001f, 0.1f) : 0f;
    }

    public static void Restore()
    {
        if (!saved) { return; }

        Time.captureDeltaTime = savedCaptureDelta;
        Application.targetFrameRate = savedTargetFrameRate;

        if (!Application.isEditor) { QualitySettings.vSyncCount = savedVSync; }

        AudioListener.volume = savedVolume;
        Debug.unityLogger.filterLogType = savedLogFilter;

        saved = false;
    }

    private static void Save()
    {
        if (saved) { return; }

        savedCaptureDelta = Time.captureDeltaTime;
        savedTargetFrameRate = Application.targetFrameRate;
        savedVSync = QualitySettings.vSyncCount;
        savedVolume = AudioListener.volume;
        savedLogFilter = Debug.unityLogger.filterLogType;
        saved = true;
    }
}
#endif
