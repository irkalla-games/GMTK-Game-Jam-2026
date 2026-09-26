using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Compilation;
using UnityEngine;

/// <summary>
/// Tools/Build Mode: switches the project between Debug - the debug tools compiled in, Development Build
/// on - and Playable, which has neither and is what gets uploaded. The checkmark shows the current mode.
///
/// Both halves are written to the active build profile, where File/Build Profiles shows them as its
/// Scripting Defines and its Development Build box - or to Player Settings for the active target when no
/// profile is active. Either way they are project files, so the mode is committed: whatever mode is
/// pushed is the mode the next person to pull gets.
///
/// The define is DEBUG_TOOLS - see BuildMode for what it gates and why that is decided at compile time.
/// Switching therefore recompiles scripts before Play Mode shows any difference.
///
/// Deliberately no build hook. Building while in Debug mode is allowed and makes a Debug build; this menu
/// is the whole mechanism.
///
/// Development Build matters beyond the watermark: com.unity.pipeline, the package the Unity CLI drives
/// this Editor through, compiles its runtime and ~9 MB of Roslyn into every Development build and into
/// nothing else. A Playable build contains none of it.
/// </summary>
public static class BuildModeMenu
{
    public const string DebugToolsDefine = "DEBUG_TOOLS";

    private const string DebugItem = "Tools/Build Mode/Debug";

    private const string PlayableItem = "Tools/Build Mode/Playable";

    [MenuItem(DebugItem, priority = 0)]
    private static void SwitchToDebug() => Apply(debug: true);

    [MenuItem(PlayableItem, priority = 1)]
    private static void SwitchToPlayable() => Apply(debug: false);

    [MenuItem(DebugItem, isValidateFunction: true)]
    private static bool CanSwitchToDebug() => RefreshChecks();

    [MenuItem(PlayableItem, isValidateFunction: true)]
    private static bool CanSwitchToPlayable() => RefreshChecks();

    /// <summary>
    /// Whether the active profile - or Player Settings, with none active - carries DEBUG_TOOLS. Read from
    /// the settings rather than from BuildMode.DebugTools, which is only as fresh as the last compile.
    /// </summary>
    public static bool IsDebug => ReadDefines().Contains(DebugToolsDefine);

    /// Validate functions run every time the menu opens, which makes this the place that keeps both
    /// checkmarks honest. Greyed out in Play Mode and mid-compile, where a define change lands half-way.
    private static bool RefreshChecks()
    {
        bool debug = IsDebug;

        Menu.SetChecked(DebugItem, debug);
        Menu.SetChecked(PlayableItem, !debug);

        return !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;
    }

    /// <summary>
    /// Writes both halves of the mode on every run, even when the define already matches - a profile whose
    /// Development Build box was flipped by hand is put back in step rather than left disagreeing with it.
    /// </summary>
    private static void Apply(bool debug)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Build Mode: exit Play Mode first - a define change recompiles scripts.");
            return;
        }

        List<string> defines = ReadDefines();

        defines.RemoveAll(define => define == DebugToolsDefine);

        if (debug) { defines.Add(DebugToolsDefine); }

        // With a profile active this is the profile's own Development Build box, not a global setting.
        EditorUserBuildSettings.development = debug;

        BuildProfile profile = BuildProfile.GetActiveBuildProfile();

        if (profile != null)
        {
            profile.scriptingDefines = defines.ToArray();

            // m_HasScriptingDefines only decides whether File/Build Profiles shows the Scripting Defines
            // section - compilation reads the list regardless - but a define nobody can see there is a
            // define nobody knows is set. Through SerializedObject because the flag has no public setter.
            SerializedObject serialized = new(profile);
            serialized.FindProperty("m_HasScriptingDefines").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }
        else
        {
            PlayerSettings.SetScriptingDefineSymbols(ActiveTarget(), defines.ToArray());
        }

        CompilationPipeline.RequestScriptCompilation();

        string where = profile != null
            ? $"build profile '{profile.name}'"
            : $"Player Settings ({ActiveTarget().TargetName})";

        Debug.Log($"Build Mode: {(debug ? "Debug" : "Playable")}. {DebugToolsDefine} "
                  + $"{(debug ? "added to" : "removed from")} {where}, Development Build {(debug ? "on" : "off")}. "
                  + "Scripts are recompiling.");
    }

    private static List<string> ReadDefines()
    {
        BuildProfile profile = BuildProfile.GetActiveBuildProfile();

        if (profile != null) { return new List<string>(profile.scriptingDefines ?? new string[0]); }

        PlayerSettings.GetScriptingDefineSymbols(ActiveTarget(), out string[] defines);

        return new List<string>(defines);
    }

    private static NamedBuildTarget ActiveTarget() =>
        NamedBuildTarget.FromBuildTargetGroup(
            BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
}
