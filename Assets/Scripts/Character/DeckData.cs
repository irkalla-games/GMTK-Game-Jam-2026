using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One named starting deck a player can pick at the character-select screen - "Knight: Aggressive" as
/// opposed to "Knight: Defensive". A CharacterOption lists the decks its character may start with;
/// RunManager copies this deck's cards into that hero's PartyMember record exactly the way it already
/// copies Character.AuthoredDeck, so a DeckData is authored the same way a prefab's inline deck is,
/// just detachable from any one prefab and offerable to whichever CharacterOption lists it.
///
/// Also the unit deck unlocks are tracked against - see DeckUnlocks. A deck a player has not earned yet
/// still exists as an asset and can be authored and playtested; Locked only governs whether the select
/// screen offers it.
/// </summary>
[CreateAssetMenu(menuName = "Character/Deck Data")]
public class DeckData : ScriptableObject
{
    [SerializeField] private string displayName;

    [TextArea]
    [SerializeField] private string description;

    [Tooltip("Which character(s) this deck is meant for - gates which CharacterOption slots may offer "
             + "it. Any means it may be offered regardless of the character chosen for that slot.")]
    [SerializeField] private CharacterClass forClass;

    [Tooltip("The deck as authored. Copied into the run's PartyMember record at RunManager.Begin, "
             + "never handed out live - the same reason Character.AuthoredDeck is read-only.")]
    [SerializeField] private List<CardData> cards = new();

    [Tooltip("Off by default on purpose, same reasoning as RangeShape.Anywhere being 0: a DeckData "
             + "authored before this field existed must deserialize to \"playable\", not \"hidden\". "
             + "On: the select screen shows this deck greyed out and unpickable until DeckUnlocks says "
             + "otherwise.")]
    [SerializeField] private bool locked;

    [Tooltip("Stable id DeckUnlocks saves against. Leave blank to fall back to this asset's name - but "
             + "renaming the asset later then orphans anyone's save, so a deck meant to ship locked "
             + "should have this set explicitly.")]
    [SerializeField] private string unlockId;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    public string Description => description;

    public CharacterClass ForClass => forClass;

    public IReadOnlyList<CardData> Cards => cards;

    public bool Locked => locked;

    public string UnlockId => string.IsNullOrWhiteSpace(unlockId) ? name : unlockId;

    /// Whether `characterClass` may start with this deck - Any on either side always matches, same
    /// rule CardData.CanBeUsedBy applies between a card and a character.
    public bool AvailableTo(CharacterClass characterClass) =>
        forClass == CharacterClass.Any
        || characterClass == CharacterClass.Any
        || (forClass & characterClass) != 0;
}
