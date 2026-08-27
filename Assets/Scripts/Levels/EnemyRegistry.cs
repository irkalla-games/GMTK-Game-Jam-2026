using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One entry in EnemyRegistry: a body EncounterRoller may consider, and whether it may actually be
/// drawn at random.
/// </summary>
[System.Serializable]
public struct EnemyRegistryEntry
{
    [Tooltip("Prefab with a Character on it. Power, role and boss-ness all come from there - see "
             + "Character.PowerLevel/BattleRole/IsBoss.")]
    public GameObject prefab;

    [Tooltip("Whether EncounterRoller may draw this body at random. Off for Assets/Prefabs/Allies - "
             + "they are listed here because summon chains reference them, but a summoned ally is never "
             + "itself a random encounter pick.")]
    public bool eligibleForRandomDraw;
}

/// <summary>
/// Every body EncounterRoller can know about: what exists, and which of those may be drawn at random.
/// Deliberately does not duplicate power/role/boss - those live on the prefab's own Character, one
/// obvious place to tune a goblin, and it is the goblin. Rebuilt by Tools > Enemies > Rebuild Enemy
/// Registry (EnemyRegistryGenerator), which scans Assets/Prefabs/{Enemies,Bosses,Allies} the same way
/// Tools/EnemySheet's Get-DiscoveredRoster does - never hand-edited.
/// </summary>
[CreateAssetMenu(menuName = "Enemy Registry")]
public class EnemyRegistry : ScriptableObject
{
    [SerializeField] private List<EnemyRegistryEntry> entries = new();

    public IReadOnlyList<EnemyRegistryEntry> Entries => entries;
}
