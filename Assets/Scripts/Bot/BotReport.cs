#if UNITY_EDITOR || BOT_RUNNER
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Turns a batch folder of runs/*/run.json into report.md and a set of CSVs. Runtime code rather than
/// Editor code so the headless player can build it too (-botReport &lt;folder&gt;), which is how parallel
/// shards are merged - rebuilding from the folder means the report is always of every run that
/// finished, whoever played it.
///
/// Enemy, level and card tables leave out baseline profiles (Random): a bot playing at random is there
/// to be beaten, and its deaths would otherwise flatter every enemy it met. The overview and the
/// profile comparison still include it - that is where it earns its keep.
/// </summary>
public static class BotReport
{
    private const float LowSampleBattles = 10f;

    public static string Build(string batchDir)
    {
        List<BotRunRecord> runs = LoadRuns(batchDir);
        BotJob job = LoadJob(batchDir);

        List<string> profiles = new();

        foreach (BotRunRecord run in runs)
        {
            if (!profiles.Contains(run.profile)) { profiles.Add(run.profile); }
        }

        List<BotRunRecord> real = runs.FindAll(r => !r.baseline);

        StringBuilder md = new();

        Header(md, batchDir, job, runs, profiles);
        Overview(md, runs, profiles);
        WhereRunsEnd(md, runs, profiles);
        LevelDifficulty(md, real);
        EnemyThreat(md, real, out List<string[]> enemyRows);
        Killers(md, real);
        Heroes(md, runs, profiles, out List<string[]> heroRows);
        Cards(md, real, out List<string[]> cardRows);
        Rewards(md, real);
        SharedSeeds(md, runs, profiles);
        Behaviour(md, runs, profiles);
        Performance(md, runs, profiles);
        Errors(md, runs);

        string reportPath = Path.Combine(batchDir, "report.md");
        BotText.WriteAtomic(reportPath, md.ToString());

        WriteRunsCsv(batchDir, runs);
        WriteLevelsCsv(batchDir, runs);
        WriteCsv(Path.Combine(batchDir, "enemies.csv"), EnemyHeader, enemyRows);
        WriteCsv(Path.Combine(batchDir, "heroes.csv"), HeroHeader, heroRows);
        WriteCsv(Path.Combine(batchDir, "cards.csv"), CardHeader, cardRows);
        WriteRewardsCsv(batchDir, runs);
        WriteErrorsCsv(batchDir, runs);

        return reportPath;
    }

    // ---------------------------------------------------------------------------------------------
    // Loading

    public static List<BotRunRecord> LoadRuns(string batchDir)
    {
        List<BotRunRecord> runs = new();
        string runsDir = Path.Combine(batchDir, "runs");

        if (!Directory.Exists(runsDir)) { return runs; }

        string[] folders = Directory.GetDirectories(runsDir);
        Array.Sort(folders, StringComparer.Ordinal);

        foreach (string folder in folders)
        {
            string path = Path.Combine(folder, "run.json");

            if (!File.Exists(path)) { continue; }

            try
            {
                BotRunRecord run = JsonUtility.FromJson<BotRunRecord>(File.ReadAllText(path));

                if (run != null && run.outcome != "Running") { runs.Add(run); }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"BotReport: skipped unreadable {path}: {ex.Message}");
            }
        }

        return runs;
    }

    private static BotJob LoadJob(string batchDir)
    {
        string path = Path.Combine(batchDir, "job.json");

        try { return File.Exists(path) ? JsonUtility.FromJson<BotJob>(File.ReadAllText(path)) : null; }
        catch (Exception) { return null; }
    }

    // ---------------------------------------------------------------------------------------------
    // Sections

    private static void Header(StringBuilder md, string batchDir, BotJob job, List<BotRunRecord> runs, List<string> profiles)
    {
        md.AppendLine($"# Bot balance report - {Path.GetFileName(batchDir.TrimEnd('/', '\\'))}");
        md.AppendLine();

        if (job != null)
        {
            List<string> party = new();

            foreach (BotPartySlot slot in job.party) { party.Add(string.IsNullOrEmpty(slot.deck) ? slot.hero : $"{slot.hero} ({slot.deck})"); }

            md.AppendLine($"- **Campaign:** {job.campaign}, difficulty tier {job.tier}");
            md.AppendLine($"- **Party:** {string.Join(", ", party)}");
            md.AppendLine($"- **Profiles:** {string.Join(", ", profiles)}; {job.runsPerProfile} run(s) each from base seed {job.baseSeed}");
            md.AppendLine($"- **Build:** commit {job.commit}{(job.dirtyTree ? " with local changes" : string.Empty)}, {job.speed}"
                          + (job.speed == BotSpeed.Turbo ? $" (step {BotText.F(job.turboStep)})" : string.Empty));
        }

        md.AppendLine($"- **Runs in this report:** {runs.Count} (generated {DateTime.Now:yyyy-MM-dd HH:mm})");
        md.AppendLine();
        md.AppendLine("Damage counts what registered: health lost plus what a Shield absorbed. \"Prevented\" is what Block, "
                      + "Parry and Dodge stopped outright. Enemy, level and card tables leave out baseline profiles (Random).");
        md.AppendLine();
    }

    private static void Overview(StringBuilder md, List<BotRunRecord> runs, List<string> profiles)
    {
        md.AppendLine("## Overview");
        md.AppendLine();

        List<string[]> rows = new();

        foreach (string profile in profiles)
        {
            List<BotRunRecord> mine = runs.FindAll(r => r.profile == profile);
            int won = mine.FindAll(r => r.outcome == "Won").Count;
            int died = mine.FindAll(r => r.outcome == "Died").Count;
            int other = mine.Count - won - died;
            (float low, float high) = Wilson(won, mine.Count);

            rows.Add(new[]
            {
                profile + (mine.Count > 0 && mine[0].baseline ? " (baseline)" : string.Empty),
                BotText.I(mine.Count), BotText.I(won), BotText.I(died), BotText.I(other),
                $"{Pct(won, mine.Count)} ({BotText.F(low * 100f, 0)}-{BotText.F(high * 100f, 0)}%)",
                BotText.F(Avg(mine, r => r.levelsCleared), 1),
                BotText.F(Avg(mine, r => r.dmgDealt), 0),
                BotText.F(Avg(mine, r => r.dmgTaken), 0),
                BotText.F(Avg(mine, r => r.falls), 1),
            });
        }

        Table(md, new[] { "Profile", "Runs", "Won", "Died", "Stalled/other", "Win rate (95% CI)", "Avg levels cleared",
                          "Avg dealt", "Avg taken", "Avg falls" }, rows);
    }

    private static void WhereRunsEnd(StringBuilder md, List<BotRunRecord> runs, List<string> profiles)
    {
        md.AppendLine("## Where runs end");
        md.AppendLine();
        md.AppendLine("Runs that did not win, by the level they ended on, and who landed the blows in the final wipe.");
        md.AppendLine();

        SortedDictionary<int, string> names = new();
        SortedDictionary<int, Dictionary<string, int>> killersAt = new();

        foreach (BotRunRecord run in runs)
        {
            if (run.outcome == "Won" || run.endLevel < 1) { continue; }

            names[run.endLevel] = run.endLevelName;

            if (!killersAt.TryGetValue(run.endLevel, out Dictionary<string, int> killers))
            {
                killers = new Dictionary<string, int>();
                killersAt[run.endLevel] = killers;
            }

            foreach (string killer in run.killers) { killers[killer] = killers.TryGetValue(killer, out int n) ? n + 1 : 1; }
        }

        if (names.Count == 0)
        {
            md.AppendLine("Every run won.");
            md.AppendLine();
            return;
        }

        List<string> headers = new() { "Level" };
        headers.AddRange(profiles);
        headers.Add("Top killers");

        List<string[]> rows = new();

        foreach (KeyValuePair<int, string> level in names)
        {
            List<string> row = new() { $"{level.Key}: {level.Value}" };

            foreach (string profile in profiles)
            {
                List<BotRunRecord> mine = runs.FindAll(r => r.profile == profile);
                int ended = mine.FindAll(r => r.outcome != "Won" && r.endLevel == level.Key).Count;

                row.Add(ended > 0 ? $"{ended} ({Pct(ended, mine.Count)})" : "-");
            }

            row.Add(TopKeys(killersAt[level.Key], 3));
            rows.Add(row.ToArray());
        }

        Table(md, headers.ToArray(), rows);
    }

    private static void LevelDifficulty(StringBuilder md, List<BotRunRecord> runs)
    {
        md.AppendLine("## Level difficulty");
        md.AppendLine();

        SortedDictionary<int, List<BotLevelRecord>> byLevel = ByLevel(runs);
        List<string[]> rows = new();

        foreach (KeyValuePair<int, List<BotLevelRecord>> entry in byLevel)
        {
            List<BotLevelRecord> levels = entry.Value;
            int cleared = levels.FindAll(l => l.result == "Cleared" || l.result == "Won").Count;
            Dictionary<string, int> killers = new();

            foreach (BotLevelRecord level in levels)
            {
                foreach (BotFallRecord fall in level.fallList) { killers[fall.killer] = killers.TryGetValue(fall.killer, out int n) ? n + 1 : 1; }
            }

            float hpEnd = 0f;
            int hpCount = 0;

            foreach (BotLevelRecord level in levels)
            {
                if (level.hpMaxStart <= 0) { continue; }

                hpEnd += (float)level.hpEnd / level.hpMaxStart;
                hpCount++;
            }

            rows.Add(new[]
            {
                $"{entry.Key}: {levels[0].name}{(levels.Exists(l => l.boss) ? " (boss)" : string.Empty)}",
                BotText.I(levels.Count), Pct(cleared, levels.Count),
                BotText.F(AvgL(levels, l => l.rounds), 1),
                BotText.F(AvgL(levels, l => l.dmgTaken), 0),
                BotText.F(AvgL(levels, l => l.dmgDealt), 0),
                BotText.F(AvgL(levels, l => l.falls), 2),
                hpCount > 0 ? Pct(hpEnd, hpCount) : "-",
                BotText.F(AvgL(levels, l => l.enemiesOpening + l.enemiesWaves + l.enemiesSummoned), 1),
                TopKeys(killers, 3),
            });
        }

        Table(md, new[] { "Level", "Reached", "Cleared", "Avg rounds", "Avg taken", "Avg dealt", "Avg falls",
                          "Party HP left", "Avg enemies", "Falls caused by" }, rows);
    }

    private static readonly string[] EnemyHeader =
    {
        "enemy", "boss", "power", "tier", "battles", "battle_share", "bodies", "dmg_to_heroes", "dmg_to_allies",
        "dmg_per_battle", "dmg_per_body", "falls_caused", "killed", "kill_rate", "rounds_to_kill", "dmg_taken",
        "presence_delta", "low_sample",
    };

    private static void EnemyThreat(StringBuilder md, List<BotRunRecord> runs, out List<string[]> csv)
    {
        csv = new List<string[]>();

        md.AppendLine("## Enemy threat, tiered by how often each enemy is seen");
        md.AppendLine();
        md.AppendLine("Tiers by share of battles an enemy appears in: **Common** 20%+, **Uncommon** 5-20%, **Rare** under 5%. "
                      + "Sorted by damage to heroes per battle it was in. \"Δ taken\" is how much more damage the party took in "
                      + "battles with it than in the same level's battles without it - the cleanest read of what it adds. "
                      + "Rows under 10 battles are marked *low sample*.");
        md.AppendLine();

        Dictionary<string, BotEnemyStat> total = new();
        int battles = 0;

        foreach (BotRunRecord run in runs)
        {
            battles += run.levels.Count;

            foreach (BotEnemyStat stat in run.enemies)
            {
                if (!total.TryGetValue(stat.type, out BotEnemyStat sum))
                {
                    sum = new BotEnemyStat { type = stat.type, boss = stat.boss, power = stat.power };
                    total[stat.type] = sum;
                }

                sum.bodies += stat.bodies;
                sum.battles += stat.battles;
                sum.dmgToHeroes += stat.dmgToHeroes;
                sum.dmgToAllies += stat.dmgToAllies;
                sum.prevented += stat.prevented;
                sum.fallsCaused += stat.fallsCaused;
                sum.killed += stat.killed;
                sum.roundsAlive += stat.roundsAlive;
                sum.dmgTaken += stat.dmgTaken;
            }
        }

        if (total.Count == 0 || battles == 0)
        {
            md.AppendLine("No enemies recorded.");
            md.AppendLine();
            return;
        }

        Dictionary<string, float> delta = PresenceDelta(runs);
        List<BotEnemyStat> sorted = new(total.Values);
        sorted.Sort((a, b) => PerBattle(b).CompareTo(PerBattle(a)));

        foreach (string tier in new[] { "Common", "Uncommon", "Rare" })
        {
            List<string[]> rows = new();

            foreach (BotEnemyStat stat in sorted)
            {
                float share = (float)stat.battles / battles;

                if (TierOf(share) != tier) { continue; }

                bool low = stat.battles < LowSampleBattles;
                string d = delta.TryGetValue(stat.type, out float dv) ? (dv >= 0 ? "+" : string.Empty) + BotText.F(dv, 1) : "-";

                rows.Add(new[]
                {
                    stat.type + (stat.boss ? " (boss)" : string.Empty) + (low ? " *low sample*" : string.Empty),
                    $"{stat.battles} ({Pct(share, 1)})", BotText.I(stat.bodies),
                    BotText.F(PerBattle(stat), 1),
                    BotText.F(stat.bodies > 0 ? (float)stat.dmgToHeroes / stat.bodies : 0f, 1),
                    d,
                    BotText.I(stat.fallsCaused),
                    Pct(stat.killed, stat.bodies),
                    BotText.F(stat.killed > 0 ? (float)stat.roundsAlive / stat.killed : 0f, 1),
                });

                csv.Add(new[]
                {
                    stat.type, stat.boss ? "1" : "0", BotText.F(stat.power, 2), tier, BotText.I(stat.battles),
                    BotText.F(share, 3), BotText.I(stat.bodies), BotText.I(stat.dmgToHeroes), BotText.I(stat.dmgToAllies),
                    BotText.F(PerBattle(stat), 2), BotText.F(stat.bodies > 0 ? (float)stat.dmgToHeroes / stat.bodies : 0f, 2),
                    BotText.I(stat.fallsCaused), BotText.I(stat.killed), BotText.F(stat.bodies > 0 ? (float)stat.killed / stat.bodies : 0f, 3),
                    BotText.F(stat.killed > 0 ? (float)stat.roundsAlive / stat.killed : 0f, 2), BotText.I(stat.dmgTaken),
                    delta.TryGetValue(stat.type, out float dvc) ? BotText.F(dvc, 2) : string.Empty, low ? "1" : "0",
                });
            }

            md.AppendLine($"### {tier}");
            md.AppendLine();

            if (rows.Count == 0)
            {
                md.AppendLine("None.");
                md.AppendLine();
                continue;
            }

            Table(md, new[] { "Enemy", "Seen in (share)", "Bodies", "Dmg to heroes / battle", "Dmg / body", "Δ taken",
                              "Falls caused", "Killed", "Rounds to kill" }, rows);
        }
    }

    private static float PerBattle(BotEnemyStat stat) => stat.battles > 0 ? (float)stat.dmgToHeroes / stat.battles : 0f;

    private static string TierOf(float share) => share >= 0.2f ? "Common" : share >= 0.05f ? "Uncommon" : "Rare";

    /// <summary>
    /// Per enemy type: mean damage the party took in battles where it appeared, minus the mean for the
    /// same level number's battles where it did not - averaged over levels, weighted by how often it
    /// appeared there. Levels where it always (or never) appears say nothing and are skipped.
    /// </summary>
    private static Dictionary<string, float> PresenceDelta(List<BotRunRecord> runs)
    {
        Dictionary<string, float> weighted = new();
        Dictionary<string, int> weights = new();

        foreach (KeyValuePair<int, List<BotLevelRecord>> entry in ByLevel(runs))
        {
            List<BotLevelRecord> levels = entry.Value;
            HashSet<string> types = new();

            foreach (BotLevelRecord level in levels) { types.UnionWith(level.enemyTypes); }

            foreach (string type in types)
            {
                float with = 0f, without = 0f;
                int nWith = 0, nWithout = 0;

                foreach (BotLevelRecord level in levels)
                {
                    if (level.enemyTypes.Contains(type)) { with += level.dmgTaken; nWith++; }
                    else { without += level.dmgTaken; nWithout++; }
                }

                if (nWith == 0 || nWithout == 0) { continue; }

                float d = with / nWith - without / nWithout;

                weighted[type] = (weighted.TryGetValue(type, out float w) ? w : 0f) + d * nWith;
                weights[type] = (weights.TryGetValue(type, out int n) ? n : 0) + nWith;
            }
        }

        Dictionary<string, float> result = new();

        foreach (KeyValuePair<string, float> entry in weighted) { result[entry.Key] = entry.Value / weights[entry.Key]; }

        return result;
    }

    private static void Killers(StringBuilder md, List<BotRunRecord> runs)
    {
        md.AppendLine("## Who knocks heroes down");
        md.AppendLine();

        Dictionary<string, int> falls = new();
        Dictionary<string, int> wipes = new();

        foreach (BotRunRecord run in runs)
        {
            foreach (BotLevelRecord level in run.levels)
            {
                foreach (BotFallRecord fall in level.fallList) { falls[fall.killer] = falls.TryGetValue(fall.killer, out int n) ? n + 1 : 1; }
            }

            foreach (string killer in run.killers) { wipes[killer] = wipes.TryGetValue(killer, out int n) ? n + 1 : 1; }
        }

        if (falls.Count == 0)
        {
            md.AppendLine("No hero fell.");
            md.AppendLine();
            return;
        }

        List<string> keys = new(falls.Keys);
        keys.Sort((a, b) => falls[b] != falls[a] ? falls[b].CompareTo(falls[a]) : string.CompareOrdinal(a, b));

        List<string[]> rows = new();

        foreach (string key in keys)
        {
            rows.Add(new[] { key, BotText.I(falls[key]), BotText.I(wipes.TryGetValue(key, out int w) ? w : 0) });
        }

        Table(md, new[] { "Last hit by", "Hero falls", "Run-ending wipes" }, rows);
    }

    private static readonly string[] HeroHeader =
    {
        "profile", "hero", "runs", "dmg_dealt", "dmg_taken", "healing_done", "healing_received", "shield_absorbed",
        "prevented", "falls", "cards_played", "energy_unspent",
    };

    private static void Heroes(StringBuilder md, List<BotRunRecord> runs, List<string> profiles, out List<string[]> csv)
    {
        csv = new List<string[]>();

        md.AppendLine("## Heroes");
        md.AppendLine();

        List<string[]> rows = new();

        foreach (string profile in profiles)
        {
            List<BotRunRecord> mine = runs.FindAll(r => r.profile == profile);
            Dictionary<string, BotHeroStat> sums = new();
            List<string> order = new();

            foreach (BotRunRecord run in mine)
            {
                foreach (BotHeroStat hero in run.heroes)
                {
                    if (!sums.TryGetValue(hero.hero, out BotHeroStat sum))
                    {
                        sum = new BotHeroStat { hero = hero.hero };
                        sums[hero.hero] = sum;
                        order.Add(hero.hero);
                    }

                    sum.dmgDealt += hero.dmgDealt;
                    sum.dmgTaken += hero.dmgTaken;
                    sum.healingDone += hero.healingDone;
                    sum.healingReceived += hero.healingReceived;
                    sum.shieldAbsorbed += hero.shieldAbsorbed;
                    sum.prevented += hero.prevented;
                    sum.falls += hero.falls;
                    sum.cardsPlayed += hero.cardsPlayed;
                    sum.energyUnspent += hero.energyUnspent;
                }
            }

            float n = Mathf.Max(1, mine.Count);

            foreach (string name in order)
            {
                BotHeroStat s = sums[name];

                rows.Add(new[]
                {
                    profile, name, BotText.F(s.dmgDealt / n, 0), BotText.F(s.dmgTaken / n, 0), BotText.F(s.healingDone / n, 0),
                    BotText.F(s.shieldAbsorbed / n, 0), BotText.F(s.prevented / n, 0), BotText.F(s.falls / n, 2),
                    BotText.F(s.cardsPlayed / n, 0), BotText.F(s.energyUnspent / n, 1),
                });

                csv.Add(new[]
                {
                    profile, name, BotText.I(mine.Count), BotText.I(s.dmgDealt), BotText.I(s.dmgTaken), BotText.I(s.healingDone),
                    BotText.I(s.healingReceived), BotText.I(s.shieldAbsorbed), BotText.I(s.prevented), BotText.I(s.falls),
                    BotText.I(s.cardsPlayed), BotText.I(s.energyUnspent),
                });
            }
        }

        md.AppendLine("Per run, averaged.");
        md.AppendLine();
        Table(md, new[] { "Profile", "Hero", "Dealt", "Taken", "Healing done", "Shield absorbed", "Prevented", "Falls",
                          "Cards played", "Energy left unspent" }, rows);
    }

    private static readonly string[] CardHeader =
    {
        "card", "category", "rarity", "cost", "offered", "picked", "pick_rate", "played", "played_per_run",
        "avg_score", "dmg_dealt", "dmg_per_play",
    };

    private static void Cards(StringBuilder md, List<BotRunRecord> runs, out List<string[]> csv)
    {
        csv = new List<string[]>();

        md.AppendLine("## Cards");
        md.AppendLine();

        Dictionary<string, BotCardStat> sums = new();

        foreach (BotRunRecord run in runs)
        {
            foreach (BotCardStat card in run.cards)
            {
                if (!sums.TryGetValue(card.card, out BotCardStat sum))
                {
                    sum = new BotCardStat { card = card.card, category = card.category, rarity = card.rarity, cost = card.cost };
                    sums[card.card] = sum;
                }

                sum.offered += card.offered;
                sum.picked += card.picked;
                sum.played += card.played;
                sum.scoreSum += card.scoreSum;
                sum.dmgDealt += card.dmgDealt;
            }
        }

        if (sums.Count == 0)
        {
            md.AppendLine("No cards recorded.");
            md.AppendLine();
            return;
        }

        List<BotCardStat> sorted = new(sums.Values);
        sorted.Sort((a, b) => b.played != a.played ? b.played.CompareTo(a.played) : string.CompareOrdinal(a.card, b.card));

        List<string[]> rows = new();
        List<string> neverPlayed = new();
        float nRuns = Mathf.Max(1, runs.Count);

        foreach (BotCardStat s in sorted)
        {
            string pickRate = s.offered > 0 ? Pct(s.picked, s.offered) : "-";

            rows.Add(new[]
            {
                s.card, s.category, s.rarity, BotText.I(s.cost), BotText.I(s.offered), pickRate, BotText.I(s.played),
                BotText.F(s.played > 0 ? s.scoreSum / s.played : 0f, 1), BotText.F(s.played > 0 ? (float)s.dmgDealt / s.played : 0f, 1),
            });

            csv.Add(new[]
            {
                s.card, s.category, s.rarity, BotText.I(s.cost), BotText.I(s.offered), BotText.I(s.picked),
                BotText.F(s.offered > 0 ? (float)s.picked / s.offered : 0f, 3), BotText.I(s.played), BotText.F(s.played / nRuns, 2),
                BotText.F(s.played > 0 ? s.scoreSum / s.played : 0f, 2), BotText.I(s.dmgDealt),
                BotText.F(s.played > 0 ? (float)s.dmgDealt / s.played : 0f, 2),
            });

            if (s.played == 0 && s.picked > 0) { neverPlayed.Add(s.card); }
        }

        Table(md, new[] { "Card", "Category", "Rarity", "Cost", "Offered", "Pick rate", "Played", "Avg score", "Dmg / play" }, rows);

        if (neverPlayed.Count > 0)
        {
            md.AppendLine($"Picked but never played: {string.Join(", ", neverPlayed)}.");
            md.AppendLine();
        }
    }

    private static void Rewards(StringBuilder md, List<BotRunRecord> runs)
    {
        md.AppendLine("## Rewards");
        md.AppendLine();

        Dictionary<string, int> mix = new();

        foreach (BotRunRecord run in runs)
        {
            foreach (BotRewardRecord reward in run.rewards)
            {
                string key = $"{reward.occasion}: {reward.kind}{(reward.kind == "skip" ? " - " + reward.choice : string.Empty)}";
                mix[key] = mix.TryGetValue(key, out int n) ? n + 1 : 1;
            }
        }

        if (mix.Count == 0)
        {
            md.AppendLine("No rewards offered.");
            md.AppendLine();
            return;
        }

        List<string> keys = new(mix.Keys);
        keys.Sort(string.CompareOrdinal);

        List<string[]> rows = new();

        foreach (string key in keys) { rows.Add(new[] { key, BotText.I(mix[key]) }); }

        Table(md, new[] { "Choice", "Times" }, rows);
    }

    private static void SharedSeeds(StringBuilder md, List<BotRunRecord> runs, List<string> profiles)
    {
        if (profiles.Count < 2) { return; }

        md.AppendLine("## Profiles compared on shared seeds");
        md.AppendLine();

        string reference = profiles.Contains("Balanced") ? "Balanced" : profiles[0];

        md.AppendLine($"Each run paired with the {reference} run on the same seed (same encounters level by level), "
                      + "compared by levels cleared, then by winning.");
        md.AppendLine();

        List<string[]> rows = new();

        foreach (string profile in profiles)
        {
            if (profile == reference) { continue; }

            int better = 0, equal = 0, worse = 0;
            float delta = 0f;
            int pairs = 0;

            foreach (BotRunRecord run in runs.FindAll(r => r.profile == profile))
            {
                BotRunRecord other = runs.Find(r => r.profile == reference && r.runIndex == run.runIndex);

                if (other == null) { continue; }

                int score = run.levelsCleared * 2 + (run.outcome == "Won" ? 1 : 0);
                int otherScore = other.levelsCleared * 2 + (other.outcome == "Won" ? 1 : 0);

                if (score > otherScore) { better++; }
                else if (score < otherScore) { worse++; }
                else { equal++; }

                delta += run.levelsCleared - other.levelsCleared;
                pairs++;
            }

            rows.Add(new[]
            {
                profile, BotText.I(pairs), BotText.I(better), BotText.I(equal), BotText.I(worse),
                (delta >= 0 ? "+" : string.Empty) + BotText.F(pairs > 0 ? delta / pairs : 0f, 2),
            });
        }

        Table(md, new[] { "Profile", $"Pairs vs {reference}", "Better", "Equal", "Worse", "Avg Δ levels cleared" }, rows);
    }

    private static void Behaviour(StringBuilder md, List<BotRunRecord> runs, List<string> profiles)
    {
        md.AppendLine("## Behaviour checks");
        md.AppendLine();
        md.AppendLine("Whether each profile plays the way it is meant to: its mix of plays by category, energy left on the "
                      + "table, and how often the safety nets fired.");
        md.AppendLine();

        string[] categories = Enum.GetNames(typeof(BotPlayCategory));
        List<string> headers = new() { "Profile" };
        headers.AddRange(categories);
        headers.AddRange(new[] { "Unspent energy / round", "Play-cap hits", "Refused plays", "Decisions / run" });

        List<string[]> rows = new();

        foreach (string profile in profiles)
        {
            List<BotRunRecord> mine = runs.FindAll(r => r.profile == profile);
            Dictionary<string, int> byCategory = new();
            int plays = 0, rounds = 0, unspent = 0, caps = 0, refused = 0, decisions = 0;

            foreach (BotRunRecord run in mine)
            {
                foreach (BotCardStat card in run.cards)
                {
                    byCategory[card.category] = (byCategory.TryGetValue(card.category, out int n) ? n : 0) + card.played;
                    plays += card.played;
                }

                foreach (BotLevelRecord level in run.levels) { rounds += level.rounds; }

                unspent += run.energyUnspent;
                caps += run.playCapHits;
                refused += run.plannerRefusals;
                decisions += run.decisions;
            }

            List<string> row = new() { profile };

            foreach (string category in categories)
            {
                row.Add(Pct(byCategory.TryGetValue(category, out int n) ? n : 0, plays));
            }

            row.Add(BotText.F(rounds > 0 ? (float)unspent / rounds : 0f, 2));
            row.Add(BotText.I(caps));
            row.Add(BotText.I(refused));
            row.Add(BotText.F(mine.Count > 0 ? (float)decisions / mine.Count : 0f, 0));
            rows.Add(row.ToArray());
        }

        Table(md, headers.ToArray(), rows);
    }

    private static void Performance(StringBuilder md, List<BotRunRecord> runs, List<string> profiles)
    {
        md.AppendLine("## Performance");
        md.AppendLine();

        List<string[]> rows = new();

        foreach (string profile in profiles)
        {
            List<BotRunRecord> mine = runs.FindAll(r => r.profile == profile);
            float worst = 0f;

            foreach (BotRunRecord run in mine) { worst = Mathf.Max(worst, run.plannerMsMax); }

            rows.Add(new[]
            {
                profile, BotText.F(Avg(mine, r => r.wallSeconds), 1), BotText.F(Avg(mine, r => r.frames), 0),
                BotText.F(Avg(mine, r => r.plannerMsAvg), 2), BotText.F(Avg(mine, r => r.plannerMsP95), 2), BotText.F(worst, 1),
            });
        }

        Table(md, new[] { "Profile", "Seconds / run", "Frames / run", "Planner ms avg", "Planner ms p95", "Planner ms max" }, rows);
    }

    private static void Errors(StringBuilder md, List<BotRunRecord> runs)
    {
        md.AppendLine("## Errors and stalls");
        md.AppendLine();

        Dictionary<string, (int count, List<string> runIds)> byMessage = new();

        foreach (BotRunRecord run in runs)
        {
            foreach (BotErrorRecord error in run.errorList)
            {
                string key = $"{error.type}: {error.message}";

                if (!byMessage.TryGetValue(key, out (int count, List<string> runIds) entry)) { entry = (0, new List<string>()); }

                entry.count += error.count;

                if (!entry.runIds.Contains(run.runId)) { entry.runIds.Add(run.runId); }

                byMessage[key] = entry;
            }
        }

        List<BotRunRecord> stalled = runs.FindAll(r => r.outcome != "Won" && r.outcome != "Died");

        Warnings(md, runs);

        if (byMessage.Count == 0 && stalled.Count == 0)
        {
            md.AppendLine("No errors, no stalls.");
            md.AppendLine();
            return;
        }

        if (byMessage.Count > 0)
        {
            List<string[]> rows = new();

            foreach (KeyValuePair<string, (int count, List<string> runIds)> entry in byMessage)
            {
                string shown = entry.Value.runIds.Count > 3
                    ? string.Join(", ", entry.Value.runIds.GetRange(0, 3)) + $" +{entry.Value.runIds.Count - 3} more"
                    : string.Join(", ", entry.Value.runIds);

                rows.Add(new[] { entry.Key, BotText.I(entry.Value.count), shown });
            }

            Table(md, new[] { "Error", "Count", "Runs" }, rows);
        }

        if (stalled.Count > 0)
        {
            List<string[]> rows = new();

            foreach (BotRunRecord run in stalled)
            {
                rows.Add(new[] { run.runId, run.outcome, $"L{run.endLevel} R{run.endRound}", run.outcomeNote ?? string.Empty });
            }

            Table(md, new[] { "Run", "Outcome", "Where", "Note" }, rows);
        }
    }

    /// The warnings the game logged most across the batch - not failures, but content worth a look.
    private static void Warnings(StringBuilder md, List<BotRunRecord> runs)
    {
        Dictionary<string, int> counts = new();
        Dictionary<string, int> inRuns = new();

        foreach (BotRunRecord run in runs)
        {
            foreach (BotWarningRecord warning in run.warningList)
            {
                counts[warning.message] = (counts.TryGetValue(warning.message, out int n) ? n : 0) + warning.count;
                inRuns[warning.message] = (inRuns.TryGetValue(warning.message, out int r) ? r : 0) + 1;
            }
        }

        if (counts.Count == 0) { return; }

        List<string> keys = new(counts.Keys);
        keys.Sort((a, b) => counts[b] != counts[a] ? counts[b].CompareTo(counts[a]) : string.CompareOrdinal(a, b));

        List<string[]> rows = new();

        for (int i = 0; i < keys.Count && i < 10; i++)
        {
            rows.Add(new[] { keys[i], BotText.I(counts[keys[i]]), BotText.I(inRuns[keys[i]]) });
        }

        md.AppendLine("Most frequent warnings (the game's own, not the bot's):");
        md.AppendLine();
        Table(md, new[] { "Warning", "Times", "Runs" }, rows);
    }

    // ---------------------------------------------------------------------------------------------
    // CSVs

    private static void WriteRunsCsv(string batchDir, List<BotRunRecord> runs)
    {
        string[] header =
        {
            "run_id", "profile", "baseline", "run_index", "seed", "party", "tier", "outcome", "levels_cleared", "end_level",
            "end_level_name", "end_round", "killers", "dmg_dealt", "dmg_taken", "healing", "shield_absorbed", "prevented",
            "falls", "heroes_lost", "cards_played", "energy_unspent", "decisions", "errors", "warnings", "planner_refusals",
            "play_cap_hits", "planner_ms_avg", "planner_ms_p95", "planner_ms_max", "wall_s", "frames", "digest",
        };

        List<string[]> rows = new();

        foreach (BotRunRecord r in runs)
        {
            rows.Add(new[]
            {
                r.runId, r.profile, r.baseline ? "1" : "0", BotText.I(r.runIndex), BotText.I(r.seed), r.partyLabel, BotText.I(r.tier),
                r.outcome, BotText.I(r.levelsCleared), BotText.I(r.endLevel), r.endLevelName, BotText.I(r.endRound),
                string.Join(";", r.killers), BotText.I(r.dmgDealt), BotText.I(r.dmgTaken), BotText.I(r.healing),
                BotText.I(r.shieldAbsorbed), BotText.I(r.prevented), BotText.I(r.falls), BotText.I(r.heroesLost),
                BotText.I(r.cardsPlayed), BotText.I(r.energyUnspent), BotText.I(r.decisions), BotText.I(r.errors),
                BotText.I(r.warnings), BotText.I(r.plannerRefusals), BotText.I(r.playCapHits), BotText.F(r.plannerMsAvg, 3),
                BotText.F(r.plannerMsP95, 3), BotText.F(r.plannerMsMax, 3), BotText.F(r.wallSeconds, 2), BotText.I(r.frames), r.digest,
            });
        }

        WriteCsv(Path.Combine(batchDir, "runs.csv"), header, rows);
    }

    private static void WriteLevelsCsv(string batchDir, List<BotRunRecord> runs)
    {
        string[] header =
        {
            "run_id", "profile", "baseline", "seed", "level", "level_name", "boss", "result", "rounds", "dmg_dealt", "dmg_taken",
            "healing", "shield_absorbed", "prevented", "falls", "enemies_opening", "enemies_waves", "enemies_summoned",
            "enemies_killed", "hp_start", "hp_max_start", "hp_end", "cards_played", "energy_unspent", "enemy_types",
        };

        List<string[]> rows = new();

        foreach (BotRunRecord r in runs)
        {
            foreach (BotLevelRecord l in r.levels)
            {
                rows.Add(new[]
                {
                    r.runId, r.profile, r.baseline ? "1" : "0", BotText.I(r.seed), BotText.I(l.level), l.name, l.boss ? "1" : "0",
                    l.result, BotText.I(l.rounds), BotText.I(l.dmgDealt), BotText.I(l.dmgTaken), BotText.I(l.healing),
                    BotText.I(l.shieldAbsorbed), BotText.I(l.prevented), BotText.I(l.falls), BotText.I(l.enemiesOpening),
                    BotText.I(l.enemiesWaves), BotText.I(l.enemiesSummoned), BotText.I(l.enemiesKilled), BotText.I(l.hpStart),
                    BotText.I(l.hpMaxStart), BotText.I(l.hpEnd), BotText.I(l.cardsPlayed), BotText.I(l.energyUnspent),
                    string.Join(";", l.enemyTypes),
                });
            }
        }

        WriteCsv(Path.Combine(batchDir, "levels.csv"), header, rows);
    }

    private static void WriteRewardsCsv(string batchDir, List<BotRunRecord> runs)
    {
        string[] header = { "run_id", "profile", "level", "round", "hero", "occasion", "kind", "choice", "offered" };
        List<string[]> rows = new();

        foreach (BotRunRecord r in runs)
        {
            foreach (BotRewardRecord w in r.rewards)
            {
                rows.Add(new[] { r.runId, r.profile, BotText.I(w.level), BotText.I(w.round), w.hero, w.occasion, w.kind, w.choice, w.offered });
            }
        }

        WriteCsv(Path.Combine(batchDir, "rewards.csv"), header, rows);
    }

    private static void WriteErrorsCsv(string batchDir, List<BotRunRecord> runs)
    {
        string[] header = { "run_id", "profile", "level", "round", "type", "message", "count", "first_stack" };
        List<string[]> rows = new();

        foreach (BotRunRecord r in runs)
        {
            foreach (BotErrorRecord e in r.errorList)
            {
                string stack = e.stack ?? string.Empty;

                if (stack.Length > 500) { stack = stack.Substring(0, 500); }

                rows.Add(new[] { r.runId, r.profile, BotText.I(e.level), BotText.I(e.round), e.type, e.message, BotText.I(e.count), stack });
            }
        }

        WriteCsv(Path.Combine(batchDir, "errors.csv"), header, rows);
    }

    private static void WriteCsv(string path, string[] header, List<string[]> rows)
    {
        StringBuilder sb = new();

        sb.AppendLine(string.Join(",", header));

        foreach (string[] row in rows)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (i > 0) { sb.Append(','); }

                sb.Append(BotText.Csv(row[i]));
            }

            sb.AppendLine();
        }

        BotText.WriteAtomic(path, sb.ToString(), BotText.Utf8Bom);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers

    private static SortedDictionary<int, List<BotLevelRecord>> ByLevel(List<BotRunRecord> runs)
    {
        SortedDictionary<int, List<BotLevelRecord>> byLevel = new();

        foreach (BotRunRecord run in runs)
        {
            foreach (BotLevelRecord level in run.levels)
            {
                if (!byLevel.TryGetValue(level.level, out List<BotLevelRecord> list))
                {
                    list = new List<BotLevelRecord>();
                    byLevel[level.level] = list;
                }

                list.Add(level);
            }
        }

        return byLevel;
    }

    private static void Table(StringBuilder md, string[] headers, List<string[]> rows)
    {
        if (rows.Count == 0)
        {
            md.AppendLine("Nothing to show.");
            md.AppendLine();
            return;
        }

        md.Append('|');

        foreach (string header in headers) { md.Append(' ').Append(Cell(header)).Append(" |"); }

        md.AppendLine();
        md.Append('|');

        for (int i = 0; i < headers.Length; i++) { md.Append(i == 0 ? "---|" : "--:|"); }

        md.AppendLine();

        foreach (string[] row in rows)
        {
            md.Append('|');

            foreach (string cell in row) { md.Append(' ').Append(Cell(cell)).Append(" |"); }

            md.AppendLine();
        }

        md.AppendLine();
    }

    private static string Cell(string text) => (text ?? string.Empty).Replace("|", "\\|").Replace("\n", " ");

    private static string Pct(float part, float whole) => whole > 0 ? BotText.F(part / whole * 100f, 0) + "%" : "-";

    private static string Pct(int part, int whole) => Pct((float)part, whole);

    private static float Avg(List<BotRunRecord> runs, Func<BotRunRecord, float> value)
    {
        if (runs.Count == 0) { return 0f; }

        float sum = 0f;

        foreach (BotRunRecord run in runs) { sum += value(run); }

        return sum / runs.Count;
    }

    private static float AvgL(List<BotLevelRecord> levels, Func<BotLevelRecord, float> value)
    {
        if (levels.Count == 0) { return 0f; }

        float sum = 0f;

        foreach (BotLevelRecord level in levels) { sum += value(level); }

        return sum / levels.Count;
    }

    private static string TopKeys(Dictionary<string, int> counts, int max)
    {
        List<string> keys = new(counts.Keys);
        keys.Sort((a, b) => counts[b] != counts[a] ? counts[b].CompareTo(counts[a]) : string.CompareOrdinal(a, b));

        List<string> shown = new();

        for (int i = 0; i < keys.Count && i < max; i++) { shown.Add($"{keys[i]} ({counts[keys[i]]})"); }

        return shown.Count > 0 ? string.Join(", ", shown) : "-";
    }

    /// Wilson score interval for a win rate - honest at small sample sizes, unlike the ± of a normal approximation.
    private static (float low, float high) Wilson(int wins, int n)
    {
        if (n <= 0) { return (0f, 0f); }

        const float z = 1.96f;
        float p = (float)wins / n;
        float denominator = 1f + z * z / n;
        float centre = (p + z * z / (2f * n)) / denominator;
        float margin = z * Mathf.Sqrt(p * (1f - p) / n + z * z / (4f * n * n)) / denominator;

        return (Mathf.Clamp01(centre - margin), Mathf.Clamp01(centre + margin));
    }
}
#endif
