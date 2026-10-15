#if UNITY_EDITOR || BOT_RUNNER
using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Where the bot has got to, for whoever is watching: the Bot Runner window reads the statics while it
/// shares the domain with Play Mode, and anything outside the process - the Editor bridge after a domain
/// reload, a PowerShell script watching a headless shard, Claude polling from the CLI - reads the
/// status.json this writes into the batch folder every couple of seconds.
///
/// StopRequested is the one field written from outside: the window's Stop button sets it, and the pilot
/// winds the batch down at its next tick with whatever it has finished.
/// </summary>
public static class BotStatus
{
    public const string Idle = "Idle";
    public const string Running = "Running";
    public const string Finished = "Finished";
    public const string Diverged = "Diverged";
    public const string ReplayComplete = "ReplayComplete";
    public const string Failed = "Failed";

    public static string State = Idle;
    public static string Message;
    public static string BatchDir;
    public static string StatusFile;
    public static string CurrentRun;
    public static int RunsTotal;
    public static int RunsDone;
    public static int Won;
    public static int Died;
    public static int Stalled;
    public static int Errors;
    public static int Level;
    public static int Round;
    public static bool StopRequested;
    public static bool ExitWhenDone;

    private static float lastWrite = -999f;

    /// A Stopwatch rather than Time.realtimeSinceStartup, which the Editor restarts when Play Mode begins
    /// and ends - the final write, from the pilot's OnDestroy on the way out, would read a negative time.
    private static readonly System.Diagnostics.Stopwatch elapsed = new();

    [Serializable]
    private class Snapshot
    {
        public string state;
        public string message;
        public string batchDir;
        public string currentRun;
        public int runsTotal;
        public int runsDone;
        public int won;
        public int died;
        public int stalled;
        public int errors;
        public int level;
        public int round;
        public float elapsedSeconds;
        public string updatedUtc;
    }

    /// Clears every counter for a new batch. Statics survive between Play Mode sessions in the Editor
    /// when no domain reload happens on the way out, so a second batch must not inherit the first's.
    public static void Reset(string batchDir, string statusFile, int runsTotal, bool exitWhenDone)
    {
        State = Running;
        Message = null;
        BatchDir = batchDir;
        StatusFile = statusFile;
        CurrentRun = null;
        RunsTotal = runsTotal;
        RunsDone = Won = Died = Stalled = Errors = 0;
        Level = Round = 0;
        elapsed.Restart();
        StopRequested = false;
        ExitWhenDone = exitWhenDone;
        lastWrite = -999f;
    }

    public static string ToJson()
    {
        Snapshot s = new()
        {
            state = State,
            message = Message,
            batchDir = BatchDir,
            currentRun = CurrentRun,
            runsTotal = RunsTotal,
            runsDone = RunsDone,
            won = Won,
            died = Died,
            stalled = Stalled,
            errors = Errors,
            level = Level,
            round = Round,
            elapsedSeconds = (float)elapsed.Elapsed.TotalSeconds,
            updatedUtc = DateTime.UtcNow.ToString("o"),
        };

        return JsonUtility.ToJson(s, prettyPrint: true);
    }

    /// Writes status.json, throttled to one write every couple of real seconds unless forced.
    public static void Write(bool force = false)
    {
        if (string.IsNullOrEmpty(StatusFile)) { return; }

        float now = Time.realtimeSinceStartup;

        if (!force && now - lastWrite < 2f) { return; }

        lastWrite = now;

        try { BotText.WriteAtomic(StatusFile, ToJson()); }
        catch (IOException) { /* a reader holding the file this instant - the next write gets it */ }
    }

    public static string Summary =>
        $"{State}: {RunsDone}/{RunsTotal} runs, {Won} won, {Died} died, {Stalled} stalled, {Errors} errors"
        + (string.IsNullOrEmpty(CurrentRun) ? string.Empty : $" - {CurrentRun} L{Level} R{Round}")
        + (string.IsNullOrEmpty(Message) ? string.Empty : $" - {Message}");
}
#endif
