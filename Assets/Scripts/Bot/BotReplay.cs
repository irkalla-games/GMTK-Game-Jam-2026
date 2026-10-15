#if UNITY_EDITOR || BOT_RUNNER
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// A recorded run's decisions, handed back one at a time for BotPilot to re-apply - the planner never
/// runs in a replay, so editing a profile after recording cannot change what a replay does.
///
/// Every decision is checked before it is used: the live game has to be at the same level, round and
/// step, and its fingerprint (BotFingerprint) has to match the recorded one. Anything else is a
/// divergence - content edited since the recording, or one of the frame-timing effects BotPilot's
/// determinism notes describe - and the replay stops right there with both fingerprints diffed, rather
/// than carrying on into a battle that no longer matches the log.
/// </summary>
public sealed class BotReplay
{
    public BotRunRecord Header { get; private set; }

    public int Matched { get; private set; }

    public int Total => decisions.Count;

    public bool Finished => cursor >= decisions.Count;

    private readonly List<BotEvent> decisions = new();
    private int cursor;

    public static BotReplay Load(string runDir, out string problem)
    {
        problem = null;

        string runPath = Path.Combine(runDir, "run.json");
        string eventsPath = Path.Combine(runDir, "events.jsonl");

        if (!File.Exists(runPath) || !File.Exists(eventsPath))
        {
            problem = $"{runDir} has no run.json/events.jsonl to replay";
            return null;
        }

        BotReplay replay = new() { Header = JsonUtility.FromJson<BotRunRecord>(File.ReadAllText(runPath)) };

        foreach (string line in File.ReadLines(eventsPath))
        {
            if (string.IsNullOrWhiteSpace(line)) { continue; }

            BotEvent e = JsonUtility.FromJson<BotEvent>(line);

            if (e != null && e.kind == "decision") { replay.decisions.Add(e); }
        }

        if (replay.decisions.Count == 0) { problem = $"{runDir} recorded no decisions"; }

        return replay;
    }

    /// <summary>
    /// The recorded decision for this point, or null with `problem` saying why it cannot be used. On a
    /// fingerprint mismatch the diff of the two fingerprints comes back in `diff`.
    /// </summary>
    public BotEvent Next(string context, int level, int round, int step, string fp, string fpText,
                         out string problem, out string diff)
    {
        problem = null;
        diff = null;

        if (Finished)
        {
            problem = $"the recording ends after {decisions.Count} decisions, but the game asked for another at "
                      + $"L{level} R{round} step {step} ({context})";
            return null;
        }

        BotEvent e = decisions[cursor];

        if (e.level != level || e.round != round || e.step != step)
        {
            problem = $"recorded decision #{cursor + 1} is at L{e.level} R{e.round} step {e.step} ({e.action}), but the game "
                      + $"is asking at L{level} R{round} step {step} ({context})";
            diff = Diff(e.fpText, fpText);
            return null;
        }

        if (e.fp != fp)
        {
            problem = $"the board differs from the recording at L{level} R{round} step {step} ({context})";
            diff = Diff(e.fpText, fpText);
            return null;
        }

        cursor++;
        Matched++;

        return e;
    }

    /// Line-by-line: every line of either fingerprint the other does not have.
    public static string Diff(string recorded, string live)
    {
        string[] a = (recorded ?? string.Empty).Split('\n');
        string[] b = (live ?? string.Empty).Split('\n');
        HashSet<string> inA = new(a);
        HashSet<string> inB = new(b);
        StringBuilder sb = new();

        sb.AppendLine("--- recorded");

        foreach (string line in a)
        {
            if (!inB.Contains(line)) { sb.AppendLine("- " + line); }
        }

        sb.AppendLine("+++ live");

        foreach (string line in b)
        {
            if (!inA.Contains(line)) { sb.AppendLine("+ " + line); }
        }

        return sb.ToString();
    }
}
#endif
