using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Everything one battle's random encounter produced: the opening lineup, the reinforcement waves, and
/// how far the opening fell short of the level's role minimums. What EncounterRoller.RollFor returns -
/// to BattleManager for a real battle, and to the Encounter Simulator thousands of times over.
///
/// Waves are returned as rolled, including any scheduled past the level's last turn - those never
/// arrive in a real battle (BattleManager.SpawnDueWaves only ever reaches TurnsToSurvive), and it is
/// the caller's business to know that, not the roller's.
/// </summary>
public readonly struct RolledEncounter
{
    public readonly List<EnemyPlacement> opening;
    public readonly List<EnemyWave> waves;
    public readonly int frontlineShortfall;
    public readonly int backlineShortfall;

    /// Power of the bosses EncounterBudget.bossCount placed - on top of every power budget rather than
    /// out of one, so anything comparing the opening against its budget has to take this off first.
    public readonly float bossCountPower;

    public RolledEncounter(List<EnemyPlacement> opening, List<EnemyWave> waves,
        int frontlineShortfall, int backlineShortfall, float bossCountPower)
    {
        this.opening = opening;
        this.waves = waves;
        this.frontlineShortfall = frontlineShortfall;
        this.backlineShortfall = backlineShortfall;
        this.bossCountPower = bossCountPower;
    }

    public bool MetMinimums => frontlineShortfall == 0 && backlineShortfall == 0;
}

/// <summary>
/// Resolves a level's EncounterBudget into concrete EnemyPlacements and EnemyWaves, drawing randomly
/// from an EnemyRegistry. Plain C# rather than a MonoBehaviour/ScriptableObject so it carries no scene
/// lifetime of its own and is directly testable - BattleManager owns calling it once, at battle start,
/// and holding onto the result (see BattleManager.RollEncounter).
///
/// Frontline/backline are two zones derived from board geometry, never authored: everything above the
/// party's deepest spawn cell is enemy territory (falling back to the board's own top half when a level
/// authors no party spawn cells at all), split by y into a near half (frontline) and a far half
/// (backline, closer to the board's own far edge) - the same axis a level already lays its hand-placed
/// enemies out on, e.g. Level4BlackKnight's boss sits on the board's very last row. See
/// BattleManager.SpawnParty/AssignPartyCells for the mirror-image version on the party's side.
///
/// **Rolls a whole lineup several times and keeps the best one**, rather than making one greedy pass.
/// A single pass spends until nothing is affordable, which fails in two ways a budget alone cannot
/// fix: it always converts power into the largest headcount it can buy, and because `remaining` only
/// shrinks, the tail of every draw is whatever the cheapest body is. Four bats and a goblin is what
/// that produces. Instead, each roll:
///
/// 1. leans away from types already in the lineup, as hard as duplicatePenalty says (PickBody),
/// 2. when headcount is capped, prefers bodies costing about `remaining / slots left`, so the budget
///    spreads across the bodies allowed rather than pooling in the first few picks (PerSlotTarget),
/// 3. is scored at every point it could have stopped, so a cheap trailing duplicate is dropped when the
///    lineup is better without it (TruncateToBestPrefix),
///
/// and the best of `rollAttempts` rolls wins. See Score for how the terms are weighed.
///
/// **Variety is entirely duplicatePenalty's business.** At its default of 0 there is none: every
/// affordable enemy is equally likely on every pick, repeats included, step 3 never trims anything, and
/// scoring ignores repeats. Raising it turns all three on together - the draw leans away from repeats,
/// cheap trailing repeats get trimmed, and retries prefer the least repetitive lineup. Step 2 is on
/// whenever headcount is capped, whatever the penalty.
/// </summary>
public class EncounterRoller
{
    /// Lineups rolled per draw when a level does not say: a single pass, so retrying is something a
    /// level opts into by setting rollAttempts. An unset 0 is read as this rather than as zero attempts,
    /// which would roll nothing at all.
    private const int DefaultAttempts = 1;

    /// What one body short of a role's minimum costs. Deliberately far above any real waste or
    /// duplicate total, so an attempt that met the minimums always beats one that did not, and the
    /// other two terms only ever break ties among attempts that did.
    private const float ShortfallPenalty = 1000f;

    /// Floor on the distance term in PickBody's cost weighting, so a body costing exactly the
    /// target is strongly preferred (weight 4) without being the only thing ever picked.
    private const float TargetSoftness = 0.25f;

    /// The turn the opening lineup is the whole board on - BattleManager's TurnsElapsed on the first
    /// TurnStart. Reinforcements are scheduled after it; see RollReinforcementWaves.
    private const int OpeningTurn = 1;

    private readonly List<Character> eligibleBodies = new();
    private readonly System.Random roll;
    private readonly Vector2Int boardSize;
    private readonly int zoneMinY;
    private readonly int splitY;
    private readonly int zoneMaxY;

    /// Which half of the enemy zone a bucket places into. Either lets the draw choose per body, which
    /// is what makes one anyPower number land anywhere from all-frontline to all-backline.
    private enum SideMode
    {
        Front,
        Back,
        Either,
    }

    /// One body placed into a lineup. Kept alongside its Character rather than as a bare EnemyPlacement
    /// so a lineup can be truncated and its bookkeeping rebuilt without a GetComponent per body.
    private readonly struct Pick
    {
        public readonly Character body;
        public readonly bool front;
        public readonly Vector2Int cell;

        public Pick(Character body, bool front, Vector2Int cell)
        {
            this.body = body;
            this.front = front;
            this.cell = cell;
        }
    }

    /// <summary>
    /// One candidate lineup, mid-roll: what has been placed, what it cost, and the bookkeeping Score
    /// reads. A class because it is mutated in place all the way through a roll and handed to helpers
    /// that add to it.
    /// </summary>
    private sealed class Lineup
    {
        public readonly List<Pick> picks = new();

        /// Placements per distinct prefab, so a truncation can tell whether removing a body also
        /// removes the last of its kind.
        private readonly Dictionary<GameObject, int> countByPrefab = new();

        public float Spent { get; private set; }
        public int FrontCount { get; private set; }
        public int BackCount { get; private set; }

        public int Duplicates => picks.Count - countByPrefab.Count;

        /// How many of this prefab the lineup already holds - what PickBody's variety weight reads.
        public int UseCount(GameObject prefab) => countByPrefab.TryGetValue(prefab, out int n) ? n : 0;

        public void Add(Pick pick)
        {
            picks.Add(pick);

            GameObject prefab = pick.body.gameObject;
            countByPrefab[prefab] = countByPrefab.TryGetValue(prefab, out int n) ? n + 1 : 1;

            Spent += pick.body.PowerLevel;
            if (pick.front) { FrontCount++; } else { BackCount++; }
        }

        /// Drops everything after the first `length` picks, keeping the bookkeeping true.
        public void TruncateTo(int length)
        {
            for (int i = picks.Count - 1; i >= length; i--)
            {
                Pick pick = picks[i];
                GameObject prefab = pick.body.gameObject;

                if (--countByPrefab[prefab] == 0) { countByPrefab.Remove(prefab); }

                Spent -= pick.body.PowerLevel;
                if (pick.front) { FrontCount--; } else { BackCount--; }

                picks.RemoveAt(i);
            }
        }

        public List<EnemyPlacement> ToPlacements()
        {
            List<EnemyPlacement> placements = new(picks.Count);

            foreach (Pick pick in picks)
            {
                placements.Add(new EnemyPlacement { prefab = pick.body.gameObject, cell = pick.cell });
            }

            return placements;
        }
    }

    public EncounterRoller(EnemyRegistry registry, IReadOnlyList<GameObject> poolFilter,
        IReadOnlyList<Vector2Int> partySpawnCells, Vector2Int boardSize, System.Random roll)
    {
        this.roll = roll;
        this.boardSize = boardSize;

        if (registry != null)
        {
            foreach (EnemyRegistryEntry entry in registry.Entries)
            {
                if (!entry.eligibleForRandomDraw || entry.prefab == null) { continue; }
                if (!PassesPoolFilter(entry.prefab, poolFilter)) { continue; }

                Character character = entry.prefab.GetComponent<Character>();

                // PowerLevel <= 0 means unscored (see Character.powerLevel's own tooltip) - excluded
                // here rather than checked at every draw site, so an unscored body silently never
                // appears in a random encounter instead of being drawn for free.
                if (character != null && character.PowerLevel > 0f) { eligibleBodies.Add(character); }
            }
        }

        int partyFrontY = -1;
        if (partySpawnCells != null)
        {
            foreach (Vector2Int cell in partySpawnCells)
            {
                if (cell.y > partyFrontY) { partyFrontY = cell.y; }
            }
        }

        zoneMinY = partyFrontY >= 0 ? partyFrontY + 1 : boardSize.y / 2;
        zoneMaxY = boardSize.y - 1;
        if (zoneMinY > zoneMaxY) { zoneMinY = zoneMaxY; }
        splitY = zoneMinY + (zoneMaxY - zoneMinY) / 2;
    }

    /// <summary>
    /// Rolls one battle's encounter for `level` at difficulty `tier` - **the one path both a real
    /// battle and the Encounter Simulator go through.** BattleManager.RollEncounter calls this, and so
    /// does Tools/Levels/Encounter Simulator; keeping every step of it here is what makes the
    /// simulator's numbers the game's numbers rather than a copy of the rules that could drift.
    ///
    /// The tier scales the budget before anything is spent, so the opening and the waves are drawn
    /// against the same difficulty. EncounterBudget is a struct and LevelData hands out its own by
    /// value, so the scaling happens on a local copy and the level asset is never written to - a
    /// runtime write to a ScriptableObject would persist into the .asset after Play Mode exits.
    ///
    /// The roller reads each enemy's PowerLevel, BattleRole and boss flag straight off its prefab, so
    /// retuning an enemy changes what this rolls with no other step.
    /// </summary>
    public static RolledEncounter RollFor(LevelData level, EnemyRegistry registry, DifficultyTier tier,
        Vector2Int boardSize, System.Random dice)
    {
        if (level == null)
        {
            return new RolledEncounter(new List<EnemyPlacement>(), new List<EnemyWave>(), 0, 0, 0f);
        }

        EncounterBudget budget = tier.Scale(level.EncounterBudget);
        EncounterRoller roller = new(registry, budget.poolFilter, level.PartySpawnCells, boardSize, dice);

        List<EnemyPlacement> opening =
            roller.RollStartingLineup(budget, out int frontShort, out int backShort, out float bossPower);
        List<EnemyWave> waves = roller.RollReinforcementWaves(budget);

        return new RolledEncounter(opening, waves, frontShort, backShort, bossPower);
    }

    private static bool PassesPoolFilter(GameObject prefab, IReadOnlyList<GameObject> poolFilter)
    {
        if (poolFilter == null || poolFilter.Count == 0) { return true; }

        foreach (GameObject allowed in poolFilter)
        {
            if (allowed == prefab) { return true; }
        }

        return false;
    }

    /// <summary>
    /// The opening lineup: frontlinePower/backlinePower/anyPower plus the two boss budgets.
    ///
    /// All five buckets are drawn into **one** lineup and scored together, because frontlineCount and
    /// backlineCount are limits on the board rather than on a bucket - five buckets each separately
    /// honouring "at most 5 frontline" would happily place twenty-five.
    ///
    /// anyPower is drawn last, after the dedicated and boss buckets, because it is the only bucket that
    /// chooses its own sides: by the time it runs it can see what the fixed buckets committed to, and
    /// spend its budget on whatever the minimums are still short of. It is also the only bucket that
    /// may stop short of its budget (see TruncateToBestPrefix) - the fixed buckets are a designer
    /// asking for exactly that much of exactly that role, and bosses in particular must never be
    /// trimmed away to make the numbers tidier.
    ///
    /// The two shortfalls say how many bodies of each role the winning lineup still lacks against the
    /// authored minimums - reported rather than logged here, so a caller rolling thousands of battles
    /// (the Encounter Simulator) can tally them instead of flooding the console. See RollFor.
    ///
    /// bossCount's bosses are placed before everything else that chooses a side, so a boss always gets
    /// a slot while one is free, and `bossCountPower` reports what they cost - see PlaceBosses.
    /// </summary>
    public List<EnemyPlacement> RollStartingLineup(EncounterBudget budget,
        out int frontlineShortfall, out int backlineShortfall, out float bossCountPower)
    {
        frontlineShortfall = 0;
        backlineShortfall = 0;
        bossCountPower = 0f;

        float total = budget.frontlinePower + budget.backlinePower + budget.anyPower
                      + budget.bossFrontlinePower + budget.bossBacklinePower;

        if (total <= 0f && budget.bossCount <= 0) { return new List<EnemyPlacement>(); }

        List<Character> frontPool = RolePool(BattleRole.Frontline, isBoss: false);
        List<Character> backPool = RolePool(BattleRole.Backline, isBoss: false);
        List<Character> anyPool = BossPool(boss: false);
        List<Character> bossPool = BossPool(boss: true);
        List<Character> bossFrontPool = RolePool(BattleRole.Frontline, isBoss: true);
        List<Character> bossBackPool = RolePool(BattleRole.Backline, isBoss: true);

        Lineup best = null;
        float bestScore = float.MaxValue;

        for (int attempt = 0; attempt < Attempts(budget); attempt++)
        {
            Lineup candidate = new();

            Spend(candidate, frontPool, budget.frontlinePower, budget, SideMode.Front);
            Spend(candidate, backPool, budget.backlinePower, budget, SideMode.Back);

            float bossPower = PlaceBosses(candidate, bossPool, budget);

            Spend(candidate, bossFrontPool, budget.bossFrontlinePower, budget, SideMode.Front);
            Spend(candidate, bossBackPool, budget.bossBacklinePower, budget, SideMode.Back);

            float anyRemaining = FillMinimums(candidate, anyPool, budget.anyPower, budget);

            // Everything placed so far - the fixed buckets, the bosses and the minimums - is kept
            // whatever the score says. Only the free spending after this point is a candidate for
            // trimming.
            int floor = candidate.picks.Count;

            Spend(candidate, anyPool, anyRemaining, budget, SideMode.Either);

            // The counted bosses' power is added to the target as well as sitting in Spent, so it cancels
            // out of the waste term. Left out, a boss would read as overspend and the score would trim
            // regular enemies to "pay" for a boss that was never meant to come out of the budget.
            float score = TruncateToBestPrefix(candidate, floor, total + bossPower, budget);

            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
                bossCountPower = bossPower;
            }
        }

        if (best == null) { return new List<EnemyPlacement>(); }

        frontlineShortfall = Mathf.Max(0, budget.frontlineCount.min - best.FrontCount);
        backlineShortfall = Mathf.Max(0, budget.backlineCount.min - best.BackCount);

        return best.ToPlacements();
    }

    /// <summary>
    /// Places EncounterBudget.bossCount bosses, each picked uniformly at random from every drawable boss
    /// and stood on a side its BattleRole allows that still has headcount room. Every boss is untagged
    /// today, so the side is a coin flip - which is the point: the boss power budgets fix a side, this
    /// does not. A boss tagged Frontline or Backline later is respected through FitsSide.
    ///
    /// Prefers a boss not already placed, so a two-boss level never repeats one while another is
    /// available. Deliberately not governed by duplicatePenalty: a repeated goblin is texture, a repeated
    /// boss is a set piece shown twice.
    ///
    /// Costs nothing from any power budget - a count means "one boss" whatever the bosses cost - and
    /// returns the power placed so RollStartingLineup can keep it out of the waste term. Still subject
    /// to maxPowerPerEnemy, like every other draw.
    /// </summary>
    private float PlaceBosses(Lineup lineup, List<Character> bossPool, EncounterBudget rules)
    {
        float placed = 0f;

        for (int i = 0; i < rules.bossCount; i++)
        {
            bool frontRoom = rules.frontlineCount.HasRoomFor(lineup.FrontCount);
            bool backRoom = rules.backlineCount.HasRoomFor(lineup.BackCount);

            List<Character> candidates = new();
            List<Character> fresh = new();

            foreach (Character boss in bossPool)
            {
                if (boss.PowerLevel > rules.maxPowerPerEnemy) { continue; }

                bool fits = (frontRoom && FitsSide(boss, front: true)) || (backRoom && FitsSide(boss, front: false));

                if (!fits) { continue; }

                candidates.Add(boss);

                if (lineup.UseCount(boss.gameObject) == 0) { fresh.Add(boss); }
            }

            List<Character> from = fresh.Count > 0 ? fresh : candidates;

            if (from.Count == 0) { break; }

            Character picked = from[roll.Next(from.Count)];

            bool canFront = frontRoom && FitsSide(picked, front: true);
            bool canBack = backRoom && FitsSide(picked, front: false);

            Place(lineup, picked, front: canFront && (!canBack || roll.Next(2) == 0));
            placed += picked.PowerLevel;
        }

        return placed;
    }

    /// <summary>
    /// Splits reinforcementPower across successive waves of at most maxPowerPerWave, scheduled
    /// waveInterval turns apart plus a per-wave 0..waveIntervalJitter roll, until the budget is spent -
    /// "30 power split into waves of max 8, continuously" rather than one big dump. The first wave lands
    /// on turn 1 + waveInterval, never turn 1 itself - that turn's board is the opening. Never boss-budgeted:
    /// reinforcements draw from the same non-boss pool anyPower does. A wave that could not afford a
    /// single body stops the whole roll rather than looping forever on a budget too small to spend.
    ///
    /// Each wave gets the variety preference and the best-of-several-attempts pick, but deliberately
    /// neither the headcounts nor the trimming. frontlineCount/backlineCount describe the board a level
    /// opens with - a six-power wave forced to carry "at least two backline" would be a different
    /// feature. And a trimmed wave spends less, which would stretch reinforcementPower across more waves
    /// and quietly change how a level's pressure builds, which is a pacing decision for the level, not
    /// a side effect of a variety fix.
    /// </summary>
    public List<EnemyWave> RollReinforcementWaves(EncounterBudget budget)
    {
        List<EnemyWave> waves = new();

        if (budget.waveInterval <= 0) { return waves; }

        List<Character> pool = BossPool(boss: false);

        // A wave answers to no headcount - see this method's summary.
        EncounterBudget waveRules = budget;
        waveRules.frontlineCount = default;
        waveRules.backlineCount = default;

        float remaining = budget.reinforcementPower;

        // Never on the opening turn. BattleManager.TurnStart spawns a wave due on turn 1 before the
        // player's first action, so it would stand on the very board frontlineCount/backlineCount
        // describe and break them - a "max 2 frontline" level showing four. The first reinforcement
        // lands one interval after turn 1 instead: turn 2 at an interval of 1.
        int turn = OpeningTurn + budget.waveInterval;

        while (remaining > 0f)
        {
            float waveBudget = Mathf.Min(remaining, budget.maxPowerPerWave);
            Lineup wave = BestWave(pool, waveBudget, waveRules);

            if (wave == null || wave.picks.Count == 0) { break; }

            remaining -= wave.Spent;

            waves.Add(new EnemyWave { turn = turn, enemies = wave.ToPlacements() });

            int jitter = budget.waveIntervalJitter > 0 ? roll.Next(0, budget.waveIntervalJitter + 1) : 0;
            turn += budget.waveInterval + jitter;
        }

        return waves;
    }

    private Lineup BestWave(List<Character> pool, float budget, EncounterBudget rules)
    {
        if (budget <= 0f) { return null; }

        Lineup best = null;
        float bestScore = float.MaxValue;

        for (int attempt = 0; attempt < Attempts(rules); attempt++)
        {
            Lineup candidate = new();

            Spend(candidate, pool, budget, rules, SideMode.Either);

            float score = Score(candidate, budget, rules);

            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    // ---- Scoring -------------------------------------------------------------------------------

    /// <summary>
    /// How good a lineup is, lower being better. Three terms, in the order they matter:
    ///
    /// - **shortfall** - bodies short of a role's authored minimum, at a penalty large enough that any
    ///   lineup meeting the minimums beats any that did not.
    /// - **waste** - how far the lineup landed from its power budget. This is what "try a few times to
    ///   get closer to the total power" buys.
    /// - **duplicates** - bodies repeating a prefab already in the lineup, at duplicatePenalty each.
    ///   This is what lets Crab + Goblin + Bat beat four bats and a goblin even though the second spends
    ///   more of the budget.
    ///
    /// Waste and duplicates are in the same units on purpose. Dropping a repeated body raises waste by
    /// that body's power and lowers duplicates by one, so it is worth doing exactly when the body costs
    /// less than duplicatePenalty - which makes the penalty read as "the cheapest repeat worth keeping".
    /// </summary>
    private static float Score(Lineup lineup, float budget, EncounterBudget rules)
    {
        return Shortfall(lineup, rules) * ShortfallPenalty
               + Mathf.Abs(budget - lineup.Spent)
               + lineup.Duplicates * DuplicatePenalty(rules);
    }

    private static int Shortfall(Lineup lineup, EncounterBudget rules) =>
        Mathf.Max(0, rules.frontlineCount.min - lineup.FrontCount)
        + Mathf.Max(0, rules.backlineCount.min - lineup.BackCount);

    /// <summary>
    /// Scores the lineup at every length from `floor` to its full length, keeps the best, and returns
    /// that score.
    ///
    /// This is what lets a roll choose to stop early. A draw loop on its own only ever stops when it
    /// runs out of affordable bodies, so it can never produce "fewer, but all different" - it always
    /// buys the last cheap duplicate too. Scoring each stopping point is cheap (a lineup is a handful of
    /// bodies) and needs no guess about where to stop: Score's own terms decide.
    ///
    /// Ties go to the longer lineup, so trimming only happens when it is a strict improvement - which
    /// means never while duplicatePenalty is 0, since dropping a body can then only add waste.
    /// </summary>
    private static float TruncateToBestPrefix(Lineup lineup, int floor, float budget, EncounterBudget rules)
    {
        int full = lineup.picks.Count;
        int bestLength = full;
        float bestScore = Score(lineup, budget, rules);

        // Walk backwards by trimming one at a time - Lineup keeps its bookkeeping true on the way down,
        // so each shorter prefix is scored exactly without being rebuilt from scratch.
        List<Pick> removed = new();

        for (int length = full - 1; length >= floor; length--)
        {
            removed.Add(lineup.picks[length]);
            lineup.TruncateTo(length);

            float score = Score(lineup, budget, rules);

            if (score < bestScore)
            {
                bestScore = score;
                bestLength = length;
            }
        }

        // Put back everything between `floor` and the winning length, in original order.
        for (int i = removed.Count - 1; i >= 0 && lineup.picks.Count < bestLength; i--)
        {
            lineup.Add(removed[i]);
        }

        return bestScore;
    }

    private static int Attempts(EncounterBudget rules) =>
        rules.rollAttempts > 0 ? rules.rollAttempts : DefaultAttempts;

    /// Taken literally, unlike rollAttempts: 0 is a real setting meaning "repeats cost nothing when
    /// scoring". Clamped at 0 so a negative typo cannot start rewarding repeats.
    private static float DuplicatePenalty(EncounterBudget rules) => Mathf.Max(0f, rules.duplicatePenalty);

    // ---- Drawing -------------------------------------------------------------------------------

    /// <summary>
    /// Spends `budget` from `pool` into `lineup` until nothing affordable fits a side with room left,
    /// returning nothing - the lineup's own bookkeeping is the record of what was spent.
    ///
    /// **The body is chosen before the side**, from every affordable body that fits *some* side with
    /// room, and then placed on a side its BattleRole allows. That order is what keeps the frontline/
    /// backline mix following the pool: a pool of five frontline types and three backline types comes
    /// out roughly five to three, the way the old uniform draw did. Choosing the side first - a coin
    /// flip, then a body to fit it - forced every uncapped lineup toward half backline whatever the pool
    /// held, which quietly made ranged enemies far more common than a level's pool implied.
    /// </summary>
    private void Spend(Lineup lineup, List<Character> pool, float budget, EncounterBudget rules, SideMode mode)
    {
        if (budget <= 0f || pool.Count == 0) { return; }

        float remaining = budget;

        while (true)
        {
            bool frontRoom = mode != SideMode.Back && rules.frontlineCount.HasRoomFor(lineup.FrontCount);
            bool backRoom = mode != SideMode.Front && rules.backlineCount.HasRoomFor(lineup.BackCount);

            if (!frontRoom && !backRoom) { break; }

            float target = PerSlotTarget(lineup, rules, mode, remaining);

            if (!TryPlace(lineup, pool, rules, ref remaining, frontRoom, backRoom, target)) { break; }
        }
    }

    /// <summary>
    /// Places bodies on each side until its authored minimum is met or the budget will not stretch,
    /// returning what is left. Backline first, since backline bodies are the scarcer role in the
    /// registry and the more likely one to go unmet if frontline were allowed to spend the budget first.
    ///
    /// A minimum is a request, not a guarantee - a roll that could not afford it is scored down by
    /// Score's shortfall term rather than forced over budget.
    /// </summary>
    private float FillMinimums(Lineup lineup, List<Character> pool, float budget, EncounterBudget rules)
    {
        float remaining = budget;

        foreach (bool front in new[] { false, true })
        {
            int minimum = front ? rules.frontlineCount.min : rules.backlineCount.min;

            while ((front ? lineup.FrontCount : lineup.BackCount) < minimum)
            {
                float target = PerSlotTarget(lineup, rules, SideMode.Either, remaining);

                // Only the side being filled is offered, and only while it still has room. A minimum
                // authored above its own maximum, or one the fixed buckets already filled the role past,
                // stops at the ceiling and is reported as a shortfall - a max is never exceeded to
                // satisfy a min.
                bool frontRoom = front && rules.frontlineCount.HasRoomFor(lineup.FrontCount);
                bool backRoom = !front && rules.backlineCount.HasRoomFor(lineup.BackCount);

                if (!TryPlace(lineup, pool, rules, ref remaining, frontRoom, backRoom, target)) { break; }
            }
        }

        return remaining;
    }

    /// <summary>
    /// Places one affordable body on a side that has room and that its BattleRole allows, reporting
    /// whether it did. A body that fits both open sides - BattleRole None or Both - takes a coin flip,
    /// which is the one place a side is still chosen at random.
    /// </summary>
    private bool TryPlace(Lineup lineup, List<Character> pool, EncounterBudget rules, ref float remaining,
        bool frontRoom, bool backRoom, float target)
    {
        if (!frontRoom && !backRoom) { return false; }

        Character picked =
            PickBody(pool, rules, remaining, lineup, frontRoom, backRoom, target);

        if (picked == null) { return false; }

        bool canFront = frontRoom && FitsSide(picked, front: true);
        bool canBack = backRoom && FitsSide(picked, front: false);

        Place(lineup, picked, front: canFront && (!canBack || roll.Next(2) == 0));
        remaining -= picked.PowerLevel;

        return true;
    }

    /// Adds `body` to the lineup on the given side, at a random cell in that side's zone. The one place
    /// a cell is chosen, shared by regular enemies and bossCount's bosses.
    private void Place(Lineup lineup, Character body, bool front)
    {
        Vector2Int cell = RandomCellInZone(front ? zoneMinY : splitY + 1, front ? splitY : zoneMaxY);

        lineup.Add(new Pick(body, front, cell));
    }

    /// <summary>
    /// The cost a body should ideally have for the budget left to spread evenly across the headcount
    /// left - or 0, meaning "no preference", when either side this bucket may place on is uncapped.
    ///
    /// Without this a capped lineup under-spends by luck: seven slots and 25 power, filled uniformly
    /// from bodies averaging 1.8, comes to about 13. Aiming each pick at `remaining / slots left` pulls
    /// the picks toward whatever makes the numbers land, and re-aims after every pick, so an early
    /// cheap body is compensated for by a dearer one later.
    /// </summary>
    private static float PerSlotTarget(Lineup lineup, EncounterBudget rules, SideMode mode, float remaining)
    {
        int frontLeft = mode == SideMode.Back ? 0 : SlotsLeft(rules.frontlineCount, lineup.FrontCount);
        int backLeft = mode == SideMode.Front ? 0 : SlotsLeft(rules.backlineCount, lineup.BackCount);

        if (frontLeft == int.MaxValue || backLeft == int.MaxValue) { return 0f; }

        int slots = frontLeft + backLeft;

        return slots > 0 ? remaining / slots : 0f;
    }

    private static int SlotsLeft(RoleCount count, int current) =>
        count.max <= 0 ? int.MaxValue : Mathf.Max(0, count.max - current);

    /// <summary>
    /// A random affordable body that fits at least one side with room (`frontRoom`/`backRoom` - see
    /// FitsSide), weighted by two things:
    ///
    /// - **variety** - a type already in this lineup `k` times is drawn `1 / (1 + duplicatePenalty * k)`
    ///   as often as one not yet used. At duplicatePenalty 0 that is 1 for everything: repeats are
    ///   exactly as likely as anything else, which is what a level with no penalty asks for. At 1.5 a
    ///   type used once is drawn at 0.4 the weight of a fresh one, twice at 0.25; at 10 repeats all but
    ///   stop while anything else is affordable. One knob, so a designer never has a level that says
    ///   "repeats cost nothing" while the draw quietly refuses to repeat anyway.
    /// - **cost** - with a `target` (see PerSlotTarget), bodies costing about that much are preferred.
    ///   Weighted rather than "closest wins" so a level still sees different bodies each playthrough.
    ///
    /// Affordable means costing no more than maxPowerPerEnemy AND no more than `remaining`, so a budget
    /// is still a ceiling rather than a target - the draw stops when nothing fits.
    /// </summary>
    private Character PickBody(List<Character> pool, EncounterBudget rules, float remaining,
        Lineup lineup, bool frontRoom, bool backRoom, float target)
    {
        float penalty = DuplicatePenalty(rules);

        List<Character> affordable = new();
        List<float> weights = new();
        float totalWeight = 0f;

        foreach (Character character in pool)
        {
            if (character.PowerLevel > rules.maxPowerPerEnemy || character.PowerLevel > remaining) { continue; }

            bool fits = (frontRoom && FitsSide(character, front: true))
                        || (backRoom && FitsSide(character, front: false));

            if (!fits) { continue; }

            float weight = 1f / (1f + penalty * lineup.UseCount(character.gameObject));

            if (target > 0f) { weight *= 1f / (TargetSoftness + Mathf.Abs(character.PowerLevel - target)); }

            affordable.Add(character);
            weights.Add(weight);
            totalWeight += weight;
        }

        if (affordable.Count == 0) { return null; }

        float pick = (float)roll.NextDouble() * totalWeight;

        for (int i = 0; i < affordable.Count; i++)
        {
            pick -= weights[i];

            if (pick <= 0f) { return affordable[i]; }
        }

        return affordable[affordable.Count - 1];
    }

    /// <summary>
    /// Whether a body may stand on (and count toward) the frontline or backline. This is what makes
    /// frontlineCount/backlineCount mean *role* rather than *zone*: "at least two backline" is two
    /// rangers, eyes or skulls, never two goblins that happened to be rolled into the back half.
    ///
    /// None and Both fit either side - None because every prefab defaulted to it before roles existed
    /// (see BattleRole), Both because that is what it says. The anyPower bucket used to drop any body on
    /// a coin-flipped side regardless of role; routing it through here also stops a melee bruiser
    /// spawning in the far back row.
    /// </summary>
    public static bool FitsSide(Character character, bool front)
    {
        if (character.BattleRole == BattleRole.None) { return true; }

        return (character.BattleRole & (front ? BattleRole.Frontline : BattleRole.Backline)) != 0;
    }

    /// <summary>
    /// The role-and-boss-filtered pool. A body with BattleRole.None counts as eligible for either
    /// dedicated bucket, same as it does for BattleManager's party placement - every prefab defaulted to
    /// None before roles existed, and requiring an exact role tag first would make this feature draw
    /// nothing until every enemy in the project was individually re-tagged.
    /// </summary>
    private List<Character> RolePool(BattleRole role, bool isBoss)
    {
        List<Character> pool = new();

        foreach (Character character in eligibleBodies)
        {
            if (character.IsBoss != isBoss) { continue; }
            if (character.BattleRole != BattleRole.None && (character.BattleRole & role) == 0) { continue; }

            pool.Add(character);
        }

        return pool;
    }

    /// The combined frontline+backline pool - what anyPower and the reinforcement waves draw from.
    private List<Character> BossPool(bool boss)
    {
        List<Character> pool = new();

        foreach (Character character in eligibleBodies)
        {
            if (character.IsBoss == boss) { pool.Add(character); }
        }

        return pool;
    }

    private Vector2Int RandomCellInZone(int minY, int maxY)
    {
        int x = roll.Next(0, Mathf.Max(1, boardSize.x));
        int y = minY <= maxY ? roll.Next(minY, maxY + 1) : Mathf.Clamp(minY, 0, Mathf.Max(0, boardSize.y - 1));
        return new Vector2Int(x, y);
    }
}
