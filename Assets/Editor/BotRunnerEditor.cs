using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// The Editor half of the balance bot: static entry points the Bot Runner window calls - and that
/// Claude calls through `unity command eval_file` - plus a bridge that tidies up around each batch.
///
/// A batch starts by writing job.json into a fresh BotRuns/&lt;timestamp&gt;-&lt;label&gt;/ folder, staging the
/// job for BotBootstrap to pick up (Temp/BotRunner/pending-job.json), pointing Play Mode at the Main
/// Menu - where a real run starts - and entering Play Mode. From there the runtime BotPilot drives, and
/// this bridge leaves Play Mode again once BotStatus says the batch is done.
///
/// Returning to Edit Mode always puts things back: the Play Mode start scene the user had, the game's
/// pacing (BotPacing), and no stale job left waiting to fire on the next ordinary Play.
///
/// From the CLI:
///   var job = BotRunnerEditor.DefaultJob(); job.runsPerProfile = 2;
///   job.profiles = BotRunnerEditor.Profiles("Balanced", "Random");
///   return BotRunnerEditor.StartBatch(job);          // the batch folder
///   return BotRunnerEditor.Status();                 // JSON, while or after it runs
///   return BotRunnerEditor.StartReplay(@"...\BotRuns\...\runs\r000-Balanced");
/// </summary>
[InitializeOnLoad]
public static class BotRunnerEditor
{
    private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string PresetFolder = "Assets/Data/Bots";
    private const string PreviousStartSceneKey = "BotRunner.PreviousStartScene";
    private const string StartSceneSwappedKey = "BotRunner.StartSceneSwapped";

    private static bool exitRequested;

    static BotRunnerEditor()
    {
        BotAssets.EditorLookup = LookupAsset;

        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static string RepoRoot => Directory.GetParent(Application.dataPath).FullName;

    /// BotRuns/ at the repo root - gitignored.
    public static string OutputRoot => Path.Combine(RepoRoot, "BotRuns");

    // ---------------------------------------------------------------------------------------------
    // Jobs

    /// The window's defaults: the real campaign on Normal, the roster's playable-size party on starter
    /// decks, the Balanced profile, one run, Turbo.
    public static BotJob DefaultJob()
    {
        BotJob job = new() { label = "batch", campaign = "RealRun - Random" };

        CharacterRoster roster = FirstAsset<CharacterRoster>();

        if (roster != null)
        {
            for (int i = 0; i < roster.PlayablePartySize && i < roster.Characters.Count; i++)
            {
                CharacterOption option = roster.Characters[i];

                if (option == null) { continue; }

                DeckData deck = BotAssets.StarterDeckOf(option);
                job.party.Add(new BotPartySlot { hero = option.name, deck = deck != null ? deck.name : string.Empty });
            }
        }

        job.profiles = Profiles("Balanced");

        return job;
    }

    /// Snapshots of the named BotProfile assets, falling back to the matching BotPresets default when an
    /// asset is missing.
    public static List<BotProfileData> Profiles(params string[] names)
    {
        List<BotProfileData> profiles = new();

        foreach (string name in names)
        {
            BotProfile asset = AssetDatabase.LoadAssetAtPath<BotProfile>($"{PresetFolder}/{name}.asset");

            if (asset != null)
            {
                profiles.Add(asset.Snapshot());
                continue;
            }

            BotProfileData preset = BotPresets.All().Find(p => p.name == name);

            if (preset != null) { profiles.Add(preset); }
            else { Debug.LogWarning($"BotRunnerEditor: no profile named {name} - skipped."); }
        }

        return profiles;
    }

    /// <summary>
    /// Writes the batch folder and its job.json, then enters Play Mode to run it. Returns the batch
    /// folder, or an "error: ..." string if it could not start.
    /// </summary>
    public static string StartBatch(BotJob job)
    {
        string refusal = RefuseLaunch();

        if (refusal != null) { return Error(refusal); }

        job.mode = BotMode.Play;

        if (string.IsNullOrWhiteSpace(job.outputDir))
        {
            job.outputDir = Path.Combine(OutputRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{BotText.Slug(job.label)}");
        }

        job.outputDir = Path.GetFullPath(job.outputDir);
        job.batchId = Path.GetFileName(job.outputDir);
        job.createdUtc = DateTime.UtcNow.ToString("o");
        (job.commit, job.dirtyTree) = GitState();

        string problem = job.Validate();

        if (problem != null) { return Error(problem); }

        Directory.CreateDirectory(job.outputDir);
        File.WriteAllText(Path.Combine(job.outputDir, "job.json"), JsonUtility.ToJson(job, prettyPrint: true), BotText.Utf8);

        Launch(job);

        return job.outputDir;
    }

    /// Runs a job.json written earlier (Save job... in the window, or by hand) as a new batch.
    public static string StartBatchFromFile(string jobPath)
    {
        BotJob job = JsonUtility.FromJson<BotJob>(File.ReadAllText(jobPath));
        job.outputDir = null;

        return StartBatch(job);
    }

    /// <summary>
    /// Re-plays a recorded run (runs/&lt;run id&gt; inside a batch folder) in Play Mode at a watchable speed,
    /// stopping at the end - or wherever the game stops matching the recording, with replay/divergence.txt
    /// saying where and why. Stays in Play Mode afterwards so the board can be looked at.
    /// </summary>
    public static string StartReplay(string runDir, float speed = 1f, float pause = 0.6f)
    {
        string refusal = RefuseLaunch();

        if (refusal != null) { return Error(refusal); }

        runDir = Path.GetFullPath(runDir);
        string runJson = Path.Combine(runDir, "run.json");

        if (!File.Exists(runJson)) { return Error($"{runDir} has no run.json"); }

        BotRunRecord header = JsonUtility.FromJson<BotRunRecord>(File.ReadAllText(runJson));
        (string commit, bool dirty) = GitState();

        if (commit != header.commit || dirty != header.dirtyTree)
        {
            Debug.LogWarning($"BotRunner: this run was recorded at {header.commit}{(header.dirtyTree ? " (dirty)" : string.Empty)} and the "
                             + $"project is at {commit}{(dirty ? " (dirty)" : string.Empty)} - if any content changed since, the replay "
                             + "will stop where it first differs.");
        }

        BotJob job = new()
        {
            mode = BotMode.Replay,
            label = "replay",
            replayRunDir = runDir,
            outputDir = Directory.GetParent(Directory.GetParent(runDir).FullName).FullName,
            campaign = header.campaign,
            party = header.party,
            tier = header.tier,
            profiles = new List<BotProfileData> { new() { name = header.profile } },
            speed = BotSpeed.Watch,
            watchSpeed = Mathf.Clamp(speed, 1f, 4f),
            watchPause = Mathf.Max(0f, pause),
            exitWhenDone = false,
            commit = header.commit,
            dirtyTree = header.dirtyTree,
            batchId = header.batchId,
        };

        string problem = job.Validate();

        if (problem != null) { return Error(problem); }

        Launch(job);

        return runDir;
    }

    /// Asks the running batch to stop after it saves the run in progress.
    public static string Stop()
    {
        if (!EditorApplication.isPlaying) { return "not playing"; }

        BotStatus.StopRequested = true;

        return "stop requested";
    }

    public static string Status() => BotStatus.ToJson();

    /// Rebuilds report.md and the CSVs from whatever finished runs a batch folder holds.
    public static string RebuildReport(string batchDir) => BotReport.Build(Path.GetFullPath(batchDir));

    /// <summary>
    /// Creates any of the five preset profile assets (Assets/Data/Bots) that do not exist yet, from
    /// BotPresets. Never touches one that does - once created they are yours to tune, and silently
    /// resetting a tuned profile would be worse than a missing one.
    /// </summary>
    public static int CreateMissingPresets()
    {
        if (!AssetDatabase.IsValidFolder(PresetFolder)) { AssetDatabase.CreateFolder("Assets/Data", "Bots"); }

        int created = 0;

        foreach (BotProfileData preset in BotPresets.All())
        {
            string path = $"{PresetFolder}/{preset.name}.asset";

            if (AssetDatabase.LoadAssetAtPath<BotProfile>(path) != null) { continue; }

            BotProfile asset = ScriptableObject.CreateInstance<BotProfile>();
            asset.SetData(preset);
            AssetDatabase.CreateAsset(asset, path);
            created++;
        }

        if (created > 0) { AssetDatabase.SaveAssets(); }

        return created;
    }

    // ---------------------------------------------------------------------------------------------
    // Launching

    private static string RefuseLaunch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { return "leave Play Mode first"; }
        if (EditorApplication.isCompiling) { return "wait for scripts to finish compiling"; }
        if (EditorUtility.scriptCompilationFailed) { return "fix the compile errors first"; }

        return null;
    }

    private static void Launch(BotJob job)
    {
        string pending = BotBootstrap.PendingJobPath;

        Directory.CreateDirectory(Path.GetDirectoryName(pending));
        File.WriteAllText(pending, JsonUtility.ToJson(job), BotText.Utf8);

        // A real run starts at the Main Menu, whatever scene happens to be open - and starting there keeps
        // Game.unity's standalone EnsureRun from starting its debug campaign under the bot.
        if (!SessionState.GetBool(StartSceneSwappedKey, false))
        {
            SceneAsset previous = EditorSceneManager.playModeStartScene;

            SessionState.SetString(PreviousStartSceneKey, previous != null ? AssetDatabase.GetAssetPath(previous) : string.Empty);
            SessionState.SetBool(StartSceneSwappedKey, true);
        }

        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScenePath);

        exitRequested = false;
        EditorApplication.EnterPlaymode();
    }

    // ---------------------------------------------------------------------------------------------
    // Bridge

    private static void Poll()
    {
        if (!EditorApplication.isPlaying || exitRequested || !BotStatus.ExitWhenDone) { return; }

        if (BotStatus.State == BotStatus.Finished || BotStatus.State == BotStatus.Failed)
        {
            exitRequested = true;
            EditorApplication.ExitPlaymode();
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode) { return; }

        exitRequested = false;

        if (SessionState.GetBool(StartSceneSwappedKey, false))
        {
            string previous = SessionState.GetString(PreviousStartSceneKey, string.Empty);

            EditorSceneManager.playModeStartScene =
                string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);

            SessionState.EraseBool(StartSceneSwappedKey);
            SessionState.EraseString(PreviousStartSceneKey);
        }

        BotPacing.Restore();

        // A job staged for a launch that never read it must not fire on the next ordinary Play.
        if (File.Exists(BotBootstrap.PendingJobPath)) { File.Delete(BotBootstrap.PendingJobPath); }
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers

    private static UnityEngine.Object LookupAsset(Type type, string name)
    {
        foreach (string guid in AssetDatabase.FindAssets($"t:{type.Name}"))
        {
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), type);

            if (asset != null && asset.name == name) { return asset; }
        }

        return null;
    }

    public static T FirstAsset<T>() where T : UnityEngine.Object
    {
        foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));

            if (asset != null) { return asset; }
        }

        return null;
    }

    /// HEAD's short hash and whether the working tree has changes - recorded with every batch so a replay
    /// can warn when the project has moved on since.
    public static (string commit, bool dirty) GitState()
    {
        try
        {
            string commit = Git("rev-parse --short HEAD").Trim();
            string status = Git("status --porcelain");

            return (string.IsNullOrEmpty(commit) ? "unknown" : commit, !string.IsNullOrWhiteSpace(status));
        }
        catch (Exception)
        {
            return ("unknown", false);
        }
    }

    private static string Git(string arguments)
    {
        ProcessStartInfo info = new("git", arguments)
        {
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process process = Process.Start(info);

        string output = process != null ? process.StandardOutput.ReadToEnd() : string.Empty;

        if (process != null) { process.WaitForExit(10000); }

        return output;
    }

    private static string Error(string message)
    {
        Debug.LogError($"BotRunner: {message}");
        return "error: " + message;
    }
}
