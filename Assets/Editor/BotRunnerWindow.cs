using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools/Bot/Bot Runner: send the balance bot through the game and read what happened. Pick a party,
/// the profiles to compare and how many runs each, press Run - the Editor enters Play Mode, plays every
/// run fast-forwarded, writes a report and leaves Play Mode again. Results land in BotRuns/ at the repo
/// root: report.md and CSVs for the batch, and per run a turns.log to read and a recording to replay.
///
/// Purely fields and buttons - BotRunnerEditor does the launching, BotPilot the playing.
/// </summary>
public class BotRunnerWindow : EditorWindow
{
    [SerializeField] private string label = "batch";
    [SerializeField] private RunData campaign;
    [SerializeField] private int tier;
    [SerializeField] private List<CharacterOption> heroes = new();
    [SerializeField] private List<DeckData> decks = new();
    [SerializeField] private List<BotProfile> profiles = new();
    [SerializeField] private int runsPerProfile = 5;
    [SerializeField] private int baseSeed = 12345;
    [SerializeField] private BotSpeed speed = BotSpeed.Turbo;
    [SerializeField] private float turboStep = 0.25f;
    [SerializeField] private float watchSpeed = 1f;
    [SerializeField] private float watchPause = 0.5f;
    [SerializeField] private bool writeTurnLog = true;
    [SerializeField] private float replaySpeed = 1f;
    [SerializeField] private string lastBatchDir;

    private Vector2 scroll;

    [MenuItem("Tools/Bot/Bot Runner")]
    public static void Open()
    {
        BotRunnerWindow window = GetWindow<BotRunnerWindow>("Bot Runner");
        window.minSize = new Vector2(420f, 520f);
    }

    private void OnEnable() => FillDefaults();

    private void OnInspectorUpdate()
    {
        if (EditorApplication.isPlaying || BotStatus.State != BotStatus.Idle) { Repaint(); }
    }

    /// The same defaults BotRunnerEditor.DefaultJob uses, as assets the fields can show.
    private void FillDefaults()
    {
        if (campaign == null)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:RunData"))
            {
                RunData data = AssetDatabase.LoadAssetAtPath<RunData>(AssetDatabase.GUIDToAssetPath(guid));

                if (data != null && data.name == "RealRun - Random") { campaign = data; }
            }
        }

        if (heroes.Count == 0)
        {
            CharacterRoster roster = BotRunnerEditor.FirstAsset<CharacterRoster>();

            for (int i = 0; roster != null && i < roster.PlayablePartySize && i < roster.Characters.Count; i++)
            {
                heroes.Add(roster.Characters[i]);
                decks.Add(BotAssets.StarterDeckOf(roster.Characters[i]));
            }
        }

        if (profiles.Count == 0)
        {
            BotProfile balanced = AssetDatabase.LoadAssetAtPath<BotProfile>("Assets/Data/Bots/Balanced.asset");

            if (balanced != null) { profiles.Add(balanced); }
        }
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Batch", EditorStyles.boldLabel);
        label = EditorGUILayout.TextField(new GUIContent("Label", "Names the batch folder in BotRuns/."), label);
        campaign = (RunData)EditorGUILayout.ObjectField("Campaign", campaign, typeof(RunData), false);
        tier = EditorGUILayout.IntSlider(new GUIContent("Difficulty tier", "0 is Normal."), tier, 0, 6);

        EditorGUILayout.Space(6f);
        DrawParty();

        EditorGUILayout.Space(6f);
        DrawProfiles();

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Runs", EditorStyles.boldLabel);
        runsPerProfile = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Runs per profile",
            "Every profile plays the same seeds, so profiles can be compared run for run."), runsPerProfile));
        baseSeed = EditorGUILayout.IntField("Base seed", baseSeed);
        speed = (BotSpeed)EditorGUILayout.EnumPopup(new GUIContent("Speed",
            "Turbo: fast-forwarded, no turn banner, muted. Watch: normal speed with each play previewed."), speed);

        if (speed == BotSpeed.Turbo)
        {
            turboStep = EditorGUILayout.Slider(new GUIContent("Game seconds / frame",
                "Bigger is faster, and results are the same at any step - 0.25 is the fastest."), turboStep, 0.02f, 0.25f);
        }
        else
        {
            watchSpeed = EditorGUILayout.Slider("Watch speed", watchSpeed, 1f, 4f);
            watchPause = EditorGUILayout.Slider("Pause per decision (s)", watchPause, 0f, 3f);
        }

        writeTurnLog = EditorGUILayout.Toggle(new GUIContent("Write turns.log",
            "The readable per-turn log, about 1 MB a run. The events.jsonl replay recording is always written."), writeTurnLog);

        EditorGUILayout.Space(10f);
        DrawButtons();

        EditorGUILayout.Space(10f);
        DrawStatus();

        EditorGUILayout.EndScrollView();
    }

    private void DrawParty()
    {
        EditorGUILayout.LabelField("Party", EditorStyles.boldLabel);

        while (decks.Count < heroes.Count) { decks.Add(null); }

        int remove = -1;

        for (int i = 0; i < heroes.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();

            CharacterOption before = heroes[i];
            heroes[i] = (CharacterOption)EditorGUILayout.ObjectField(heroes[i], typeof(CharacterOption), false);

            if (heroes[i] != before) { decks[i] = BotAssets.StarterDeckOf(heroes[i]); }

            decks[i] = (DeckData)EditorGUILayout.ObjectField(decks[i], typeof(DeckData), false);

            if (GUILayout.Button("-", GUILayout.Width(22f))) { remove = i; }

            EditorGUILayout.EndHorizontal();
        }

        if (remove >= 0)
        {
            heroes.RemoveAt(remove);
            decks.RemoveAt(remove);
        }

        if (GUILayout.Button("Add hero", GUILayout.Width(90f)))
        {
            heroes.Add(null);
            decks.Add(null);
        }
    }

    private void DrawProfiles()
    {
        EditorGUILayout.LabelField("Profiles", EditorStyles.boldLabel);

        int remove = -1;

        for (int i = 0; i < profiles.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            profiles[i] = (BotProfile)EditorGUILayout.ObjectField(profiles[i], typeof(BotProfile), false);

            if (GUILayout.Button("-", GUILayout.Width(22f))) { remove = i; }

            EditorGUILayout.EndHorizontal();
        }

        if (remove >= 0) { profiles.RemoveAt(remove); }

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Add profile", GUILayout.Width(90f))) { profiles.Add(null); }

        if (GUILayout.Button(new GUIContent("Add all presets", "Balanced, Aggressive, Defensive, TotemFirst and Random."),
                             GUILayout.Width(110f)))
        {
            BotRunnerEditor.CreateMissingPresets();

            foreach (string guid in AssetDatabase.FindAssets("t:BotProfile", new[] { "Assets/Data/Bots" }))
            {
                BotProfile asset = AssetDatabase.LoadAssetAtPath<BotProfile>(AssetDatabase.GUIDToAssetPath(guid));

                if (asset != null && !profiles.Contains(asset)) { profiles.Add(asset); }
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawButtons()
    {
        bool playing = EditorApplication.isPlaying;

        EditorGUILayout.BeginHorizontal();

        using (new EditorGUI.DisabledScope(playing))
        {
            if (GUILayout.Button("Run", GUILayout.Height(28f))) { Run(); }
        }

        using (new EditorGUI.DisabledScope(!playing))
        {
            if (GUILayout.Button("Stop", GUILayout.Height(28f))) { BotRunnerEditor.Stop(); }
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();

        using (new EditorGUI.DisabledScope(playing))
        {
            if (GUILayout.Button(new GUIContent("Replay run...", "Pick a runs/<run> folder to watch it again.")))
            {
                string folder = EditorUtility.OpenFolderPanel("Pick a recorded run (BotRuns/<batch>/runs/<run>)", StartFolder(), string.Empty);

                if (!string.IsNullOrEmpty(folder)) { BotRunnerEditor.StartReplay(folder, replaySpeed, watchPause); }
            }
        }

        replaySpeed = EditorGUILayout.Slider(replaySpeed, 1f, 4f, GUILayout.Width(150f));

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button(new GUIContent("Rebuild report...", "Re-read a batch folder's runs and rewrite report.md and the CSVs.")))
        {
            string folder = EditorUtility.OpenFolderPanel("Pick a batch folder", StartFolder(), string.Empty);

            if (!string.IsNullOrEmpty(folder)) { EditorUtility.RevealInFinder(BotRunnerEditor.RebuildReport(folder)); }
        }

        if (GUILayout.Button("Save job..."))
        {
            string path = EditorUtility.SaveFilePanel("Save bot job", BotRunnerEditor.OutputRoot, $"{BotText.Slug(label)}.job.json", "json");

            if (!string.IsNullOrEmpty(path)) { File.WriteAllText(path, JsonUtility.ToJson(BuildJob(), prettyPrint: true), BotText.Utf8); }
        }

        if (GUILayout.Button("Open results"))
        {
            string target = !string.IsNullOrEmpty(lastBatchDir) && Directory.Exists(lastBatchDir) ? lastBatchDir : BotRunnerEditor.OutputRoot;

            Directory.CreateDirectory(target);
            EditorUtility.RevealInFinder(Path.Combine(target, "report.md"));
        }

        EditorGUILayout.EndHorizontal();
    }

    private string StartFolder() =>
        !string.IsNullOrEmpty(lastBatchDir) && Directory.Exists(lastBatchDir) ? lastBatchDir : BotRunnerEditor.OutputRoot;

    private void DrawStatus()
    {
        if (BotStatus.State == BotStatus.Idle && string.IsNullOrEmpty(lastBatchDir)) { return; }

        EditorGUILayout.HelpBox(BotStatus.State == BotStatus.Idle ? $"Last batch: {lastBatchDir}" : BotStatus.Summary, MessageType.Info);
    }

    private BotJob BuildJob()
    {
        BotJob job = new()
        {
            label = label,
            campaign = campaign != null ? campaign.name : "RealRun - Random",
            tier = tier,
            runsPerProfile = runsPerProfile,
            baseSeed = baseSeed,
            speed = speed,
            turboStep = turboStep,
            watchSpeed = watchSpeed,
            watchPause = watchPause,
            writeTurnLog = writeTurnLog,
        };

        for (int i = 0; i < heroes.Count; i++)
        {
            if (heroes[i] == null) { continue; }

            DeckData deck = i < decks.Count ? decks[i] : null;
            job.party.Add(new BotPartySlot { hero = heroes[i].name, deck = deck != null ? deck.name : string.Empty });
        }

        foreach (BotProfile profile in profiles)
        {
            if (profile != null) { job.profiles.Add(profile.Snapshot()); }
        }

        return job;
    }

    private void Run()
    {
        BotJob job = BuildJob();

        if (job.profiles.Count == 0)
        {
            EditorUtility.DisplayDialog("Bot Runner", "Add at least one profile - \"Add all presets\" creates the five defaults.", "OK");
            return;
        }

        string result = BotRunnerEditor.StartBatch(job);

        if (result.StartsWith("error:"))
        {
            EditorUtility.DisplayDialog("Bot Runner", result, "OK");
            return;
        }

        lastBatchDir = result;
    }
}
