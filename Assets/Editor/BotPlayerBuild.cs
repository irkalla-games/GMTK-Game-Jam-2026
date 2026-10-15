using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the headless balance-bot player - a Windows build of MainMenu + Game with BOT_RUNNER defined,
/// so BotBootstrap reads -botJob from the command line. Run only by Tools/BotRunner/Build-BotPlayer.ps1,
/// inside a mirror copy of the project it keeps under %LOCALAPPDATA%: building a non-active target from
/// the open Editor would switch its platform (a full reimport) and write into the committed web build
/// profile, and neither is acceptable for a test tool.
///
/// Refuses to run anywhere but that mirror - it renames the product (so the bot player keeps its
/// PlayerPrefs under its own registry key, apart from the real game's), and that must never land in the
/// real project's ProjectSettings.
/// </summary>
public static class BotPlayerBuild
{
    public const string MirrorMarker = "BotBuildMirror.txt";
    public const string ExecutableName = "EscapeTheSpireBot.exe";

    public static void BuildFromCommandLine()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;

        if (!Application.isBatchMode || !File.Exists(Path.Combine(root, MirrorMarker)))
        {
            Debug.LogError("BotPlayerBuild: run this through Tools/BotRunner/Build-BotPlayer.ps1 - it only builds inside "
                           + "the mirror project that script maintains, never the real one.");

            if (Application.isBatchMode) { EditorApplication.Exit(2); }

            return;
        }

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            Debug.LogError($"BotPlayerBuild: the active target is {EditorUserBuildSettings.activeBuildTarget} - launch Unity with "
                           + "-buildTarget Win64 so the platform is active before scripts load.");
            EditorApplication.Exit(2);
            return;
        }

        string outDir = Argument("-botBuildOut") ?? Path.Combine(root, "Builds", "BotPlayer");

        Directory.CreateDirectory(outDir);

        PlayerSettings.productName = "Escape The Spire BotRunner";
        PlayerSettings.runInBackground = true;

        List<string> scenes = new();

        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled) { scenes.Add(scene.path); }
        }

        if (scenes.Count == 0) { scenes.AddRange(new[] { "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Game.unity" }); }

        BuildPlayerOptions options = new()
        {
            scenes = scenes.ToArray(),
            locationPathName = Path.Combine(outDir, ExecutableName),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
            extraScriptingDefines = new[] { "BOT_RUNNER" },
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);

        Debug.Log($"BotPlayerBuild: {report.summary.result}, {report.summary.totalErrors} errors, output {options.locationPathName}");

        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    private static string Argument(string name)
    {
        string[] args = System.Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) { return args[i + 1]; }
        }

        return null;
    }
}
