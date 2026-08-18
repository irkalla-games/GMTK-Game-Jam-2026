using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One chosen hero: which prefab, and which of its starting decks was picked at the select screen. The
/// select-screen counterpart to EnemyPlacement - a hero placed by a player's choice rather than a
/// level's authoring.
///
/// prefab is a GameObject for the same Inspector-picker reason StartingParty is - see its tooltip.
/// </summary>
[System.Serializable]
public struct PartyEntry
{
    public GameObject prefab;

    [Tooltip("Null falls back to the prefab's own authored deck (Character.AuthoredDeck) - the same "
             + "thing choosing no deck at the select screen means.")]
    public DeckData deck;
}

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
             + "its own copies and places them at that level's party spawn cells.\n\n"
             + "This is the standalone-play fallback only - see StartingHeroes for what a real run "
             + "through the Main Menu's character select actually uses. Kept, and kept working, so "
             + "opening Game.unity directly and pressing Play (EnsureRun) still needs no menu.")]
    [SerializeField] private List<GameObject> startingParty = new();

    [Tooltip("Starting-deck override for StartingParty, index for index - the standalone-play "
             + "counterpart to PartyEntry.deck. A shorter list, or a null/empty entry, falls back to "
             + "that index's prefab's own authored deck, same meaning PartyEntry.deck == null already "
             + "carries into StartingHeroes. Lets Game.unity be played directly with a chosen starter "
             + "deck instead of always the prefab's own.")]
    [SerializeField] private List<DeckData> startingPartyDecks = new();

    [Tooltip("The party as chosen at the character-select screen: who, and which starting deck each "
             + "took. RunManager.Begin prefers this over StartingParty whenever it is non-empty - see "
             + "that method for the exact fallback order.")]
    [SerializeField] private List<PartyEntry> startingHeroes = new();

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

    /// Deck override for StartingParty, index for index - see RunManager.BeginFromStartingParty for how
    /// a missing or null entry falls back to that prefab's own authored deck.
    public IReadOnlyList<DeckData> StartingPartyDecks => startingPartyDecks;

    /// The party chosen at character select, or empty for an asset authored the old way (StartingParty
    /// only) - see RunManager.Begin for how the two are reconciled.
    public IReadOnlyList<PartyEntry> StartingHeroes => startingHeroes;

    /// <summary>
    /// Off by default on purpose. A bool deserializes to false in an asset authored before the field
    /// existed, so false has to be the behaviour we want - and heal-up-between-levels is it for now.
    /// Health is tracked and written back either way, so turning attrition on is one checkbox and no
    /// code change. Same reasoning as RangeShape.Anywhere being 0.
    /// </summary>
    public bool CarryDamageBetweenLevels => carryDamageBetweenLevels;

    /// <summary>
    /// Builds a RunData entirely in memory, for a run whose party was chosen at the select screen
    /// rather than dragged onto an asset in the Inspector. This is the seam a future level generator
    /// plugs into - only `levels` needs to come from somewhere else, this factory itself never changes.
    ///
    /// This looks like it breaks "never mutate a ScriptableObject at runtime" and does not - that rule
    /// is about writes landing on an asset already saved to disk. CreateInstance produces an object
    /// with no asset path, so there is nothing on disk for a later write to corrupt.
    /// HideAndDontSave is belt-and-braces on top of that: it keeps this instance out of the
    /// Resources.UnloadUnusedAssets sweep a scene load can trigger, and out of the Hierarchy/Project
    /// windows, since it is not asset-backed and has nowhere to be shown.
    /// </summary>
    public static RunData CreateRuntime(
        IEnumerable<LevelData> levels, IEnumerable<PartyEntry> heroes, bool carryDamageBetweenLevels)
    {
        RunData run = CreateInstance<RunData>();
        run.hideFlags = HideFlags.HideAndDontSave;
        run.levels = new List<LevelData>(levels);
        run.startingHeroes = new List<PartyEntry>(heroes);
        run.carryDamageBetweenLevels = carryDamageBetweenLevels;
        return run;
    }
}
