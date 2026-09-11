using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Rolls a level's random encounter thousands of times and reports what comes out: example battles,
/// how often each enemy appears, the most and least common openings and waves, pacing, and a set of
/// checks for budgets that cannot do what they look like they do. The engine behind
/// EncounterSimulatorWindow - kept apart from it so the window is only fields and buttons.
///
/// **Every battle goes through EncounterRoller.RollFor, the exact call BattleManager.RollEncounter
/// makes.** Nothing here re-implements a rule of the roller, so a change to how encounters are drawn
/// shows up in the simulator with no edit to this file, and what it reports is what the game rolls.
///
/// Everything is read fresh on every run: the level's EncounterBudget, its hand-placed enemies and
/// waves, and each enemy prefab's Power Level, role and health. Retune a number, run again.
///
/// Battle `i` is seeded with `seed + i`, so a run is reproducible: keep the seed, change one number on
/// the level, and any difference in the report is that change rather than luck.
/// </summary>
public static class EncounterSimulation
{
    public struct Settings
    {
        public LevelData level;
        public EnemyRegistry registry;

        /// Null runs at Normal - the same thing a campaign with no ladder does.
        public DifficultyLadder ladder;
        public int tierIndex;

        public int battles;
        public int examples;
        public int seed;
    }

    /// One foldout's worth of report. The window draws each on its own; ToText joins them.
    public sealed class Section
    {
        public readonly string title;
        public readonly string body;

        public Section(string title, string body)
        {
            this.title = title;
            this.body = body;
        }
    }

    public sealed class Report
    {
        public readonly List<Section> sections = new();

        public string ToText()
        {
            StringBuilder text = new();

            foreach (Section section in sections)
            {
                text.AppendLine("== " + section.title.ToUpperInvariant() + " ==");
                text.AppendLine(section.body.TrimEnd());
                text.AppendLine();
            }

            return text.ToString();
        }
    }

    /// Stand-in board size for a level authored at 0,0 ("use GridManager's default"). Only moves which
    /// cells enemies are placed on, never what is rolled, so it cannot change anything this reports.
    private static readonly Vector2Int FallbackBoardSize = new(6, 6);

    private const int CommonShown = 10;
    private const int RareShown = 10;

    // ---- One battle ----------------------------------------------------------------------------

    /// What one battle puts in front of the player: hand-placed plus rolled opening, and every wave
    /// merged by the turn it lands on - BattleManager.SpawnDueWaves spawns every wave matching a turn at
    /// once, so two waves sharing a turn are one arrival to the player.
    private sealed class Battle
    {
        public readonly List<Character> opening = new();
        public readonly SortedDictionary<int, List<Character>> arriving = new();
        public readonly List<(int turn, List<Character> bodies)> late = new();

        public float rolledOpeningPower;
        public int frontShort;
        public int backShort;

        public IEnumerable<Character> Faced => opening.Concat(arriving.Values.SelectMany(wave => wave));
    }

    // ---- Accumulators --------------------------------------------------------------------------

    /// Running min/avg/max without keeping every sample.
    private sealed class Stat
    {
        private double sum;

        public double Min { get; private set; } = double.MaxValue;
        public double Max { get; private set; } = double.MinValue;
        public int Count { get; private set; }
        public double Avg => Count > 0 ? sum / Count : 0;

        public void Add(double value)
        {
            sum += value;
            Count++;

            if (value < Min) { Min = value; }
            if (value > Max) { Max = value; }
        }

        public string Describe(string format = "0.0") =>
            Count == 0 ? "-" : $"min {Min.ToString(format)}   avg {Avg.ToString(format)}   max {Max.ToString(format)}";
    }

    private sealed class Tally
    {
        public int opening;
        public int waves;
        public int late;
        public int battles;

        public int Total => opening + waves;
    }

    private sealed class Totals
    {
        public readonly Stat rolledOpeningPower = new();
        public readonly Stat openingCount = new();
        public readonly Stat openingRepeats = new();
        public readonly Stat openingHp = new();
        public readonly Stat openingFront = new();
        public readonly Stat openingBack = new();
        public readonly Stat openingEither = new();
        public readonly Dictionary<int, int> openingSizes = new();
        public int openingOnBudget;
        public int metMinimums;

        public readonly Stat arrivingWaves = new();
        public readonly Stat waveSize = new();
        public readonly Stat waveRepeats = new();
        public readonly Stat waveEnemies = new();
        public readonly Stat wavePower = new();
        public readonly Stat lateWaves = new();
        public readonly Stat latePower = new();

        public readonly Stat facedCount = new();
        public readonly Stat facedTypes = new();
        public readonly Stat facedTop = new();
        public readonly Stat facedPower = new();
        public readonly Stat facedHp = new();

        /// Index 0 is the opening; 1..turns are the turns waves can land on.
        public double[] powerByTurn;
        public int[] waveOnTurn;

        public readonly Dictionary<Character, Tally> enemies = new();
        public readonly Dictionary<string, int> openings = new();
        public readonly Dictionary<string, int> waves = new();
        public int waveTotal;

        public Tally TallyOf(Character body)
        {
            if (!enemies.TryGetValue(body, out Tally tally))
            {
                tally = new Tally();
                enemies[body] = tally;
            }

            return tally;
        }
    }

    // ---- Pool analysis -------------------------------------------------------------------------

    private sealed class PoolRow
    {
        public string name;
        public Character body;
        public bool drawable;

        /// "ok", or the plain-language reason this entry is never rolled.
        public string status;
    }

    // ---- Entry point ---------------------------------------------------------------------------

    /// <summary>
    /// Runs the simulation. `cancelRequested` is polled with progress 0..1 every hundred battles and
    /// returns true to stop; a cancelled run returns null rather than a report of a partial sample,
    /// which would read as authoritative when it is not.
    /// </summary>
    public static Report Run(Settings settings, System.Func<float, bool> cancelRequested)
    {
        LevelData level = settings.level;
        int battles = Mathf.Max(1, settings.battles);
        int turns = Mathf.Max(1, level.TurnsToSurvive);

        DifficultyTier tier = settings.ladder != null ? settings.ladder.TierAt(settings.tierIndex) : default;
        string tierName = settings.ladder != null ? settings.ladder.NameAt(settings.tierIndex) : "Normal";

        // The budget as the roller will actually spend it, after the tier's scaling - RollFor applies
        // the same Scale to the same authored struct.
        EncounterBudget budget = tier.Scale(level.EncounterBudget);

        Vector2Int board = level.BoardSize.x > 0 && level.BoardSize.y > 0 ? level.BoardSize : FallbackBoardSize;

        // One GetComponent per prefab per run rather than per placement. Rebuilt every run, so an enemy
        // retuned since the last run is read fresh.
        Dictionary<GameObject, Character> bodyCache = new();
        Character BodyOf(GameObject prefab)
        {
            if (prefab == null) { return null; }

            if (!bodyCache.TryGetValue(prefab, out Character body))
            {
                body = prefab.GetComponent<Character>();
                bodyCache[prefab] = body;
            }

            return body;
        }

        List<PoolRow> pool = AnalysePool(settings.registry, budget, BodyOf, out int hiddenIneligible);

        Totals totals = new() { powerByTurn = new double[turns + 1], waveOnTurn = new int[turns + 1] };
        List<Battle> examples = new();

        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < battles; i++)
        {
            if (i % 100 == 0 && cancelRequested != null && cancelRequested((float)i / battles)) { return null; }

            RolledEncounter rolled =
                EncounterRoller.RollFor(level, settings.registry, tier, board, new System.Random(settings.seed + i));

            Battle battle = Assemble(level, rolled, turns, BodyOf);

            Accumulate(totals, battle, budget, tier, turns);

            if (i < settings.examples) { examples.Add(battle); }
        }

        clock.Stop();

        float openingBudget = OpeningBudget(budget);

        Report report = new();

        report.sections.Add(new Section("Overview",
            Overview(level, tierName, tier, battles, settings.seed, turns, clock.Elapsed.TotalSeconds)));
        report.sections.Add(new Section("Budget", BudgetText(level.EncounterBudget, budget, tier, turns)));
        report.sections.Add(new Section("Pool", PoolText(pool, budget, tier, hiddenIneligible)));
        report.sections.Add(new Section("Checks", ChecksText(level, budget, pool, totals, battles, turns)));

        if (examples.Count > 0)
        {
            report.sections.Add(new Section($"{examples.Count} example battles", ExamplesText(examples, tier)));
        }

        report.sections.Add(new Section($"Opening - over {battles} battles",
            OpeningText(level, totals, battles, openingBudget, budget, tierName)));
        report.sections.Add(new Section($"Waves - over {battles} battles", WavesText(totals, battles, budget, turns)));
        report.sections.Add(new Section("Whole battle - opening plus every wave that arrives",
            WholeBattleText(totals, pool, tierName)));
        report.sections.Add(new Section("Pacing - average enemy power arriving each turn",
            PacingText(totals, battles, turns)));
        report.sections.Add(new Section($"Enemy frequency - across {battles} battles",
            FrequencyText(totals, pool, battles, tier)));
        report.sections.Add(new Section("Most common openings", CommonText(totals.openings, battles, CommonShown, "battles")));
        report.sections.Add(new Section("Rarest openings", RareText(totals.openings, battles, RareShown, "openings")));
        report.sections.Add(new Section("Most common waves", CommonText(totals.waves, totals.waveTotal, CommonShown, "waves")));
        report.sections.Add(new Section("Rarest waves", RareText(totals.waves, totals.waveTotal, RareShown, "waves")));

        return report;
    }

    // ---- Simulation ----------------------------------------------------------------------------

    private static Battle Assemble(LevelData level, RolledEncounter rolled, int turns,
        System.Func<GameObject, Character> bodyOf)
    {
        Battle battle = new()
        {
            frontShort = rolled.frontlineShortfall,
            backShort = rolled.backlineShortfall,
        };

        // Hand-placed enemies stand in every battle alongside the rolled lineup - BattleManager.
        // SpawnEnemies spawns CurrentLevel.Enemies and then the rolled starting enemies.
        AddBodies(battle.opening, level.Enemies, bodyOf);

        int handPlaced = battle.opening.Count;

        AddBodies(battle.opening, rolled.opening, bodyOf);

        for (int i = handPlaced; i < battle.opening.Count; i++)
        {
            battle.rolledOpeningPower += battle.opening[i].PowerLevel;
        }

        // bossCount's bosses sit on top of the power budgets rather than coming out of them, so they
        // come off before the opening is compared with its budget - otherwise every boss level would
        // read as overspent by exactly one boss.
        battle.rolledOpeningPower -= rolled.bossCountPower;

        foreach (EnemyWave wave in level.Waves.Concat(rolled.waves))
        {
            List<Character> bodies = new();
            AddBodies(bodies, wave.enemies, bodyOf);

            if (bodies.Count == 0) { continue; }

            // TurnsElapsed runs 1..TurnsToSurvive, and SpawnDueWaves only ever spawns a wave whose turn
            // it reaches - anything outside that range is rolled but never seen.
            if (wave.turn >= 1 && wave.turn <= turns)
            {
                if (!battle.arriving.TryGetValue(wave.turn, out List<Character> landing))
                {
                    landing = new List<Character>();
                    battle.arriving[wave.turn] = landing;
                }

                landing.AddRange(bodies);
            }
            else
            {
                battle.late.Add((wave.turn, bodies));
            }
        }

        return battle;
    }

    private static void AddBodies(List<Character> into, IEnumerable<EnemyPlacement> placements,
        System.Func<GameObject, Character> bodyOf)
    {
        if (placements == null) { return; }

        foreach (EnemyPlacement placement in placements)
        {
            Character body = bodyOf(placement.prefab);

            if (body != null) { into.Add(body); }
        }
    }

    private static void Accumulate(Totals totals, Battle battle, EncounterBudget budget, DifficultyTier tier,
        int turns)
    {
        // Opening.
        totals.rolledOpeningPower.Add(battle.rolledOpeningPower);
        totals.openingCount.Add(battle.opening.Count);
        totals.openingRepeats.Add(Repeats(battle.opening));
        totals.openingHp.Add(battle.opening.Sum(body => Hp(body, tier)));
        totals.openingFront.Add(battle.opening.Count(body => body.BattleRole == BattleRole.Frontline));
        totals.openingBack.Add(battle.opening.Count(body => body.BattleRole == BattleRole.Backline));
        totals.openingEither.Add(battle.opening.Count(body =>
            body.BattleRole == BattleRole.None || body.BattleRole == BattleRole.Both));

        totals.openingSizes[battle.opening.Count] =
            totals.openingSizes.TryGetValue(battle.opening.Count, out int sized) ? sized + 1 : 1;

        if (Mathf.Abs(OpeningBudget(budget) - battle.rolledOpeningPower) < 0.01f) { totals.openingOnBudget++; }
        if (battle.frontShort == 0 && battle.backShort == 0) { totals.metMinimums++; }

        string openingKey = Key(battle.opening);
        totals.openings[openingKey] = totals.openings.TryGetValue(openingKey, out int seen) ? seen + 1 : 1;

        totals.powerByTurn[0] += battle.opening.Sum(body => body.PowerLevel);

        foreach (Character body in battle.opening) { totals.TallyOf(body).opening++; }

        // Waves that arrive.
        totals.arrivingWaves.Add(battle.arriving.Count);

        int waveEnemies = 0;
        float wavePower = 0f;

        foreach (KeyValuePair<int, List<Character>> wave in battle.arriving)
        {
            totals.waveSize.Add(wave.Value.Count);
            totals.waveRepeats.Add(Repeats(wave.Value));
            totals.waveTotal++;
            totals.waveOnTurn[wave.Key]++;

            float power = wave.Value.Sum(body => body.PowerLevel);
            totals.powerByTurn[wave.Key] += power;

            waveEnemies += wave.Value.Count;
            wavePower += power;

            string waveKey = Key(wave.Value, " + ");
            totals.waves[waveKey] = totals.waves.TryGetValue(waveKey, out int waveSeen) ? waveSeen + 1 : 1;

            foreach (Character body in wave.Value) { totals.TallyOf(body).waves++; }
        }

        totals.waveEnemies.Add(waveEnemies);
        totals.wavePower.Add(wavePower);

        // Waves rolled but scheduled past the last turn.
        totals.lateWaves.Add(battle.late.Count);
        totals.latePower.Add(battle.late.Sum(wave => wave.bodies.Sum(body => body.PowerLevel)));

        foreach ((int turn, List<Character> bodies) wave in battle.late)
        {
            foreach (Character body in wave.bodies) { totals.TallyOf(body).late++; }
        }

        // The whole battle as the player meets it.
        List<Character> faced = battle.Faced.ToList();

        totals.facedCount.Add(faced.Count);
        totals.facedTypes.Add(faced.Select(body => body.name).Distinct().Count());
        totals.facedTop.Add(faced.Count > 0 ? faced.GroupBy(body => body.name).Max(group => group.Count()) : 0);
        totals.facedPower.Add(faced.Sum(body => body.PowerLevel));
        totals.facedHp.Add(faced.Sum(body => Hp(body, tier)));

        foreach (Character body in faced.Distinct()) { totals.TallyOf(body).battles++; }
    }

    // ---- Helpers -------------------------------------------------------------------------------

    /// Health as it will actually be at this tier - BattleManager.ApplyDifficulty adds the same bonus
    /// to every hostile body as it joins.
    private static int Hp(Character body, DifficultyTier tier) =>
        body.MaxHealth + (body.IsHostileToParty ? tier.ExtraHealthFor(body.MaxHealth) : 0);

    private static float OpeningBudget(EncounterBudget budget) =>
        budget.frontlinePower + budget.backlinePower + budget.anyPower
        + budget.bossFrontlinePower + budget.bossBacklinePower;

    private static int Repeats(List<Character> bodies) =>
        bodies.Count - bodies.Select(body => body.name).Distinct().Count();

    /// Order-independent name for a set of bodies - "Bat x2, Goblin" and "Goblin, Bat x2" are the same
    /// opening, so they have to be the same key.
    private static string Key(IEnumerable<Character> bodies, string separator = ", ")
    {
        List<string> parts = bodies
            .GroupBy(body => body.name)
            .OrderBy(group => group.Key)
            .Select(group => group.Count() > 1 ? $"{group.Key} x{group.Count()}" : group.Key)
            .ToList();

        return parts.Count > 0 ? string.Join(separator, parts) : "(nothing)";
    }

    private static string RoleName(BattleRole role) => role switch
    {
        BattleRole.Frontline => "front",
        BattleRole.Backline => "back",
        BattleRole.Both => "both",
        _ => "untagged",
    };

    private static string Pct(double part, double whole) => whole > 0 ? $"{100.0 * part / whole:0.#}%" : "-";

    /// Left-aligned text table with right-aligned numeric columns, two spaces between columns.
    private static string Table(List<string[]> rows, params bool[] rightAlign)
    {
        if (rows.Count == 0) { return ""; }

        int columns = rows.Max(row => row.Length);
        int[] widths = new int[columns];

        foreach (string[] row in rows)
        {
            for (int c = 0; c < row.Length; c++) { widths[c] = Mathf.Max(widths[c], row[c]?.Length ?? 0); }
        }

        StringBuilder text = new();

        foreach (string[] row in rows)
        {
            for (int c = 0; c < row.Length; c++)
            {
                string cell = row[c] ?? "";
                bool right = c < rightAlign.Length && rightAlign[c];

                text.Append(right ? cell.PadLeft(widths[c]) : cell.PadRight(widths[c]));

                if (c < row.Length - 1) { text.Append("  "); }
            }

            text.AppendLine();
        }

        // Padding the last column leaves trailing spaces on every short row - harmless on screen, but
        // noise in a copied report.
        return string.Join("\n", text.ToString().TrimEnd().Split('\n').Select(line => line.TrimEnd())) + "\n";
    }

    // ---- Pool ----------------------------------------------------------------------------------

    /// <summary>
    /// Every enemy the level could name - its pool filter, or the whole registry when the filter is
    /// empty - and for each one either "ok" or the reason the roller will never draw it. The reasons
    /// mirror the checks EncounterRoller's constructor and PickBody make, stated in words.
    /// </summary>
    private static List<PoolRow> AnalysePool(EnemyRegistry registry, EncounterBudget budget,
        System.Func<GameObject, Character> bodyOf, out int hiddenIneligible)
    {
        hiddenIneligible = 0;

        Dictionary<GameObject, EnemyRegistryEntry> registered = new();

        if (registry != null)
        {
            foreach (EnemyRegistryEntry entry in registry.Entries)
            {
                if (entry.prefab != null) { registered[entry.prefab] = entry; }
            }
        }

        List<GameObject> candidates = new();
        bool filtered = budget.poolFilter != null && budget.poolFilter.Count > 0;

        if (filtered)
        {
            foreach (GameObject prefab in budget.poolFilter)
            {
                // `== null` on purpose - a deleted prefab is a Unity fake-null. See CLAUDE.md.
                if (prefab == null || !candidates.Contains(prefab)) { candidates.Add(prefab); }
            }
        }
        else
        {
            foreach (EnemyRegistryEntry entry in registered.Values)
            {
                if (entry.eligibleForRandomDraw) { candidates.Add(entry.prefab); }
                else { hiddenIneligible++; }
            }
        }

        bool bossBudget = budget.bossFrontlinePower + budget.bossBacklinePower > 0f || budget.bossCount > 0;
        bool regularBudget = budget.frontlinePower + budget.backlinePower + budget.anyPower
                             + budget.reinforcementPower > 0f;

        List<PoolRow> rows = new();

        foreach (GameObject prefab in candidates)
        {
            PoolRow row = new() { name = prefab != null ? prefab.name : "(missing prefab)" };
            rows.Add(row);

            if (prefab == null) { row.status = "missing - the prefab was deleted"; continue; }

            if (!registered.TryGetValue(prefab, out EnemyRegistryEntry entry))
            {
                row.status = "not in the EnemyRegistry - run Tools > Enemies > Rebuild Enemy Registry";
                continue;
            }

            row.body = bodyOf(prefab);

            if (!entry.eligibleForRandomDraw) { row.status = "registry marks it not eligible for random draw"; continue; }
            if (row.body == null) { row.status = "no Character component"; continue; }
            if (row.body.PowerLevel <= 0f) { row.status = "unscored (Power Level 0) - never drawn"; continue; }

            if (row.body.PowerLevel > budget.maxPowerPerEnemy)
            {
                row.status = $"over the per-enemy cap ({row.body.PowerLevel:0.##} > {budget.maxPowerPerEnemy:0.##}) - never drawn";
                continue;
            }

            if (row.body.IsBoss && !bossBudget) { row.status = "boss - this level has no boss budget"; continue; }
            if (!row.body.IsBoss && !regularBudget) { row.status = "no non-boss budget on this level"; continue; }

            row.drawable = true;
            row.status = "ok";
        }

        return rows;
    }

    // ---- Sections ------------------------------------------------------------------------------

    private static string Overview(LevelData level, string tierName, DifficultyTier tier, int battles, int seed,
        int turns, double seconds)
    {
        StringBuilder text = new();

        text.AppendLine($"Level        {level.name}");
        text.AppendLine($"Difficulty   {tierName}  (encounter power x{1f + tier.extraEncounterPower:0.00}, "
                        + $"enemy health x{1f + tier.extraEnemyHealth:0.00})");
        text.AppendLine($"Battles      {battles}   seed {seed}   {turns} turns to survive   ran in {seconds:0.00}s");

        if (level.Enemies.Count > 0 || level.Waves.Count > 0)
        {
            text.AppendLine($"Hand-placed  {level.Enemies.Count} enemies and {level.Waves.Count} authored waves are "
                            + "included in every battle below.");
        }

        text.AppendLine();
        text.AppendLine("Rolled through EncounterRoller.RollFor - the same call a real battle makes. The level");
        text.AppendLine("and every enemy's Power Level are read fresh each run: edit, then Simulate again.");
        text.AppendLine("Keep the seed to compare an edit like-for-like.");

        return text.ToString();
    }

    private static string BudgetText(EncounterBudget authored, EncounterBudget scaled, DifficultyTier tier, int turns)
    {
        StringBuilder text = new();
        bool isScaled = tier.extraEncounterPower != 0f;

        string Power(float authoredValue, float scaledValue) =>
            isScaled && authoredValue > 0f ? $"{scaledValue:0.##} (authored {authoredValue:0.##})" : $"{scaledValue:0.##}";

        text.AppendLine($"Opening      anyPower {Power(authored.anyPower, scaled.anyPower)}   "
                        + $"frontline {Power(authored.frontlinePower, scaled.frontlinePower)}   "
                        + $"backline {Power(authored.backlinePower, scaled.backlinePower)}");
        text.AppendLine($"             boss front {Power(authored.bossFrontlinePower, scaled.bossFrontlinePower)}   "
                        + $"boss back {Power(authored.bossBacklinePower, scaled.bossBacklinePower)}   "
                        + $"= {OpeningBudget(scaled):0.##} total");

        if (scaled.bossCount > 0)
        {
            text.AppendLine($"Bosses       {scaled.bossCount} random, either side - on top of the budget above");
        }

        text.AppendLine($"Per enemy    cap {scaled.maxPowerPerEnemy:0.##}");
        text.AppendLine($"Headcount    frontline {CountText(scaled.frontlineCount)}   backline {CountText(scaled.backlineCount)}");
        text.AppendLine($"Scoring      rollAttempts {(scaled.rollAttempts > 0 ? scaled.rollAttempts : 1)}   "
                        + $"duplicatePenalty {Mathf.Max(0f, scaled.duplicatePenalty):0.##}");
        text.AppendLine($"Waves        reinforcement {Power(authored.reinforcementPower, scaled.reinforcementPower)}   "
                        + $"max {scaled.maxPowerPerWave:0.##} per wave   every {scaled.waveInterval} turn(s) "
                        + $"+0..{scaled.waveIntervalJitter} jitter   over {turns} turns");

        if (scaled.poolFilter == null || scaled.poolFilter.Count == 0)
        {
            text.AppendLine("Pool         no pool filter - draws from the whole EnemyRegistry");
        }

        return text.ToString();
    }

    private static string CountText(RoleCount count)
    {
        string min = count.min > 0 ? count.min.ToString() : "-";
        string max = count.max > 0 ? count.max.ToString() : "-";

        return count.min <= 0 && count.max <= 0 ? "unlimited" : $"min {min} / max {max}";
    }

    private static string PoolText(List<PoolRow> pool, EncounterBudget budget, DifficultyTier tier, int hiddenIneligible)
    {
        List<string[]> rows = new() { new[] { "Enemy", "Power", "Role", "Boss", "HP", "Status" } };

        foreach (PoolRow row in pool.OrderBy(r => r.body != null ? r.body.PowerLevel : 0f).ThenBy(r => r.name))
        {
            rows.Add(new[]
            {
                row.name,
                row.body != null ? row.body.PowerLevel.ToString("0.##") : "-",
                row.body != null ? RoleName(row.body.BattleRole) : "-",
                row.body != null && row.body.IsBoss ? "boss" : "",
                row.body != null ? Hp(row.body, tier).ToString() : "-",
                row.status,
            });
        }

        StringBuilder text = new(Table(rows, false, true, false, false, true, false));

        List<Character> drawable = pool.Where(r => r.drawable && !r.body.IsBoss).Select(r => r.body).ToList();

        text.AppendLine();
        text.AppendLine($"Drawable     {drawable.Count} regular enemies "
                        + $"({drawable.Count(b => EncounterRoller.FitsSide(b, front: true))} can stand front, "
                        + $"{drawable.Count(b => EncounterRoller.FitsSide(b, front: false))} can stand back), "
                        + $"{pool.Count(r => r.drawable && r.body.IsBoss)} bosses");

        if (drawable.Count > 0)
        {
            text.AppendLine($"No repeats   every drawable regular enemy once comes to {drawable.Sum(b => b.PowerLevel):0.##} power");
        }

        if (hiddenIneligible > 0)
        {
            text.AppendLine($"Hidden       {hiddenIneligible} registry entries not eligible for random draw (allies)");
        }

        return text.ToString();
    }

    /// <summary>
    /// Plain-language warnings for budgets that cannot do what they look like they do - the things that
    /// took a simulation to notice by hand: a cap that rules out half the pool, an opening budget no
    /// headcount can reach, a duplicatePenalty too small to ever trim, waves scheduled after the fight.
    /// </summary>
    private static string ChecksText(LevelData level, EncounterBudget budget, List<PoolRow> pool, Totals totals,
        int battles, int turns)
    {
        List<string> warnings = new();
        List<string> notes = new();

        List<Character> regular = pool.Where(r => r.drawable && !r.body.IsBoss).Select(r => r.body).ToList();
        float openingBudget = OpeningBudget(budget);
        float regularOpening = budget.anyPower + budget.frontlinePower + budget.backlinePower;

        if (openingBudget <= 0f && budget.reinforcementPower <= 0f && budget.bossCount <= 0)
        {
            warnings.Add(level.Enemies.Count > 0 || level.Waves.Count > 0
                ? "Every Encounter Budget power field is 0 - only the hand-placed enemies and waves spawn."
                : "Every Encounter Budget power field is 0 - this level rolls nothing at all.");
        }

        if (budget.maxPowerPerEnemy <= 0f && (openingBudget > 0f || budget.reinforcementPower > 0f))
        {
            warnings.Add("maxPowerPerEnemy is 0, so no enemy is affordable and nothing is rolled.");
        }

        int neverDrawn = pool.Count(r => !r.drawable);
        if (neverDrawn > 0)
        {
            warnings.Add($"{neverDrawn} pool entr{(neverDrawn == 1 ? "y is" : "ies are")} never drawn - "
                         + $"{string.Join(", ", pool.Where(r => !r.drawable).Select(r => r.name))}. See Pool for why.");
        }

        float noRepeatMax = regular.Sum(b => b.PowerLevel);
        if (regularOpening > noRepeatMax + 0.01f && regular.Count > 0)
        {
            warnings.Add($"The opening budget ({regularOpening:0.##}) is more than every drawable enemy once "
                         + $"({noRepeatMax:0.##}), so openings must repeat enemies to spend it.");
        }

        if (budget.frontlineCount.max > 0 && budget.backlineCount.max > 0 && regular.Count > 0)
        {
            (float withRepeats, float withoutRepeats) =
                MaxCappedSpend(regular, budget.frontlineCount.max, budget.backlineCount.max);

            if (regularOpening > withRepeats + 0.01f)
            {
                warnings.Add($"Within the headcount caps (front {budget.frontlineCount.max}, back "
                             + $"{budget.backlineCount.max}) an opening can spend at most about {withRepeats:0.##} "
                             + $"({withoutRepeats:0.##} with no repeats), so the {regularOpening:0.##} budget will "
                             + "under-spend. Lower it or raise the caps. A boss also takes a slot on its side.");
            }
            else if (regularOpening > withoutRepeats + 0.01f)
            {
                notes.Add($"Within the headcount caps, spending the full {regularOpening:0.##} needs repeats - "
                          + $"a no-repeat opening tops out around {withoutRepeats:0.##}.");
            }
        }

        foreach (bool front in new[] { true, false })
        {
            RoleCount count = front ? budget.frontlineCount : budget.backlineCount;
            string side = front ? "frontline" : "backline";

            if (count.max > 0 && count.min > count.max)
            {
                warnings.Add($"The {side} minimum ({count.min}) is above its maximum ({count.max}) - the maximum wins.");
            }

            if (count.min > 0 && !regular.Any(b => EncounterRoller.FitsSide(b, front)))
            {
                warnings.Add($"A {side} minimum of {count.min} is set, but no drawable enemy can stand {side}.");
            }
        }

        bool minimumsSet = budget.frontlineCount.min > 0 || budget.backlineCount.min > 0;
        if (minimumsSet && totals.metMinimums < battles)
        {
            warnings.Add($"Role minimums were missed in {Pct(battles - totals.metMinimums, battles)} of battles "
                         + "(a real battle logs a warning when this happens).");
        }

        float cheapest = regular.Count > 0 ? regular.Min(b => b.PowerLevel) : 0f;
        float penalty = Mathf.Max(0f, budget.duplicatePenalty);

        if (penalty <= 0f)
        {
            notes.Add("duplicatePenalty is 0: repeats are exactly as likely as any other enemy on every pick. "
                      + "Raise it to lean the draw toward variety.");
        }
        else if (cheapest > 0f && penalty < cheapest)
        {
            notes.Add($"duplicatePenalty {penalty:0.##} is below the cheapest enemy ({cheapest:0.##}), so it never "
                      + "trims a repeat - it still makes repeats less likely in the draw, and breaks ties "
                      + "between rolls.");
        }

        if (penalty > 0f && budget.rollAttempts <= 1)
        {
            notes.Add("duplicatePenalty is set with a single roll: it still leans the draw away from repeats "
                      + "and can trim a trailing one, but has no other lineups to choose between. Raise "
                      + "rollAttempts to let it pick the least repetitive one.");
        }

        if (budget.reinforcementPower > 0f)
        {
            if (budget.waveInterval <= 0)
            {
                warnings.Add($"reinforcementPower is {budget.reinforcementPower:0.##} but waveInterval is 0 - no waves are rolled.");
            }
            else if (budget.maxPowerPerWave <= 0f || (cheapest > 0f && budget.maxPowerPerWave < cheapest))
            {
                warnings.Add($"maxPowerPerWave ({budget.maxPowerPerWave:0.##}) is below the cheapest drawable enemy "
                             + $"({cheapest:0.##}) - no waves can be rolled.");
            }

            if (totals.lateWaves.Avg > 0.005)
            {
                // A warning only once it is a real share of the budget - a sliver landing on turn 11 is
                // normal jitter, a third of the reinforcements never showing up is a tuning problem.
                string late = $"On average {totals.lateWaves.Avg:0.0} waves ({totals.latePower.Avg:0.#} power, "
                              + $"{Pct(totals.latePower.Avg, budget.reinforcementPower)} of reinforcementPower) are "
                              + $"scheduled after turn {turns} and never arrive.";

                if (totals.latePower.Avg >= 0.1 * budget.reinforcementPower) { warnings.Add(late); }
                else { notes.Add(late); }
            }
        }

        int drawableBosses = pool.Count(r => r.drawable && r.body.IsBoss);

        if (budget.bossCount > 0 && drawableBosses > 0 && budget.bossCount > drawableBosses)
        {
            notes.Add($"bossCount is {budget.bossCount} but only {drawableBosses} different bosses can be drawn, so "
                      + "some openings repeat a boss.");
        }

        if (budget.bossFrontlinePower + budget.bossBacklinePower > 0f && budget.bossCount > 0)
        {
            notes.Add("Both bossCount and a boss power budget are set - the level places its counted bosses "
                      + "and then spends the boss budgets too, so it can field more bosses than bossCount says.");
        }

        if ((budget.bossFrontlinePower + budget.bossBacklinePower > 0f || budget.bossCount > 0) && drawableBosses == 0)
        {
            warnings.Add("A boss budget is set, but no boss in the pool can be drawn.");
        }

        if (warnings.Count == 0 && notes.Count == 0) { return "Nothing to flag."; }

        StringBuilder text = new();

        foreach (string warning in warnings) { text.AppendLine("!  " + warning); }
        foreach (string note in notes) { text.AppendLine("-  " + note); }

        return text.ToString();
    }

    /// <summary>
    /// The most a capped opening could spend: with repeats (the dearest body that fits each side, in
    /// every slot) and without (each type once, dearest first). Greedy for the no-repeat case, so
    /// "about" - exact enough to say whether a budget is reachable, which is all the check needs.
    /// Uses EncounterRoller.FitsSide, so the role rule is the roller's own rather than a copy.
    /// </summary>
    private static (float withRepeats, float withoutRepeats) MaxCappedSpend(List<Character> regular,
        int frontMax, int backMax)
    {
        float dearestFront = regular.Where(b => EncounterRoller.FitsSide(b, front: true))
            .Select(b => b.PowerLevel).DefaultIfEmpty(0f).Max();
        float dearestBack = regular.Where(b => EncounterRoller.FitsSide(b, front: false))
            .Select(b => b.PowerLevel).DefaultIfEmpty(0f).Max();

        float withRepeats = frontMax * dearestFront + backMax * dearestBack;

        int frontLeft = frontMax;
        int backLeft = backMax;
        float withoutRepeats = 0f;

        foreach (Character body in regular.OrderByDescending(b => b.PowerLevel))
        {
            bool front = frontLeft > 0 && EncounterRoller.FitsSide(body, front: true);
            bool back = backLeft > 0 && EncounterRoller.FitsSide(body, front: false);

            if (front && back) { if (frontLeft >= backLeft) { frontLeft--; } else { backLeft--; } }
            else if (front) { frontLeft--; }
            else if (back) { backLeft--; }
            else { continue; }

            withoutRepeats += body.PowerLevel;
        }

        return (withRepeats, withoutRepeats);
    }

    private static string ExamplesText(List<Battle> examples, DifficultyTier tier)
    {
        StringBuilder text = new();

        for (int i = 0; i < examples.Count; i++)
        {
            Battle battle = examples[i];

            string shortfall = battle.frontShort + battle.backShort > 0
                ? $"   (short {battle.frontShort} front, {battle.backShort} back)"
                : "";

            text.AppendLine($"{i + 1,3}  Opening  {Key(battle.opening)}");
            text.AppendLine($"              {battle.opening.Sum(b => b.PowerLevel):0.#} power, {battle.opening.Count} enemies, "
                            + $"{battle.opening.Sum(b => Hp(b, tier))} HP{shortfall}");

            if (battle.arriving.Count > 0)
            {
                text.AppendLine("     Waves    " + string.Join("  |  ",
                    battle.arriving.Select(wave => $"T{wave.Key} {Key(wave.Value, " + ")}")));
            }
            else
            {
                text.AppendLine("     Waves    none arrive");
            }

            if (battle.late.Count > 0)
            {
                text.AppendLine($"              +{battle.late.Count} never arrive: " + string.Join("  |  ",
                    battle.late.Select(wave => $"T{wave.turn} {Key(wave.bodies, " + ")}")));
            }

            List<Character> faced = battle.Faced.ToList();
            IGrouping<string, Character> top = faced.GroupBy(b => b.name).OrderByDescending(g => g.Count()).FirstOrDefault();

            text.AppendLine($"     Faced    {faced.Count} enemies, {faced.Select(b => b.name).Distinct().Count()} types"
                            + (top != null ? $", most common {top.Key} x{top.Count()}" : "")
                            + $", {faced.Sum(b => b.PowerLevel):0.#} power, {faced.Sum(b => Hp(b, tier))} HP");
            text.AppendLine();
        }

        return text.ToString();
    }

    private static string OpeningText(LevelData level, Totals totals, int battles, float openingBudget,
        EncounterBudget budget, string tierName)
    {
        StringBuilder text = new();

        text.AppendLine($"Rolled power  {totals.rolledOpeningPower.Describe()}   "
                        + (budget.bossCount > 0 ? "(bosses excluded)   " : "")
                        + $"exactly on the {openingBudget:0.##} budget in {Pct(totals.openingOnBudget, battles)}");

        if (level.Enemies.Count > 0)
        {
            text.AppendLine($"Hand-placed   {level.Enemies.Count} enemies stand in every opening on top of that");
        }

        text.AppendLine("Enemies       " + string.Join("   ", totals.openingSizes.OrderBy(pair => pair.Key)
                            .Select(pair => $"{pair.Key}: {Pct(pair.Value, battles)}")));
        text.AppendLine($"Roles         front {totals.openingFront.Avg:0.0}   back {totals.openingBack.Avg:0.0}   "
                        + $"either/untagged {totals.openingEither.Avg:0.0}   (average per opening, by BattleRole)");
        text.AppendLine($"Repeats       avg {totals.openingRepeats.Avg:0.00}   worst {totals.openingRepeats.Max:0}");
        text.AppendLine($"Enemy HP      {totals.openingHp.Describe("0")}   (at {tierName})");
        text.AppendLine($"Variety       {totals.openings.Count} different openings; "
                        + $"{totals.openings.Count(pair => pair.Value == 1)} appeared only once");

        if (budget.frontlineCount.min > 0 || budget.backlineCount.min > 0)
        {
            text.AppendLine($"Minimums      met in {Pct(totals.metMinimums, battles)} of battles");
        }

        return text.ToString();
    }

    private static string WavesText(Totals totals, int battles, EncounterBudget budget, int turns)
    {
        if (totals.waveTotal == 0 && totals.lateWaves.Avg <= 0)
        {
            return "No waves arrive on this level.";
        }

        StringBuilder text = new();

        text.AppendLine($"Arriving      {totals.arrivingWaves.Describe("0.#")} waves per battle");
        text.AppendLine($"Wave size     avg {totals.waveSize.Avg:0.0} enemies   repeats inside a wave avg {totals.waveRepeats.Avg:0.00}");
        text.AppendLine($"From waves    avg {totals.waveEnemies.Avg:0.0} enemies and {totals.wavePower.Avg:0.#} power per battle");

        if (budget.reinforcementPower > 0f)
        {
            text.AppendLine($"Never arrive  avg {totals.lateWaves.Avg:0.0} waves / {totals.latePower.Avg:0.#} power per battle "
                            + $"({Pct(totals.latePower.Avg, budget.reinforcementPower)} of the {budget.reinforcementPower:0.##} "
                            + $"reinforcementPower lands after turn {turns})");
        }

        text.AppendLine($"Variety       {totals.waves.Count} different waves; "
                        + $"{totals.waves.Count(pair => pair.Value == 1)} appeared only once");

        return text.ToString();
    }

    private static string WholeBattleText(Totals totals, List<PoolRow> pool, string tierName)
    {
        int drawableTypes = pool.Count(r => r.drawable);

        StringBuilder text = new();

        text.AppendLine($"Enemies faced   {totals.facedCount.Describe("0")}");
        text.AppendLine($"Enemy types     avg {totals.facedTypes.Avg:0.0} of {drawableTypes} drawable");
        text.AppendLine($"Most repeated   the commonest enemy in a battle appears avg {totals.facedTop.Avg:0.0} times "
                        + $"(worst {totals.facedTop.Max:0})");
        text.AppendLine($"Total power     {totals.facedPower.Describe("0.#")}");
        text.AppendLine($"Total enemy HP  {totals.facedHp.Describe("0")}   (at {tierName})");

        return text.ToString();
    }

    private static string PacingText(Totals totals, int battles, int turns)
    {
        double peak = totals.powerByTurn.Max() / battles;
        const int barWidth = 40;

        List<string[]> rows = new();

        for (int turn = 0; turn <= turns; turn++)
        {
            double average = totals.powerByTurn[turn] / battles;
            int bar = peak > 0 ? (int)System.Math.Round(barWidth * average / peak) : 0;

            rows.Add(new[]
            {
                turn == 0 ? "Start" : $"T{turn}",
                average.ToString("0.0"),
                new string('#', bar),
                turn == 0 ? "" : $"a wave lands in {Pct(totals.waveOnTurn[turn], battles)}",
            });
        }

        return Table(rows, false, true, false, false);
    }

    private static string FrequencyText(Totals totals, List<PoolRow> pool, int battles, DifficultyTier tier)
    {
        // Every pool entry, plus anything that spawned without being in it (hand-placed enemies).
        Dictionary<Character, Tally> all = new(totals.enemies);

        foreach (PoolRow row in pool)
        {
            if (row.body != null && !all.ContainsKey(row.body)) { all[row.body] = new Tally(); }
        }

        int grandTotal = all.Values.Sum(t => t.Total);

        List<string[]> rows = new()
        {
            new[] { "Enemy", "Power", "Role", "HP", "Opening", "Waves", "Total", "Per battle", "In battles", "Share", "Never arrive" },
        };

        foreach (KeyValuePair<Character, Tally> pair in all.OrderByDescending(p => p.Value.Total).ThenBy(p => p.Key.name))
        {
            Character body = pair.Key;
            Tally tally = pair.Value;

            rows.Add(new[]
            {
                body.name + (tally.Total == 0 ? "  <- never" : ""),
                body.PowerLevel.ToString("0.##"),
                RoleName(body.BattleRole),
                Hp(body, tier).ToString(),
                tally.opening.ToString(),
                tally.waves.ToString(),
                tally.Total.ToString(),
                ((double)tally.Total / battles).ToString("0.00"),
                Pct(tally.battles, battles),
                Pct(tally.Total, grandTotal),
                tally.late.ToString(),
            });
        }

        StringBuilder text = new(Table(rows, false, true, false, true, true, true, true, true, true, true, true));

        text.AppendLine();
        text.AppendLine("Opening/Waves/Total count every enemy that arrived. \"In battles\" is the share of battles it");
        text.AppendLine("appeared in at least once. \"Never arrive\" counts copies rolled into waves after the last turn.");

        return text.ToString();
    }

    private static string CommonText(Dictionary<string, int> counts, int whole, int shown, string unit)
    {
        if (counts.Count == 0) { return "None rolled."; }

        List<string[]> rows = new();
        int rank = 1;

        foreach (KeyValuePair<string, int> pair in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(shown))
        {
            rows.Add(new[] { $"{rank++}.", pair.Key, pair.Value.ToString(), Pct(pair.Value, whole) });
        }

        return $"{counts.Count} different, across {whole} {unit}.\n\n" + Table(rows, true, false, true, true);
    }

    private static string RareText(Dictionary<string, int> counts, int whole, int shown, string unit)
    {
        if (counts.Count == 0) { return "None rolled."; }

        int once = counts.Count(pair => pair.Value == 1);

        List<string[]> rows = counts
            .OrderBy(p => p.Value).ThenBy(p => p.Key)
            .Take(shown)
            .Select(pair => new[] { pair.Key, pair.Value.ToString(), Pct(pair.Value, whole) })
            .ToList();

        return $"{once} of {counts.Count} different {unit} appeared only once.\n\n" + Table(rows, false, true, true);
    }
}
