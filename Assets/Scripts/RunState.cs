using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Who is in the party for the current run - nothing else. A run is one attempt at the game:
/// character-select picks the roster once, it survives every scene load between levels because this
/// is a PersistantSingleton, and it resets on death or after the final boss.
///
/// Holds prefabs, not live instances. Each level's BattleManager instantiates its own copies from
/// this list and places them via LevelData.PartySpawnCells - the same split LevelData.Enemies and
/// SpawnEnemies already use for enemies. Nothing here needs to survive a scene load by itself; only
/// the choice of who is in the party does.
/// </summary>
public class RunState : PersistantSingleton<RunState>
{
    [Tooltip("Used until something calls SetParty - lets a level run stand-alone in the Editor "
             + "without going through character-select first, which does not exist yet.")]
    [SerializeField] private List<Character> debugDefaultParty = new();

    private List<Character> party;

    /// The run's roster, prefabs only - see SpawnParty on BattleManager for where these get
    /// instantiated. Falls back to the debug roster until character-select calls SetParty.
    public IReadOnlyList<Character> Party =>
        party != null && party.Count > 0 ? party : debugDefaultParty;

    /// Hook this up to character-select. Replaces the roster for the run about to start.
    public void SetParty(IEnumerable<Character> chosen)
    {
        party = new List<Character>(chosen);
    }
}
