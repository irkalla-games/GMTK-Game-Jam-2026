/// <summary>
/// Which kind of build this is. Debug carries the debug tools - the Debug Run setting, the testbed it
/// starts, the backquote DebugPanel, the pause menu's hint about it, and a choice of party size on
/// character select. Playable carries none of them, plays at CharacterRoster.PlayablePartySize, and is
/// what gets uploaded.
///
/// Decided at compile time by the DEBUG_TOOLS scripting define, which Tools/Build Mode adds to or removes
/// from the active build profile (see BuildModeMenu). Fixed when the scripts compile rather than read from
/// a setting, so a Playable build has no switch left to find: GameSettings.DebugRunEnabled answers false
/// whatever PlayerPrefs holds, and on a web build PlayerPrefs is browser storage a player can edit.
///
/// Editor Play Mode compiles with the same define, so switching to Playable and pressing Play shows what
/// an uploaded build will. Anything under Assets/Editor never reaches a build either way and ignores this.
///
/// A property rather than a const on purpose: `BuildMode.DebugTools && ...` against a const false is
/// compiler warning CS0429 in every Playable compile.
/// </summary>
public static class BuildMode
{
#if DEBUG_TOOLS
    public static bool DebugTools => true;
#else
    public static bool DebugTools => false;
#endif
}
