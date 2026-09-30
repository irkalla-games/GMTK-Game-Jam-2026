#if UNITY_EDITOR || BOT_RUNNER
using System;
using System.Collections.Generic;

/// Turbo fast-forwards game time and skips the turn banner; Watch plays at a speed a person can follow.
public enum BotSpeed
{
    Turbo = 0,
    Watch = 1,
}

/// Play drives runs with the planner; Replay re-plays one recorded run's decisions.
public enum BotMode
{
    Play = 0,
    Replay = 1,
}

/// One hero in the bot's party, by asset name so a job file stays readable and survives a headless
/// player that cannot follow asset references.
[Serializable]
public class BotPartySlot
{
    /// A CharacterOption asset name - "Knight".
    public string hero;

    /// A DeckData asset name - "KnightStarter". Empty means the option's starter deck.
    public string deck;
}

/// <summary>
/// One batch of bot runs: the campaign, party, difficulty, the profiles to compare and how many runs
/// each, how fast, and where the results go. Written to the batch folder as job.json before anything
/// runs, so every report can say exactly what produced it.
///
/// Plain [Serializable] fields only - JsonUtility is the one serializer, and it reads fields, not
/// properties.
/// </summary>
[Serializable]
public class BotJob
{
    public int version = 1;
    public string batchId;
    public string label;
    public string createdUtc;

    /// git HEAD and whether the tree had local changes when the batch started - a replay warns when
    /// either differs, since content edits change what the recorded decisions meet.
    public string commit;
    public bool dirtyTree;

    public string campaign = "RealRun - Random";
    public List<BotPartySlot> party = new();
    public int tier;

    /// Profiles by value - see BotProfileData.
    public List<BotProfileData> profiles = new();
    public int runsPerProfile = 1;
    public int baseSeed = 12345;

    public BotSpeed speed = BotSpeed.Turbo;

    /// Game seconds per frame in Turbo. Every scaled wait up to this long finishes in one frame; kept
    /// under Time.maximumDeltaTime's default 0.333. Results do not depend on it - rules roll GameDice,
    /// which frames cannot shift - so the largest step is simply the fastest.
    public float turboStep = 0.25f;

    /// Watch playback speed, 1-4.
    public float watchSpeed = 1f;

    /// Real seconds Watch lingers on each decision, with the play previewed on the board.
    public float watchPause = 0.5f;

    /// The batch folder, absolute. Runs land in runs/<run id>/ under it.
    public string outputDir;

    public BotMode mode = BotMode.Play;

    /// Replay only: the recorded run folder to re-play.
    public string replayRunDir;

    /// Headless sharding: this process plays runs whose global index % shardCount == shardIndex.
    public int shardIndex;
    public int shardCount = 1;

    public int maxPlaysPerTurn = 40;

    /// Events (hits, actions, heals) the game may produce without its phase or round moving on before
    /// the run is aborted as an endless loop - a reaction re-triggering itself, say. A busy enemy phase
    /// is a few hundred.
    public int maxEventsPerPhase = 2000;

    public int stallFrames = 600;
    public float stallSecondsWatch = 90f;
    public int maxDecisionsPerRun = 20000;
    public float maxRunSeconds = 900f;
    public bool writeTurnLog = true;

    /// Diagnostics: write rng-trace.txt per run - the game's dice state at every event the recorder
    /// hears and every frame it changed, for finding which roll made two runs part ways.
    public bool traceRng;

    /// Leave Play Mode (Editor) or quit (player) when the batch finishes. Replays leave the board up.
    public bool exitWhenDone = true;

    /// <summary>
    /// Every run this process should play, in order. Interleaved by seed - run 0 of every profile, then
    /// run 1 of every profile - so a batch stopped halfway still has matched pairs to compare.
    /// </summary>
    public List<BotRunKey> Runs()
    {
        List<BotRunKey> runs = new();
        int shards = Math.Max(1, shardCount);

        for (int r = 0; r < Math.Max(1, runsPerProfile); r++)
        {
            for (int p = 0; p < profiles.Count; p++)
            {
                int global = r * profiles.Count + p;

                if (global % shards != shardIndex) { continue; }

                runs.Add(new BotRunKey
                {
                    globalIndex = global,
                    runIndex = r,
                    profileIndex = p,
                    profileName = profiles[p] != null ? profiles[p].name : "profile",
                });
            }
        }

        return runs;
    }

    /// Why this job cannot run, or null if it can.
    public string Validate()
    {
        if (string.IsNullOrWhiteSpace(campaign)) { return "no campaign named"; }
        if (party == null || party.Count == 0) { return "the party is empty"; }

        foreach (BotPartySlot slot in party)
        {
            if (slot == null || string.IsNullOrWhiteSpace(slot.hero)) { return "a party slot names no hero"; }
        }

        if (mode == BotMode.Play && (profiles == null || profiles.Count == 0)) { return "no profiles to run"; }
        if (mode == BotMode.Replay && string.IsNullOrWhiteSpace(replayRunDir)) { return "replay needs a run folder"; }
        if (runsPerProfile < 1) { return "runs per profile must be at least 1"; }
        if (string.IsNullOrWhiteSpace(outputDir)) { return "no output folder"; }
        if (shardCount < 1 || shardIndex < 0 || shardIndex >= shardCount) { return "bad shard index/count"; }

        return null;
    }
}

/// One run of a batch: which seed (runIndex) and which profile.
[Serializable]
public struct BotRunKey
{
    public int globalIndex;
    public int runIndex;
    public int profileIndex;
    public string profileName;

    /// The run's folder name - "r003-Balanced".
    public string RunId => $"r{runIndex:D3}-{BotText.Slug(profileName)}";
}
#endif
