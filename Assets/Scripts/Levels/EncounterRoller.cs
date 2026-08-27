using System.Collections.Generic;
using UnityEngine;

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
/// </summary>
public class EncounterRoller
{
    private readonly List<Character> eligibleBodies = new();
    private readonly System.Random roll;
    private readonly Vector2Int boardSize;
    private readonly int zoneMinY;
    private readonly int splitY;
    private readonly int zoneMaxY;

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
    /// The opening lineup: frontlinePower/backlinePower/anyPower plus the two boss budgets, each drawn
    /// independently - see DrawBucket/DrawAnyBucket.
    /// </summary>
    public List<EnemyPlacement> RollStartingLineup(EncounterBudget budget)
    {
        List<EnemyPlacement> placements = new();
        placements.AddRange(DrawBucket(budget.frontlinePower, budget.maxPowerPerEnemy, BattleRole.Frontline, isBoss: false, front: true));
        placements.AddRange(DrawBucket(budget.backlinePower, budget.maxPowerPerEnemy, BattleRole.Backline, isBoss: false, front: false));
        placements.AddRange(DrawAnyBucket(budget.anyPower, budget.maxPowerPerEnemy, boss: false));
        placements.AddRange(DrawBucket(budget.bossFrontlinePower, budget.maxPowerPerEnemy, BattleRole.Frontline, isBoss: true, front: true));
        placements.AddRange(DrawBucket(budget.bossBacklinePower, budget.maxPowerPerEnemy, BattleRole.Backline, isBoss: true, front: false));
        return placements;
    }

    /// <summary>
    /// Splits reinforcementPower across successive waves of at most maxPowerPerWave, scheduled
    /// waveInterval turns apart plus a per-wave 0..waveIntervalJitter roll, until the budget is spent -
    /// "30 power split into waves of max 8, continuously" rather than one big dump. Never boss-budgeted:
    /// reinforcements draw from the same non-boss pool anyPower does. A wave that could not afford a
    /// single body stops the whole roll rather than looping forever on a budget too small to spend.
    /// </summary>
    public List<EnemyWave> RollReinforcementWaves(EncounterBudget budget)
    {
        List<EnemyWave> waves = new();

        if (budget.waveInterval <= 0) { return waves; }

        float remaining = budget.reinforcementPower;
        int turn = budget.waveInterval;

        while (remaining > 0f)
        {
            float waveBudget = Mathf.Min(remaining, budget.maxPowerPerWave);
            List<EnemyPlacement> placements = DrawAnyBucket(waveBudget, budget.maxPowerPerEnemy, boss: false);

            if (placements.Count == 0) { break; }

            foreach (EnemyPlacement placement in placements)
            {
                remaining -= placement.prefab.GetComponent<Character>().PowerLevel;
            }

            waves.Add(new EnemyWave { turn = turn, enemies = placements });

            int jitter = budget.waveIntervalJitter > 0 ? roll.Next(0, budget.waveIntervalJitter + 1) : 0;
            turn += budget.waveInterval + jitter;
        }

        return waves;
    }

    /// <summary>
    /// One role-and-boss-filtered bucket. A body with BattleRole.None counts as eligible for either
    /// dedicated bucket, same as it does for BattleManager's party placement - every prefab defaults to
    /// None today, and requiring an exact role tag first would make this feature draw nothing until
    /// every enemy in the project was individually re-tagged.
    /// </summary>
    private List<EnemyPlacement> DrawBucket(float budget, float maxPowerPerEnemy, BattleRole role, bool isBoss, bool front)
    {
        List<Character> pool = new();
        foreach (Character character in eligibleBodies)
        {
            if (character.IsBoss != isBoss) { continue; }
            if (character.BattleRole != BattleRole.None && (character.BattleRole & role) == 0) { continue; }
            pool.Add(character);
        }

        return DrawFromPool(pool, budget, maxPowerPerEnemy, front ? zoneMinY : splitY + 1, front ? splitY : zoneMaxY);
    }

    /// <summary>
    /// The combined frontline+backline pool, rolling each pick's own side - what lets one power number
    /// land anywhere from all-frontline to all-backline across different playthroughs.
    /// </summary>
    private List<EnemyPlacement> DrawAnyBucket(float budget, float maxPowerPerEnemy, bool boss)
    {
        List<Character> pool = new();
        foreach (Character character in eligibleBodies)
        {
            if (character.IsBoss == boss) { pool.Add(character); }
        }

        List<EnemyPlacement> placements = new();
        float remaining = budget;

        while (true)
        {
            Character picked = PickAffordable(pool, maxPowerPerEnemy, remaining);
            if (picked == null) { break; }

            bool front = roll.Next(2) == 0;
            Vector2Int cell = RandomCellInZone(front ? zoneMinY : splitY + 1, front ? splitY : zoneMaxY);
            placements.Add(new EnemyPlacement { prefab = picked.gameObject, cell = cell });
            remaining -= picked.PowerLevel;
        }

        return placements;
    }

    private List<EnemyPlacement> DrawFromPool(List<Character> pool, float budget, float maxPowerPerEnemy, int minY, int maxY)
    {
        List<EnemyPlacement> placements = new();
        float remaining = budget;

        while (true)
        {
            Character picked = PickAffordable(pool, maxPowerPerEnemy, remaining);
            if (picked == null) { break; }

            placements.Add(new EnemyPlacement { prefab = picked.gameObject, cell = RandomCellInZone(minY, maxY) });
            remaining -= picked.PowerLevel;
        }

        return placements;
    }

    /// <summary>
    /// A uniformly random body from `pool` costing no more than maxPowerPerEnemy AND no more than
    /// `remaining` - null once nothing left is cheap enough, which is what makes budget a ceiling
    /// rather than a target every draw loop above stops on.
    /// </summary>
    private Character PickAffordable(List<Character> pool, float maxPowerPerEnemy, float remaining)
    {
        List<Character> affordable = new();
        foreach (Character character in pool)
        {
            if (character.PowerLevel <= maxPowerPerEnemy && character.PowerLevel <= remaining) { affordable.Add(character); }
        }

        return affordable.Count > 0 ? affordable[roll.Next(affordable.Count)] : null;
    }

    private Vector2Int RandomCellInZone(int minY, int maxY)
    {
        int x = roll.Next(0, Mathf.Max(1, boardSize.x));
        int y = minY <= maxY ? roll.Next(minY, maxY + 1) : Mathf.Clamp(minY, 0, Mathf.Max(0, boardSize.y - 1));
        return new Vector2Int(x, y);
    }
}
