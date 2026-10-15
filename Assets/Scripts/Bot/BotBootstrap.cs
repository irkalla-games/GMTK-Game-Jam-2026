#if UNITY_EDITOR || BOT_RUNNER
using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Starts the balance bot when there is a job for it, before the first scene loads - the same hook
/// AudioManager bootstraps itself from. Does nothing at all on an ordinary Play or an ordinary launch.
///
/// In the Editor the job arrives as a file the Bot Runner staged just before entering Play Mode
/// (Temp/BotRunner/pending-job.json), because entering Play Mode reloads the domain and no static
/// survives it. The file is consumed on read, and one older than a couple of minutes is ignored - it is
/// left over from a launch that never happened, not this one.
///
/// In a player (the headless bot build) it comes from the command line:
///   -botJob job.json   -botOut folder   -botShard i   -botShards n
///   -botReport folder  (rebuild report.md and the CSVs from a batch folder, then quit)
/// </summary>
public static class BotBootstrap
{
    public const string PendingJobRelativePath = "Temp/BotRunner/pending-job.json";

    private static readonly TimeSpan PendingJobMaxAge = TimeSpan.FromMinutes(2);

    /// The project folder in the Editor; the folder holding the executable in a player.
    public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    public static string PendingJobPath => Path.Combine(ProjectRoot, PendingJobRelativePath);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        try
        {
            if (Application.isEditor) { LaunchPendingEditorJob(); }
            else { LaunchFromCommandLine(); }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);

            if (!Application.isEditor && Arg("-botJob") != null) { Application.Quit(3); }
        }
    }

    private static void LaunchPendingEditorJob()
    {
        string path = PendingJobPath;

        if (!File.Exists(path)) { return; }

        DateTime written = File.GetLastWriteTimeUtc(path);
        string json = File.ReadAllText(path);

        File.Delete(path);

        if (DateTime.UtcNow - written > PendingJobMaxAge)
        {
            Debug.LogWarning($"BotBootstrap: ignored a stale bot job from {written:u} - start it again from Tools/Bot/Bot Runner.");
            return;
        }

        BotPilot.Launch(JsonUtility.FromJson<BotJob>(json));
    }

    private static void LaunchFromCommandLine()
    {
        string reportDir = Arg("-botReport");

        if (reportDir != null)
        {
            string report = BotReport.Build(Path.GetFullPath(reportDir));
            Debug.Log($"BotBootstrap: wrote {report}");
            Application.Quit(0);
            return;
        }

        string jobPath = Arg("-botJob");

        if (jobPath == null) { return; }

        BotJob job = JsonUtility.FromJson<BotJob>(File.ReadAllText(Path.GetFullPath(jobPath)));

        string outDir = Arg("-botOut");

        if (outDir != null) { job.outputDir = Path.GetFullPath(outDir); }

        if (int.TryParse(Arg("-botShard"), out int shard)) { job.shardIndex = shard; }
        if (int.TryParse(Arg("-botShards"), out int shards)) { job.shardCount = Math.Max(1, shards); }

        job.exitWhenDone = true;

        BotPilot.Launch(job);
    }

    private static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) { return args[i + 1]; }
        }

        return null;
    }
}
#endif
