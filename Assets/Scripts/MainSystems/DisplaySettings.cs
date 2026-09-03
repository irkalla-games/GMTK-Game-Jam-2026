using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns GameSettings' stored display choices into actual Screen calls, and owns the one list of
/// resolutions the settings screens offer.
///
/// Static and stateless for the same reason GameSettings is: there is nothing to update per frame and
/// nothing to keep alive across a scene load. The resolution list IS cached, but only because
/// Screen.resolutions allocates a fresh array on every access and a dropdown asks for it repeatedly -
/// the cache is a copy of something the OS owns, not state this class is authoritative for.
///
/// Note the deliberate silence about aspect ratio. CameraFrame keeps a fixed 19.2 x 10.8 world frame
/// visible and every canvas is on Expand, so a non-16:9 resolution letterboxes on its own and there is
/// nothing here to correct for - see CLAUDE.md on why that pairing is load-bearing.
/// </summary>
public static class DisplaySettings
{
    private static List<Resolution> cached;

    /// <summary>
    /// Every distinct width x height the display supports, smallest first.
    ///
    /// Deduplicated by size: Screen.resolutions lists one entry per refresh rate, so a 144Hz monitor
    /// reports 1920x1080 four or five times over and a raw list would fill a dropdown with identical
    /// rows. The highest refresh rate for each size wins, since that is the entry a player picking
    /// "1920x1080" means.
    /// </summary>
    public static IReadOnlyList<Resolution> Resolutions
    {
        get
        {
            if (cached != null) { return cached; }

            Dictionary<(int Width, int Height), Resolution> best = new();

            foreach (Resolution candidate in Screen.resolutions)
            {
                (int, int) key = (candidate.width, candidate.height);

                if (!best.TryGetValue(key, out Resolution existing)
                    || RefreshOf(candidate) > RefreshOf(existing))
                {
                    best[key] = candidate;
                }
            }

            cached = new List<Resolution>(best.Values);

            cached.Sort(static (a, b) =>
            {
                int byWidth = a.width.CompareTo(b.width);

                return byWidth != 0 ? byWidth : a.height.CompareTo(b.height);
            });

            // A display that reports nothing - which happens in some headless and remote-desktop
            // sessions - would otherwise leave the dropdown empty and Apply with nothing to pick.
            if (cached.Count == 0) { cached.Add(Screen.currentResolution); }

            return cached;
        }
    }

    /// <summary>
    /// Human-readable labels for Resolutions, in the same order - what a dropdown shows.
    /// </summary>
    public static List<string> ResolutionLabels()
    {
        List<string> labels = new();

        foreach (Resolution resolution in Resolutions)
        {
            labels.Add($"{resolution.width} x {resolution.height}");
        }

        return labels;
    }

    /// <summary>
    /// The index currently in effect: the saved one when it is valid, otherwise whichever entry matches
    /// the resolution the screen is actually running at.
    ///
    /// A saved index is only meaningful against the monitor it was saved on. Moving the game to a
    /// smaller display, or unplugging one of two, can leave it pointing past the end of a shorter list -
    /// so it is range-checked on every read rather than trusted.
    /// </summary>
    public static int CurrentIndex()
    {
        int saved = GameSettings.ResolutionIndex;

        if (saved >= 0 && saved < Resolutions.Count) { return saved; }

        for (int i = 0; i < Resolutions.Count; i++)
        {
            if (Resolutions[i].width == Screen.width && Resolutions[i].height == Screen.height)
            {
                return i;
            }
        }

        return Resolutions.Count - 1;
    }

    /// <summary>
    /// Applies the saved fullscreen and resolution choices to the actual window.
    ///
    /// FullScreenWindow rather than ExclusiveFullScreen: borderless matches the resolution the desktop
    /// is already at, so alt-tabbing does not force a mode switch, and it is what a 2D game wants on a
    /// modern display.
    /// </summary>
    public static void Apply()
    {
        Resolution target = Resolutions[CurrentIndex()];

        FullScreenMode mode = GameSettings.Fullscreen
            ? FullScreenMode.FullScreenWindow
            : FullScreenMode.Windowed;

        Screen.SetResolution(target.width, target.height, mode);
    }

    /// <summary>
    /// Unity 6 replaced Resolution.refreshRate (int) with refreshRateRatio (a rational). Read through
    /// one helper so the deprecated property is not scattered across the file.
    /// </summary>
    private static double RefreshOf(Resolution resolution) => resolution.refreshRateRatio.value;
}
