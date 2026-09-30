#if UNITY_EDITOR || BOT_RUNNER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Drives the real game through whole runs for balance testing - the balance bot. One coroutine, one
/// tick per frame, surviving every scene load (a PersistantSingleton, like RunManager): at the Main
/// Menu it starts the next run exactly as CharacterSelectPanel does (RunManager.StartRun, then load
/// Game); in a battle it answers whatever the game is waiting on - a notification, a reward, a
/// discard, the removal grid - and otherwise plays a card through CardPlayManager.TryPlay or ends the
/// turn through BattleManager.RequestEndTurn. Every rule stays the game's own: the bot only ever calls
/// the doors a click reaches.
///
/// Launched by BotBootstrap, from a job the Bot Runner window stages (Editor) or from -botJob (player).
///
/// Determinism. Every rule rolls through GameDice, whose state nothing but rules advance - the engine
/// rolls UnityEngine.Random itself on every rendered frame, which is why rules no longer share it. The
/// pilot seeds GameDice at fixed logical points: once as each Game scene loads - after Awake, before
/// BattleManager.Start rolls the encounter, so the encounter, floor and opening shuffles depend only on
/// (run seed, level) - and again at every decision, from (run seed, level, round, step), right before
/// the decision is committed and after all planning. A replay reseeds at exactly the same points. The
/// one roll whose count still depends on frame grouping (an enemy with Random targeting re-aiming in
/// BattleManager.LateUpdate) can therefore only shift the dice within one decision, and the next reseed
/// clears it. The planner and the reward policy roll nothing of the game's - their own dice are a
/// System.Random derived from the same decision seed.
///
/// Nothing here writes PlayerPrefs: the campaign is rebuilt with progression off
/// (RunData.CreateRuntimeFrom), the tutorial is never started, and volume is muted through
/// AudioListener rather than GameSettings.
/// </summary>
public class BotPilot : PersistantSingleton<BotPilot>
{
    public const string GameScene = "Game";
    public const string MenuScene = "MainMenu";

    private BotJob job;
    private readonly List<BotRunKey> queue = new();
    private int queueCursor;

    private RunData runtimeCampaign;
    private string partyLabel;
    private bool assetsResolved;
    private bool finished;
    private bool stopHandled;

    // The run in progress.
    private bool runActive;
    private BotRunKey runKey;
    private BotProfileData profile;
    private BotRecorder recorder;
    private int runSeed;
    private string pendingOutcome;
    private string pendingNote;
    private float runStartRealtime;
    private int runStartFrame;
    private int runErrors;
    private int tickExceptions;
    private bool abortRequested;
    private string abortNote;

    // Where in the run the bot is. `step` numbers decisions within a round - the unit a replay matches on.
    private int levelIndex;
    private int round = -1;
    private int step;
    private int playsThisTurn;
    private int refusalsInARow;
    private readonly HashSet<string> banned = new();
    private BotSkipKind lastSkip;
    private string lastOfferKey;
    private int sameOfferShows;
    private bool notificationSeen;
    private string notificationOutcome;

    // Pacing: a battle decision needs two consecutive ready ticks, so BattleManager.LateUpdate has
    // re-planned every enemy intent since the board last changed. Watch previews a play before it lands.
    private int readyStreak;
    private float nextDecisionAt;
    private BotCandidate pendingPlay;

    // Watchdog.
    private int frame;
    private string lastSignature;
    private int lastProgressFrame;
    private float lastProgressRealtime;
    private int stallAttempts;

    // Replay.
    private BotReplay replay;
    private bool halted;
    private bool inLogHook;

    private bool Watch => job.speed == BotSpeed.Watch;

    private int LevelNumber => levelIndex + 1;

    /// Creates the pilot and starts the batch. Called by BotBootstrap before the first scene loads.
    public static BotPilot Launch(BotJob job)
    {
        if (Instance != null)
        {
            Debug.LogWarning("BotPilot: a batch is already running - ignoring the new job.");
            return Instance;
        }

        BotPilot pilot = new GameObject(nameof(BotPilot)).AddComponent<BotPilot>();
        pilot.Begin(job);

        return pilot;
    }

    private void Begin(BotJob newJob)
    {
        job = newJob;

        if (job.mode == BotMode.Replay && !PrepareReplay(out string replayProblem))
        {
            Fail(replayProblem);
            return;
        }

        string problem = job.Validate();

        if (problem != null)
        {
            Fail(problem);
            return;
        }

        Directory.CreateDirectory(job.outputDir);

        if (job.mode == BotMode.Play)
        {
            foreach (BotRunKey key in job.Runs())
            {
                // Resume: a run with a finished run.json is kept, a half-written one is played again.
                if (!RunFinished(Path.Combine(job.outputDir, "runs", key.RunId))) { queue.Add(key); }
            }
        }

        string statusFile = job.mode == BotMode.Replay
            ? Path.Combine(job.replayRunDir, "replay", "status.json")
            : Path.Combine(job.outputDir, job.shardCount > 1 ? $"status-shard-{job.shardIndex}.json" : "status.json");

        BotStatus.Reset(job.outputDir, statusFile, queue.Count, job.exitWhenDone);
        BotStatus.Message = job.mode == BotMode.Replay ? $"replaying {job.replayRunDir}" : null;
        BotStatus.Write(force: true);

        BotPacing.Apply(job);

        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.logMessageReceived += OnLog;

        lastProgressRealtime = Time.realtimeSinceStartup;

        StartCoroutine(Drive());
    }

    private bool PrepareReplay(out string problem)
    {
        replay = BotReplay.Load(job.replayRunDir, out problem);

        if (replay == null || problem != null) { return false; }

        BotRunRecord header = replay.Header;

        job.campaign = header.campaign;
        job.party = new List<BotPartySlot>(header.party);
        job.tier = header.tier;
        job.profiles = new List<BotProfileData> { new() { name = header.profile } };
        job.exitWhenDone = false;

        queue.Add(new BotRunKey { globalIndex = 0, runIndex = header.runIndex, profileIndex = 0, profileName = header.profile });

        return true;
    }

    private static bool RunFinished(string runDir)
    {
        string path = Path.Combine(runDir, "run.json");

        if (!File.Exists(path)) { return false; }

        try
        {
            BotRunRecord record = JsonUtility.FromJson<BotRunRecord>(File.ReadAllText(path));
            return record != null && record.outcome != "Running";
        }
        catch (Exception) { return false; }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        SceneManager.sceneLoaded -= OnSceneLoaded;
        Application.logMessageReceived -= OnLog;

        // Play Mode ended or the player is quitting mid-run: keep what was played.
        if (runActive && recorder != null)
        {
            try
            {
                recorder.FinishRun(pendingOutcome ?? "Cancelled", pendingNote ?? "stopped mid-run",
                                   Time.realtimeSinceStartup - runStartRealtime, frame - runStartFrame);
            }
            catch (Exception ex) { Debug.LogException(ex); }

            runActive = false;
        }

        if (BotStatus.State == BotStatus.Running) { BotStatus.State = BotStatus.Finished; }

        BotStatus.Write(force: true);
        BotPacing.Restore();
    }

    // ---------------------------------------------------------------------------------------------
    // The loop

    private IEnumerator Drive()
    {
        while (!finished)
        {
            frame++;

            try { Tick(); }
            catch (Exception ex) { OnTickException(ex); }

            yield return null;
        }
    }

    private void Tick()
    {
        BotPacing.PerFrame();
        BotStatus.Write();

        if (runActive && recorder != null) { recorder.TraceRng("frame"); }

        if (BotStatus.StopRequested && !stopHandled)
        {
            stopHandled = true;
            StopBatch();
            return;
        }

        if (halted) { return; }

        Scene scene = SceneManager.GetActiveScene();

        if (scene.name == MenuScene)
        {
            MenuTick();
            return;
        }

        if (scene.name != GameScene) { return; }

        // A Game scene nobody asked for - BattleManager's EnsureRun started its debug campaign, say.
        if (!runActive)
        {
            LoadMenu();
            return;
        }

        if (abortRequested)
        {
            Abort("Error", abortNote);
            return;
        }

        if (!Watch && Time.realtimeSinceStartup - runStartRealtime > job.maxRunSeconds)
        {
            Abort("Stalled", $"the run passed {job.maxRunSeconds} real seconds");
            return;
        }

        if (EndlessLoop(out string loop))
        {
            Abort("Stalled", loop);
            return;
        }

        if (Watchdog()) { return; }

        NotificationManager notes = NotificationManager.Instance;

        if (notes != null && notes.IsShowing)
        {
            NotificationTick(notes);
            return;
        }

        notificationSeen = false;

        CardChoicePanel discard = CardChoicePanel.Instance;

        if (discard != null && discard.IsShowing)
        {
            if (Paced()) { DiscardTick(discard); }
            return;
        }

        CardRemovalPanel removal = CardRemovalPanel.Instance;

        if (removal != null && removal.IsShowing)
        {
            if (Paced()) { RemovalTick(removal); }
            return;
        }

        LootManager loot = LootManager.Instance;

        if (loot != null && loot.Panel != null && loot.Panel.IsShowing)
        {
            if (Paced()) { RewardTick(loot.Panel); }
            return;
        }

        if (pendingPlay != null)
        {
            if (Time.realtimeSinceStartup >= nextDecisionAt) { CommitPendingPlay(); }
            return;
        }

        if (!ReadyToAct())
        {
            readyStreak = 0;
            return;
        }

        if (++readyStreak < 2 || !Paced()) { return; }

        BattleTick();
    }

    private bool Paced() => !Watch || Time.realtimeSinceStartup >= nextDecisionAt;

    private static bool ReadyToAct()
    {
        BattleManager battle = BattleManager.Instance;

        if (battle == null || battle.Phase != BattlePhase.PlayerActing || battle.EndTurnRequested || battle.InputLocked)
        {
            return false;
        }

        if (ActionManager.Instance != null && !ActionManager.Instance.IsIdle) { return false; }
        if (LootManager.Instance != null && !LootManager.Instance.IsIdle) { return false; }
        if (ActiveHandViewer.Instance != null && ActiveHandViewer.Instance.Busy) { return false; }

        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // Runs

    private void MenuTick()
    {
        if (runActive) { FinishRun(); }

        if (finished) { return; }

        if (!assetsResolved && !ResolveAssets(out string problem))
        {
            Fail(problem);
            return;
        }

        if (queueCursor >= queue.Count)
        {
            FinishBatch();
            return;
        }

        StartRun(queue[queueCursor++]);
    }

    /// Finds the campaign, heroes and decks by name - see BotAssets - and builds the campaign the runs
    /// play: the authored one, progression switched off.
    private bool ResolveAssets(out string problem)
    {
        problem = null;

        RunData campaign = BotAssets.Find<RunData>(job.campaign);

        if (campaign == null)
        {
            problem = $"no RunData named '{job.campaign}'";
            return false;
        }

        List<PartyEntry> entries = new();
        List<string> names = new();

        foreach (BotPartySlot slot in job.party)
        {
            CharacterOption option = BotAssets.Find<CharacterOption>(slot.hero);

            if (option == null || option.Prefab == null)
            {
                problem = $"no hero option named '{slot.hero}'";
                return false;
            }

            DeckData deck = string.IsNullOrWhiteSpace(slot.deck)
                ? BotAssets.StarterDeckOf(option)
                : BotAssets.Find<DeckData>(slot.deck);

            if (deck == null && !string.IsNullOrWhiteSpace(slot.deck))
            {
                problem = $"no deck named '{slot.deck}'";
                return false;
            }

            entries.Add(new PartyEntry { prefab = option.Prefab, deck = deck });
            names.Add(option.DisplayName);
        }

        runtimeCampaign = RunData.CreateRuntimeFrom(campaign, entries, allowProgression: false);
        partyLabel = string.Join("+", names);
        assetsResolved = true;

        return true;
    }

    private void StartRun(BotRunKey key)
    {
        runKey = key;
        profile = job.profiles[Mathf.Clamp(key.profileIndex, 0, job.profiles.Count - 1)];
        runSeed = replay != null ? replay.Header.seed : BotSeeds.RunSeed(job.baseSeed, key.runIndex);

        string runDir = replay != null
            ? Path.Combine(job.replayRunDir, "replay")
            : Path.Combine(job.outputDir, "runs", key.RunId);

        // A folder without a finished run.json is a run a crashed batch left half-written.
        if (replay == null && Directory.Exists(runDir)) { Directory.Delete(runDir, recursive: true); }

        recorder = new BotRecorder(job, key, profile, runSeed, runDir, partyLabel);

        runActive = true;
        pendingOutcome = null;
        pendingNote = null;
        runStartRealtime = Time.realtimeSinceStartup;
        runStartFrame = frame;
        runErrors = 0;
        tickExceptions = 0;
        abortRequested = false;

        BotStatus.CurrentRun = key.RunId;
        BotStatus.Level = 0;
        BotStatus.Round = 0;

        GameDice.InitState(runSeed);
        TimeFreeze.ReleaseAll();

        RunManager.StartRun(runtimeCampaign, showTutorial: false, followOn: null, difficultyTier: job.tier);
        SceneManager.LoadScene(GameScene);

        MarkProgress();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        MarkProgress();

        if (scene.name != GameScene || !runActive || finished) { return; }

        RunManager run = RunManager.Instance;
        levelIndex = run != null ? Mathf.Max(0, run.LevelNumber - 1) : 0;

        // Before BattleManager.Start rolls the encounter and the floor and SpawnParty shuffles the decks -
        // sceneLoaded runs after every Awake in the scene and before any Start.
        GameDice.InitState(BotSeeds.LevelSeed(runSeed, levelIndex));

        round = -1;
        step = 0;
        readyStreak = 0;
        playsThisTurn = 0;
        refusalsInARow = 0;
        banned.Clear();
        notificationSeen = false;
        lastOfferKey = null;
        sameOfferShows = 0;
        pendingPlay = null;

        recorder.BeginLevel(levelIndex, run != null ? run.CurrentLevel : null, BattleManager.Instance, ActionManager.Instance);
        recorder.TraceRng($"level seed L{LevelNumber}");

        if (BotPacing.IsTurbo) { DisableTurnBanner(); }

        BotStatus.Level = LevelNumber;
    }

    /// TurnTransitionViewer.Play returns at once from a disabled viewer (onRoll still fires), which skips
    /// its ~1.5 real seconds a round without BattleManager knowing anything changed.
    private static void DisableTurnBanner()
    {
        TurnTransitionViewer viewer = FindAnyObjectByType<TurnTransitionViewer>(FindObjectsInactive.Include);

        if (viewer != null) { viewer.enabled = false; }
    }

    private void FinishRun()
    {
        string outcome = pendingOutcome ?? "Stalled";
        string note = pendingNote ?? (pendingOutcome == null ? "the run ended without a result" : null);

        recorder.FinishRun(outcome, note, Time.realtimeSinceStartup - runStartRealtime, frame - runStartFrame);

        BotStatus.RunsDone++;

        switch (outcome)
        {
            case "Won": BotStatus.Won++; break;
            case "Died": BotStatus.Died++; break;
            default: BotStatus.Stalled++; break;
        }

        BotStatus.CurrentRun = null;
        BotStatus.Write(force: true);

        runActive = false;
        recorder = null;
    }

    private void FinishBatch()
    {
        if (finished) { return; }

        finished = true;

        if (job.mode == BotMode.Play && job.shardCount <= 1)
        {
            try { BotStatus.Message = "report: " + BotReport.Build(job.outputDir); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        if (BotStatus.State == BotStatus.Running) { BotStatus.State = BotStatus.Finished; }

        BotStatus.Write(force: true);
        BotPacing.Restore();

        if (runtimeCampaign != null) { Destroy(runtimeCampaign); }

        Debug.LogWarning($"BotPilot: {BotStatus.Summary}");

        if (!Application.isEditor && job.exitWhenDone)
        {
            Application.Quit(BotStatus.Stalled > 0 || BotStatus.Errors > 0 ? 2 : 0);
        }
    }

    /// The window's Stop button: keep the run in progress as Cancelled and end the batch where it stands.
    private void StopBatch()
    {
        if (runActive)
        {
            pendingOutcome = "Cancelled";
            pendingNote = "stopped from the Bot Runner";
            FinishRun();
        }

        FinishBatch();
    }

    private void Fail(string problem)
    {
        Debug.LogError($"BotPilot: cannot run this job - {problem}");

        BotStatus.State = BotStatus.Failed;
        BotStatus.Message = problem;
        BotStatus.Write(force: true);

        finished = true;
        BotPacing.Restore();

        if (!Application.isEditor && job != null && job.exitWhenDone) { Application.Quit(3); }
    }

    /// Ends the run as `outcome` and goes back to the menu, which finishes it and starts the next.
    private void Abort(string outcome, string note)
    {
        if (replay != null)
        {
            Diverge($"the replay could not continue: {note}", null);
            return;
        }

        pendingOutcome = outcome;
        pendingNote = note;
        abortRequested = false;

        if (recorder != null) { recorder.Note($"!! run aborted ({outcome}): {note}"); }

        if (RunManager.Instance != null) { RunManager.Instance.EndRun(); }

        LoadMenu();
    }

    private void LoadMenu()
    {
        TimeFreeze.ReleaseAll();
        SceneManager.LoadScene(MenuScene);
        MarkProgress();
    }

    // ---------------------------------------------------------------------------------------------
    // Decisions

    /// Brings `round` up to the battle's, restarting `step` at each new round. True when it moved.
    private bool SyncRound(BattleManager battle)
    {
        int current = battle != null ? battle.TurnsElapsed : round;

        if (current == round) { return false; }

        round = current;
        step = 0;
        BotStatus.Round = round;

        return true;
    }

    private BotEvent DecisionEvent(string action, string context, out string fpText)
    {
        fpText = BotFingerprint.Text(BattleManager.Instance, LevelNumber, round, step, context);

        return new BotEvent
        {
            kind = "decision",
            action = action,
            level = LevelNumber,
            round = round,
            step = step,
            fp = BotFingerprint.Hash(fpText),
            fpText = fpText,
        };
    }

    /// The bot's own dice for this decision - the Random profile's picks. Never UnityEngine.Random.
    private System.Random DecisionRng() =>
        new(BotSeeds.DecisionSeed(runSeed, levelIndex, round, step) ^ 0x2545F491);

    /// The game's dice for whatever this decision sets in motion. Called after planning, right before the
    /// decision is committed.
    private void Reseed()
    {
        if (recorder != null) { recorder.TraceRng($"before reseed L{LevelNumber} R{round} S{step}"); }

        GameDice.InitState(BotSeeds.DecisionSeed(runSeed, levelIndex, round, step));

        if (recorder != null) { recorder.TraceRng($"reseed L{LevelNumber} R{round} S{step}"); }

        step++;
        MarkProgress();
    }

    private void AfterDecision()
    {
        readyStreak = 0;

        if (Watch) { nextDecisionAt = Time.realtimeSinceStartup + job.watchPause; }
    }

    /// Matches this decision point against the recording when replaying. False (and the replay halted)
    /// on any mismatch.
    private bool ReplayMatches(BotEvent live, string context, out BotEvent recorded)
    {
        recorded = null;

        if (replay == null) { return true; }

        recorded = replay.Next(context, live.level, live.round, live.step, live.fp, live.fpText, out string problem, out string diff);

        if (recorded != null) { return true; }

        Diverge(problem, diff);
        return false;
    }

    private void BattleTick()
    {
        BattleManager battle = BattleManager.Instance;
        bool newRound = SyncRound(battle);
        BotBoardModel model = BotBoardModel.Build(battle);

        if (newRound)
        {
            playsThisTurn = 0;
            refusalsInARow = 0;
            banned.Clear();
            recorder.BeginRound(round, model);
        }

        // Whatever the previous play set in motion has landed by now.
        recorder.EndPlay();

        BotEvent e = DecisionEvent("play", "battle", out _);
        BotDecision decision;

        if (replay != null)
        {
            if (!ReplayMatches(e, "battle", out BotEvent recorded)) { return; }

            decision = FromRecording(recorded, model, out string problem);

            if (decision == null)
            {
                Diverge(problem, null);
                return;
            }
        }
        else if (recorder.Record.decisions >= job.maxDecisionsPerRun)
        {
            Abort("Stalled", $"{job.maxDecisionsPerRun} decisions without the run ending");
            return;
        }
        else if (playsThisTurn >= job.maxPlaysPerTurn)
        {
            decision = new BotDecision { Reason = "play cap reached" };
            recorder.PlayCapHit();
        }
        else if (refusalsInARow >= 5)
        {
            decision = new BotDecision { Reason = "five refused plays in a row" };
        }
        else
        {
            decision = BotPlanner.Decide(model, profile, DecisionRng(), banned);
        }

        if (decision.Play != null)
        {
            BotCandidate p = decision.Play;
            e.hero = p.Hero.DisplayName;
            e.hand = p.HandIndex;
            e.card = p.Card.cardName;
            e.x = p.Tile.Coordinates.x;
            e.y = p.Tile.Coordinates.y;
            e.aim = p.AimTurns;
            e.score = p.Score;
        }
        else
        {
            e.action = "endTurn";
        }

        e.note = decision.Reason;

        int unspent = 0;

        foreach (Character hero in model.Heroes) { unspent += Mathf.Max(0, hero.Energy); }

        recorder.Decision(decision, e, profile.weights, unspent);

        if (decision.IsEndTurn)
        {
            recorder.EndTurn(model);
            Reseed();
            battle.RequestEndTurn();

            if (!battle.EndTurnRequested) { recorder.Note("! End Turn was refused"); }

            banned.Clear();
            playsThisTurn = 0;
            AfterDecision();
            return;
        }

        if (Watch)
        {
            Preview(decision.Play);
            pendingPlay = decision.Play;
            nextDecisionAt = Time.realtimeSinceStartup + job.watchPause;
            return;
        }

        Commit(decision.Play);
    }

    private void CommitPendingPlay()
    {
        BotCandidate play = pendingPlay;
        pendingPlay = null;

        ClearPreview();
        Commit(play);
    }

    private void Commit(BotCandidate play)
    {
        Character hero = play.Hero;
        Card card = play.Card;
        GridTile tile = play.Tile;

        Reseed();

        card.ResetAim();

        for (int turns = 0; turns < play.AimTurns; turns++) { card.RotateAim(hero, tile); }

        recorder.BeginPlay(hero, card);

        string refusal = CardPlayManager.TryPlay(hero, card, tile);

        if (refusal != null)
        {
            card.ResetAim();
            recorder.EndPlay();
            recorder.PlayRefused(play.Describe(), refusal);
            refusalsInARow++;
            banned.Add(play.Key);
        }
        else
        {
            recorder.PlayCommitted(hero, card, play);
            playsThisTurn++;
            refusalsInARow = 0;
            banned.Clear();
        }

        AfterDecision();
    }

    /// A recorded play or end turn, resolved against the live board. Null with a reason when the hero,
    /// card or tile it names is not there.
    private static BotDecision FromRecording(BotEvent recorded, BotBoardModel model, out string problem)
    {
        problem = null;

        if (recorded.action == "endTurn") { return new BotDecision { Reason = "replay" }; }

        if (recorded.action != "play")
        {
            problem = $"the recording has a '{recorded.action}' here, but the game is waiting for a play";
            return null;
        }

        Character hero = model.Heroes.Find(h => h.DisplayName == recorded.hero);

        if (hero == null)
        {
            problem = $"no living hero named {recorded.hero}";
            return null;
        }

        int handIndex = recorded.hand >= 0 && recorded.hand < hero.Hand.Count && hero.Hand[recorded.hand] != null
                        && hero.Hand[recorded.hand].cardName == recorded.card
            ? recorded.hand
            : -1;

        for (int i = 0; handIndex < 0 && i < hero.Hand.Count; i++)
        {
            if (hero.Hand[i] != null && hero.Hand[i].cardName == recorded.card) { handIndex = i; }
        }

        Card card = handIndex >= 0 ? hero.Hand[handIndex] : null;

        GridTile tile = GridManager.Instance != null ? GridManager.Instance.GetTile(new Vector2Int(recorded.x, recorded.y)) : null;

        if (card == null || tile == null)
        {
            problem = $"{recorded.hero} has no {recorded.card} in hand, or ({recorded.x},{recorded.y}) is not on the board";
            return null;
        }

        return new BotDecision
        {
            Reason = "replay",
            Play = new BotCandidate
            {
                Hero = hero,
                HeroOrder = model.HeroOrder(hero),
                HandIndex = handIndex,
                Card = card,
                Tile = tile,
                AimTurns = recorded.aim,
                Score = recorded.score,
                Features = new BotFeatures(),
                Info = BotCardInfo.Of(card),
            },
        };
    }

    private void NotificationTick(NotificationManager notes)
    {
        if (!notificationSeen)
        {
            notificationSeen = true;
            notificationOutcome = ClassifyOutcome();

            if (notificationOutcome != null)
            {
                recorder.LevelOutcome(notificationOutcome);

                if (notificationOutcome == "Died" || notificationOutcome == "Won") { pendingOutcome = notificationOutcome; }
            }

            MarkProgress();

            // Linger on the banner in Watch, so a person watching sees it.
            if (Watch) { nextDecisionAt = Time.realtimeSinceStartup + Mathf.Max(1f, job.watchPause * 2f); }
        }

        if (!Paced()) { return; }

        // A replay ends on the final Victory or Defeat, with the board still up to look at.
        if (replay != null && (notificationOutcome == "Died" || notificationOutcome == "Won"))
        {
            CompleteReplay();
            return;
        }

        SyncRound(BattleManager.Instance);

        BotEvent e = DecisionEvent("ack", "ack", out _);
        e.choice = notes.Title;

        if (!ReplayMatches(e, "ack", out _)) { return; }

        recorder.Choice(e, $"Acknowledge \"{notes.Title}\"");
        Reseed();
        notes.Hide();
        AfterDecision();
    }

    /// What the notification on screen means, read from the game rather than its text: no hero left is
    /// a defeat; the last turn survived is a clear, or the win on the final level. Null for anything else.
    private static string ClassifyOutcome()
    {
        BattleManager battle = BattleManager.Instance;

        if (battle == null) { return null; }

        bool heroStanding = false;

        foreach (Character character in battle.Characters)
        {
            if (character != null && character.IsPlayerControlled && !character.IsDead) { heroStanding = true; }
        }

        if (!heroStanding) { return "Died"; }

        if (battle.TurnsRemaining <= 0)
        {
            return RunManager.Instance != null && RunManager.Instance.IsFinalLevel ? "Won" : "Cleared";
        }

        return null;
    }

    private void RewardTick(RewardPanel panel)
    {
        BattleManager battle = BattleManager.Instance;
        SyncRound(battle);

        string offer = OfferKey(panel);

        if (offer == lastOfferKey) { sameOfferShows++; }
        else
        {
            lastOfferKey = offer;
            sameOfferShows = 1;
        }

        // The offer is part of what must match: a replay that drew different cards has diverged even when
        // the board itself looks the same.
        BotEvent e = DecisionEvent("card", "reward:" + offer, out _);
        BotRewardChoice choice;

        if (replay != null)
        {
            if (!ReplayMatches(e, "reward", out BotEvent recorded)) { return; }

            choice = new BotRewardChoice { Kind = recorded.action, Index = recorded.index, Name = recorded.choice, Reason = "replay" };
            choice.Skip = recorded.action == "skip" && recorded.index >= 0 && recorded.index < panel.OfferedSkips.Count
                ? BotRewardPolicy.KindOf(panel.OfferedSkips[recorded.index])
                : BotSkipKind.None;
        }
        else if (sameOfferShows > 3)
        {
            // The same offer keeps coming back - a skip that backs out of itself. Take a card, or nothing.
            choice = panel.OfferedCards.Count > 0
                ? new BotRewardChoice { Kind = "card", Index = 0, Name = panel.OfferedCards[0].cardName, Reason = "offer kept re-showing" }
                : new BotRewardChoice { Kind = "decline", Reason = "offer kept re-showing" };
        }
        else
        {
            PartyMember record = BotAssets.RecordFor(panel.Receiver);
            choice = BotRewardPolicy.ChooseOffer(panel, record != null ? record.deck : null, profile, DecisionRng());
        }

        e.action = choice.Kind;
        e.index = choice.Index;
        e.choice = choice.Name;
        e.note = choice.Reason;

        string hero = panel.Receiver != null ? panel.Receiver.DisplayName : "?";
        string occasion = battle != null && battle.TurnsRemaining <= 0 ? "clear" : "pickup";

        recorder.Choice(e, $"Reward ({occasion}) for {hero}: {choice.Kind} {choice.Name} - {choice.Reason}   [offered {offer}]");

        CardData picked = choice.Kind == "card" && choice.Index >= 0 && choice.Index < panel.OfferedCards.Count
            ? panel.OfferedCards[choice.Index]
            : null;

        recorder.Reward(new BotRewardRecord
        {
            hero = hero,
            occasion = occasion,
            kind = choice.Kind,
            choice = choice.Name,
            offered = offer,
        }, panel.OfferedCards, picked);

        lastSkip = choice.Kind == "skip" ? choice.Skip : BotSkipKind.None;

        Reseed();

        switch (choice.Kind)
        {
            case "card" when choice.Index >= 0 && choice.Index < panel.OfferedCards.Count:
                panel.Choose(panel.OfferedCards[choice.Index]);
                break;
            case "equipment" when choice.Index >= 0 && choice.Index < panel.OfferedEquipment.Count:
                panel.ChooseEquipment(panel.OfferedEquipment[choice.Index]);
                break;
            case "skip" when choice.Index >= 0 && choice.Index < panel.OfferedSkips.Count:
                panel.ChooseSkip(panel.OfferedSkips[choice.Index]);
                break;
            default:
                panel.ChooseSkip(null);
                break;
        }

        AfterDecision();
    }

    private static string OfferKey(RewardPanel panel)
    {
        List<string> names = new();

        foreach (CardData card in panel.OfferedCards) { names.Add(card != null ? card.cardName : "?"); }
        foreach (EquipmentData item in panel.OfferedEquipment) { names.Add(item != null ? item.equipmentName : "?"); }
        foreach (SkipReward skip in panel.OfferedSkips) { names.Add(skip != null ? skip.Label : "?"); }

        return string.Join(" / ", names);
    }

    private void RemovalTick(CardRemovalPanel panel)
    {
        SyncRound(BattleManager.Instance);

        List<string> shown = new();

        foreach (CardData card in panel.ShownDeck) { shown.Add(card != null ? card.cardName : "-"); }

        BotEvent e = DecisionEvent("remove", $"removal:{lastSkip}:{string.Join(",", shown)}", out _);
        int index;

        if (replay != null)
        {
            if (!ReplayMatches(e, "removal", out BotEvent recorded)) { return; }

            index = recorded.index;
        }
        else
        {
            index = BotRewardPolicy.ChooseFromDeck(panel, lastSkip, profile, DecisionRng());
        }

        e.action = index < 0 ? "cancel" : lastSkip == BotSkipKind.Upgrade ? "upgrade" : "remove";
        e.index = index;
        e.choice = index >= 0 && index < panel.ShownDeck.Count && panel.ShownDeck[index] != null ? panel.ShownDeck[index].cardName : null;

        recorder.Choice(e, index < 0 ? "Removal screen: cancel" : $"{(lastSkip == BotSkipKind.Upgrade ? "Upgrade" : "Remove")} {e.choice}");

        if (index >= 0)
        {
            recorder.Reward(new BotRewardRecord
            {
                hero = "?",
                occasion = "clear",
                kind = e.action,
                choice = e.choice,
                offered = string.Empty,
            }, Array.Empty<CardData>(), null);
        }

        Reseed();

        if (index >= 0) { panel.Choose(index); }

        // Refused (ineligible) or nothing to pick - back out; the reward re-offers.
        if (!panel.Resolved) { panel.Cancel(); }

        AfterDecision();
    }

    private void DiscardTick(CardChoicePanel panel)
    {
        SyncRound(BattleManager.Instance);

        BotEvent e = DecisionEvent("discard", "discard", out _);
        int index;

        if (replay != null)
        {
            if (!ReplayMatches(e, "discard", out BotEvent recorded)) { return; }

            index = recorded.index;
        }
        else
        {
            index = BotRewardPolicy.ChooseDiscard(panel.ShownHand, profile, DecisionRng());
        }

        e.index = index;
        e.choice = index >= 0 && index < panel.ShownHand.Count && panel.ShownHand[index] != null ? panel.ShownHand[index].cardName : null;

        recorder.Choice(e, $"Discard {e.choice}");
        Reseed();

        panel.Choose(index);

        if (!panel.Resolved && panel.ShownHand.Count > 0) { panel.Choose(0); }

        AfterDecision();
    }

    // ---------------------------------------------------------------------------------------------
    // Watch-mode preview

    private static void Preview(BotCandidate play)
    {
        BattleManager battle = BattleManager.Instance;

        if (battle != null)
        {
            battle.SetActiveCharacter(play.Hero);
            battle.SetSelectedCharacter(play.Hero);
        }

        play.Card.ResetAim();

        for (int turns = 0; turns < play.AimTurns; turns++) { play.Card.RotateAim(play.Hero, play.Tile); }

        GridManager grid = GridManager.Instance;

        if (grid != null)
        {
            grid.ShowPlayableTiles(play.Card, play.Hero);
            grid.ShowAreaPreview(play.Card, play.Hero, play.Tile);
            grid.ShowDamagePreview(play.Card, play.Hero, play.Tile);
            grid.ShowPushPreview(play.Card, play.Hero, play.Tile);
        }

        if (AimHintLabel.Instance != null)
        {
            AimHintLabel.Instance.Show($"{play.Hero.DisplayName}: {play.Card.cardName} -> ({play.Tile.Coordinates.x},{play.Tile.Coordinates.y})");
        }
    }

    private static void ClearPreview()
    {
        GridManager grid = GridManager.Instance;

        if (grid != null)
        {
            grid.ClearPlayableTiles();
            grid.ClearAreaPreview();
            grid.ClearDamagePreview();
            grid.ClearPushPreview();
        }

        if (AimHintLabel.Instance != null) { AimHintLabel.Instance.Hide(); }
    }

    // ---------------------------------------------------------------------------------------------
    // Replay endings

    private void Diverge(string problem, string diff)
    {
        halted = true;
        pendingPlay = null;

        string dir = Path.Combine(job.replayRunDir ?? job.outputDir, "replay");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "divergence.txt"), problem + "\n\n" + (diff ?? string.Empty), BotText.Utf8);

        string where = replay != null ? $"after matching {replay.Matched} of {replay.Total} decisions" : string.Empty;

        Debug.LogWarning($"BotPilot: replay diverged {where} - {problem}");

        BotStatus.State = BotStatus.Diverged;
        BotStatus.Message = $"{problem} ({where})";
        BotStatus.Write(force: true);

        EndReplayRecord("Diverged", problem);

        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.Show("Replay diverged", $"{problem}\n\nSee replay/divergence.txt in the run folder.");
        }
    }

    private void CompleteReplay()
    {
        halted = true;

        BotStatus.State = BotStatus.ReplayComplete;

        // The recording's last decision is acknowledging this very notification, which a replay leaves up.
        BotStatus.Message = replay.Matched == replay.Total - 1
            ? $"replay matched all {replay.Matched} recorded decisions - the final {notificationOutcome} is left on screen"
            : $"replay matched {replay.Matched} of {replay.Total} recorded decisions";
        BotStatus.Write(force: true);

        Debug.LogWarning($"BotPilot: {BotStatus.Message}");

        EndReplayRecord(pendingOutcome ?? "Replayed", BotStatus.Message);
    }

    private void EndReplayRecord(string outcome, string note)
    {
        if (!runActive || recorder == null) { return; }

        recorder.FinishRun(outcome, note, Time.realtimeSinceStartup - runStartRealtime, frame - runStartFrame);
        runActive = false;
        recorder = null;
        BotPacing.Restore();
    }

    // ---------------------------------------------------------------------------------------------
    // Watchdog and errors

    private void MarkProgress()
    {
        lastProgressFrame = frame;
        lastProgressRealtime = Time.realtimeSinceStartup;
    }

    private string loopKey;
    private int loopKeyEvents;

    /// <summary>
    /// The watchdog's blind spot: a game that is busy but going nowhere. Events keep the progress
    /// signature moving, so a reaction that re-triggers itself forever never looks stalled - it looks
    /// like the busiest enemy phase ever. Thousands of events with the level, round, phase and decision
    /// all unchanged is that, and the run is cut off naming the action that kept resolving.
    /// </summary>
    private bool EndlessLoop(out string description)
    {
        description = null;

        BattleManager battle = BattleManager.Instance;

        if (battle == null || recorder == null) { return false; }

        string key = $"{levelIndex}|{battle.TurnsElapsed}|{(int)battle.Phase}|{step}";
        int events = recorder.EventCount;

        if (key != loopKey)
        {
            loopKey = key;
            loopKeyEvents = events;
            return false;
        }

        if (events - loopKeyEvents <= job.maxEventsPerPhase) { return false; }

        description = $"endless loop: {events - loopKeyEvents} events at L{LevelNumber} R{battle.TurnsElapsed} "
                      + $"{battle.Phase} without the battle moving on - the last action was {recorder.LastAction}";
        return true;
    }

    private string Signature()
    {
        BattleManager battle = BattleManager.Instance;
        string phase = battle != null ? $"{(int)battle.Phase}|{battle.TurnsElapsed}|{battle.TurnsRemaining}|{battle.EndTurnRequested}" : "-";
        int events = recorder != null ? recorder.EventCount : 0;

        return $"{SceneManager.GetActiveScene().handle}|{phase}|{step}|{events}|{(pendingPlay != null)}";
    }

    /// True when it acted on a stall this tick. Progress is any change to the signature above: a
    /// decision, anything the recorder heard, a phase or round change, a scene load.
    private bool Watchdog()
    {
        string signature = Signature();

        if (signature != lastSignature)
        {
            lastSignature = signature;
            MarkProgress();
            stallAttempts = 0;
            return false;
        }

        bool stalled = Watch
            ? Time.realtimeSinceStartup - lastProgressRealtime > (stallAttempts == 0 ? job.stallSecondsWatch : 15f)
            : frame - lastProgressFrame > (stallAttempts == 0 ? job.stallFrames : 120);

        if (!stalled) { return false; }

        stallAttempts++;
        MarkProgress();

        string diagnostic = Diagnostic();

        Debug.LogWarning($"BotPilot: no progress (attempt {stallAttempts}) - {diagnostic}");

        if (recorder != null) { recorder.Note($"! stalled (attempt {stallAttempts}): {diagnostic}"); }

        if (replay != null)
        {
            Diverge($"the replay stopped making progress - {diagnostic}", null);
            return true;
        }

        switch (stallAttempts)
        {
            case 1:
                if (NotificationManager.Instance != null && NotificationManager.Instance.IsShowing) { NotificationManager.Instance.Hide(); }
                if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen) { PauseMenu.Instance.Close(); }
                if (DebugPanel.Instance != null && DebugPanel.Instance.IsOpen) { DebugPanel.Instance.Close(); }
                break;
            case 2:
                LootManager loot = LootManager.Instance;
                if (loot != null && loot.Panel != null && loot.Panel.IsShowing) { loot.Panel.ChooseSkip(null); }
                if (CardRemovalPanel.Instance != null && CardRemovalPanel.Instance.IsShowing) { CardRemovalPanel.Instance.Cancel(); }
                if (CardChoicePanel.Instance != null && CardChoicePanel.Instance.IsShowing) { CardChoicePanel.Instance.Choose(0); }
                break;
            case 3:
                if (BattleManager.Instance != null) { BattleManager.Instance.RequestEndTurn(); }
                break;
            default:
                Abort("Stalled", diagnostic);
                break;
        }

        return true;
    }

    private string Diagnostic()
    {
        BattleManager battle = BattleManager.Instance;

        if (battle == null) { return $"scene {SceneManager.GetActiveScene().name}, no BattleManager"; }

        return $"L{LevelNumber} R{round} step {step}: phase {battle.Phase}, endTurnRequested {battle.EndTurnRequested}, "
               + $"inputLocked {battle.InputLocked}, actionsIdle {(ActionManager.Instance == null || ActionManager.Instance.IsIdle)}, "
               + $"lootIdle {(LootManager.Instance == null || LootManager.Instance.IsIdle)}, "
               + $"handBusy {(ActiveHandViewer.Instance != null && ActiveHandViewer.Instance.Busy)}, "
               + $"notification {(NotificationManager.Instance != null && NotificationManager.Instance.IsShowing)}, "
               + $"reward {(LootManager.Instance != null && LootManager.Instance.Panel != null && LootManager.Instance.Panel.IsShowing)}, "
               + $"removal {(CardRemovalPanel.Instance != null && CardRemovalPanel.Instance.IsShowing)}, "
               + $"discard {(CardChoicePanel.Instance != null && CardChoicePanel.Instance.IsShowing)}, "
               + $"paused {(PauseMenu.Instance != null && PauseMenu.Instance.IsOpen)}";
    }

    private void OnLog(string condition, string stackTrace, LogType type)
    {
        // A recorder write that itself logs must not come back through here.
        if (inLogHook || !runActive || recorder == null) { return; }

        inLogHook = true;

        try
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                recorder.Error(type.ToString(), condition, stackTrace);
                runErrors++;
                BotStatus.Errors++;

                if (runErrors > 50 && !abortRequested)
                {
                    abortRequested = true;
                    abortNote = $"{runErrors} errors - last: {condition}";
                }
            }
            else if (type == LogType.Warning)
            {
                recorder.Warning(condition);
            }
        }
        catch (Exception) { /* never throw back into the logger */ }
        finally { inLogHook = false; }
    }

    private void OnTickException(Exception ex)
    {
        Debug.LogException(ex);

        if (++tickExceptions > 20 && runActive && !halted)
        {
            Abort("Error", $"the bot kept failing: {ex.Message}");
        }
    }
}
#endif
