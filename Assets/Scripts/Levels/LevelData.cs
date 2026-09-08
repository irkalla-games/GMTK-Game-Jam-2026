using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One enemy to place when the battle starts.
///
/// Stats and deck live on the prefab's own Character rather than being repeated here - there is
/// one obvious place to tune a goblin, and it is the goblin. The deck override exists only so the
/// same prefab can turn up twice with different cards without needing a second prefab.
/// </summary>
[System.Serializable]
public struct EnemyPlacement
{
    [Tooltip("Prefab with a Character on it. Health, brain, damage and reach all come from there.")]
    public GameObject prefab;

    [Tooltip("Grid cell it starts on.")]
    [OneBasedCell] public Vector2Int cell;

    [Tooltip("Leave empty to use the prefab's own deck.")]
    public List<CardData> deckOverride;
}

/// <summary>
/// One reinforcement wave: a second batch of EnemyPlacements that joins the battle mid-fight instead of
/// at the start. Reuses EnemyPlacement rather than inventing a parallel shape - a wave enemy is placed
/// exactly the same way SpawnEnemies places the opening roster, just later.
/// </summary>
[System.Serializable]
public struct EnemyWave
{
    [Tooltip("Which round this wave spawns on - the same count BattleManager.TurnsElapsed reaches.")]
    public int turn;

    public List<EnemyPlacement> enemies;
}

/// <summary>
/// A level's power budgets for EncounterRoller, on top of whatever is hand-authored in `enemies`/
/// `waves`. Every field defaults to 0, and 0 spends nothing - a level that never touches this struct
/// rolls exactly nothing, the same "0 is inert" rule BoardSize and RangeShape.Anywhere already follow,
/// which is what keeps every existing tailored level spawning unchanged.
///
/// frontlinePower/backlinePower/bossFrontlinePower/bossBacklinePower each draw from their own
/// role-and-boss-filtered pool; anyPower draws from the combined frontline+backline pool and rolls
/// each pick's side independently, so one number can land anywhere from all-frontline to all-backline
/// across different playthroughs rather than a fixed split. reinforcementPower is spent the same
/// "any" way, across successive generated EnemyWaves of at most maxPowerPerWave, scheduled
/// waveInterval turns apart (plus a random 0..waveIntervalJitter offset per wave) until it runs out -
/// see EncounterRoller.RollReinforcementWaves. Every draw excludes bodies costing more than
/// maxPowerPerEnemy, and stops as soon as the next pick would exceed the remaining budget - a budget
/// is a ceiling, never a target to hit exactly.
/// </summary>
[System.Serializable]
public struct EncounterBudget
{
    public float frontlinePower;
    public float backlinePower;
    public float anyPower;

    public float bossFrontlinePower;
    public float bossBacklinePower;

    public float reinforcementPower;

    public float maxPowerPerEnemy;
    public float maxPowerPerWave;

    public int waveInterval;
    public int waveIntervalJitter;

    [Tooltip("Empty draws from the whole EnemyRegistry. Non-empty restricts every bucket above to just "
             + "these prefabs - for a thematically narrow level (all bandits, say) without needing a "
             + "second registry.")]
    public List<GameObject> poolFilter;
}

/// <summary>
/// One level's worth of board setup: which enemies stand where, and where the party is placed.
///
/// Deliberately does not say *who* is in the party - RunState owns the roster because it is chosen
/// once at the start of a run and carries across every level in that run, while a placement is
/// specific to this one level. A level that said "the party is Knight and Mage" would have to be
/// re-authored the moment the roster changes; a level that only says "party member 0 starts at
/// (2,1)" never has to change no matter who runs it.
/// </summary>
[CreateAssetMenu(menuName = "Level Data")]
public class LevelData : ScriptableObject
{
    [Tooltip("Enemies spawned when the battle starts.")]
    [SerializeField] private List<EnemyPlacement> enemies = new();

    [Tooltip("Reinforcements spawned partway through the battle, keyed by round.")]
    [SerializeField] private List<EnemyWave> waves = new();

    [Tooltip("Power budgets EncounterRoller draws from at battle start, on top of the enemies/waves "
             + "above. Leave every field at 0 for a fully hand-tailored level - see EncounterBudget.")]
    [SerializeField] private EncounterBudget encounterBudget;

    [Tooltip("Where the run's party is placed, index for index against RunState's roster. A party "
             + "with more members than this list has spawn cells for requests the last authored cell "
             + "for the extras - GridManager.NearestFreeSpawnTile then fans them out from there, same "
             + "as an over-full enemy wave. Tools/Level/Top Up Party Spawn Cells adds cells up to the "
             + "roster's max party size in one pass.")]
    [OneBasedCell]
    [SerializeField] private List<Vector2Int> partySpawnCells = new();

    [Tooltip("Board size for this level. Leave at 0,0 to use GridManager's own width and height.")]
    [SerializeField] private Vector2Int boardSize;

    [Tooltip("Turns the player has to survive. Reaching 0 is the win.")]
    [SerializeField] private int turnsToSurvive = 10;

    [Tooltip("Cards held at the start of each turn. The old hand is discarded first, so unplayed cards "
             + "do NOT carry over - see BattleManager.TurnStart. Innate cards return to hand before "
             + "the draw and take up slots, so they count against this rather than adding to it.")]
    [SerializeField] private int handSize = 5;

    [Tooltip("Default odds and choice count for a reward dropped on this level. A Character with its "
             + "own LootTable overrides this for its own drop - see Character.LootTable.")]
    [SerializeField] private LootTable lootTable;

    [Tooltip("Odds and choice count for the reward each living hero is offered once this level is "
             + "cleared. Separate from lootTable above, which is only what enemies drop mid-fight - a "
             + "later level can offer better clear-reward odds without touching what its enemies drop. "
             + "Unassigned falls back to LootManager's fallbackTable.")]
    [SerializeField] private LootTable clearRewardTable;

    [Tooltip("Floor and wall art this level may use. One is picked at random when the battle starts, "
             + "so the same encounter can read as a different place each run. Leave empty and the "
             + "board builds with no floor art at all - playable, but invisible under the tiles.")]
    [SerializeField] private List<TileSetData> tileSets = new();

    public IReadOnlyList<EnemyPlacement> Enemies => enemies;

    public IReadOnlyList<EnemyWave> Waves => waves;

    public EncounterBudget EncounterBudget => encounterBudget;

    public IReadOnlyList<Vector2Int> PartySpawnCells => partySpawnCells;

    /// <summary>
    /// Zero means "use GridManager's serialized default", not "no board". It has to: a LevelData
    /// authored before this field existed deserializes it to (0,0), and Level1 is exactly that asset.
    /// Same hazard as RangeShape.Anywhere being 0.
    /// </summary>
    public Vector2Int BoardSize => boardSize;

    public int TurnsToSurvive => turnsToSurvive;

    public int HandSize => handSize;

    public LootTable LootTable => lootTable;

    public LootTable ClearRewardTable => clearRewardTable;

    public IReadOnlyList<TileSetData> TileSets => tileSets;

    /// <summary>
    /// One of this level's tilesets, chosen with `roll`, or null when none is authored or usable.
    ///
    /// Here rather than in BattleManager because "which of my tilesets" is a question about the
    /// level, and here rather than in BoardVisuals because the list is the level's to own - the same
    /// split that keeps BoardSize on LevelData while the board that gets built is GridManager's.
    ///
    /// Unusable sets are filtered rather than rolled and rejected, so a level with one finished set
    /// and one half-authored one always draws the finished one instead of a blank board half the time.
    /// </summary>
    public TileSetData PickTileSet(System.Random roll)
    {
        List<TileSetData> usable = new();

        foreach (TileSetData set in tileSets)
        {
            // `!=` not `??`: a deleted asset is a Unity fake-null. See CLAUDE.md.
            if (set != null && set.IsUsable) { usable.Add(set); }
        }

        if (usable.Count == 0) { return null; }

        return usable[roll.Next(usable.Count)];
    }

    /// <summary>
    /// The soonest wave after `turnsElapsed`, or false when none remain - see NextWavePanel, which
    /// drives the HUD strip previewing it. `waves` is neither sorted nor deduplicated (SpawnDueWaves
    /// itself does not assume either), so this scans the whole list and collects every wave sharing the
    /// lowest qualifying turn rather than just the first match.
    /// </summary>
    public bool TryNextWave(int turnsElapsed, out int turn, out List<EnemyPlacement> enemies)
    {
        turn = int.MaxValue;
        enemies = null;

        foreach (EnemyWave wave in waves)
        {
            if (wave.turn <= turnsElapsed || wave.enemies == null || wave.enemies.Count == 0) { continue; }

            if (wave.turn > turn) { continue; }

            if (wave.turn < turn)
            {
                turn = wave.turn;
                enemies = new List<EnemyPlacement>();
            }

            enemies.AddRange(wave.enemies);
        }

        return enemies != null;
    }
}
