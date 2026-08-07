using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A campaign as authored: which levels you play, in order, and who you start with. The authoring half
/// of a run - RunManager is the runtime half that remembers how far through this you are and what the
/// party has become since. Same split as CardData/Card and AuraData/Aura.
///
/// Being an asset rather than a list on a manager is what lets there be more than one: a short
/// two-level campaign to test progression against, and the real one, swapped by dragging a different
/// asset onto the Main Menu.
/// </summary>
[CreateAssetMenu(menuName = "Run Data")]
public class RunData : ScriptableObject
{
    [Tooltip("Levels in the order they are played. Finishing the last one ends the run.")]
    [SerializeField] private List<LevelData> levels = new();

    [Tooltip("Prefabs the run starts with, each needing a Character on it. Every level instantiates "
             + "its own copies and places them at that level's party spawn cells.")]
    [SerializeField] private List<GameObject> startingParty = new();

    [Tooltip("On: damage taken in a level follows the party into the next one. Off: everyone starts "
             + "each level at full health. Death is permanent either way.")]
    [SerializeField] private bool carryDamageBetweenLevels;

    public IReadOnlyList<LevelData> Levels => levels;

    /// <summary>
    /// GameObject rather than Character, even though a Character is what every reader wants.
    ///
    /// A Component-typed field is the nicer thing on a MonoBehaviour - CardViewer cardPrefab is one,
    /// and dragging a prefab onto it works. On a ScriptableObject it does not: the Inspector's object
    /// picker lists only *main* assets, and a prefab's main asset is its GameObject, so a
    /// Character-typed field on an asset offers an empty picker and there is no way to author the
    /// party at all. Every prefab reference held by an asset in this project is a GameObject for the
    /// same reason - see EnemyPlacement.prefab.
    ///
    /// RunManager.Begin resolves each one to its Character exactly once, so the cost is a single
    /// GetComponent at the start of a run and nothing downstream ever sees a GameObject.
    /// </summary>
    public IReadOnlyList<GameObject> StartingParty => startingParty;

    /// <summary>
    /// Off by default on purpose. A bool deserializes to false in an asset authored before the field
    /// existed, so false has to be the behaviour we want - and heal-up-between-levels is it for now.
    /// Health is tracked and written back either way, so turning attrition on is one checkbox and no
    /// code change. Same reasoning as RangeShape.Anywhere being 0.
    /// </summary>
    public bool CarryDamageBetweenLevels => carryDamageBetweenLevels;
}
