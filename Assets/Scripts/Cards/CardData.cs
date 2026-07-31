using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One keyword this card is authored with, and its magnitude. Meaning depends on type - Cooldown reads
/// magnitude as its length in turns, Innate ignores it. Lives here rather than as a fixed set of bool
/// fields so a future keyword only needs a new CardKeywordType, not a new field on every card.
/// </summary>
[System.Serializable]
public struct CardKeywordEntry
{
    public CardKeywordType type;

    [Tooltip("Meaning depends on type. Cooldown: turns before it may be played, and again after each "
             + "use. Innate: unused.")]
    public int magnitude;
}

/// <summary>
/// Base for every card asset. This is the card *type* - it is shared by every copy in a deck, so
/// nothing here may be written to at runtime (in the Editor those writes persist into the .asset
/// file). Per-copy and per-run state belongs on Card.
///
/// The fields are private + read-only properties for that reason: the Inspector can author them,
/// code can't assign them. They can't be const or readonly - Unity's serializer skips both, and the
/// field would disappear from the Inspector.
///
/// Shared presentation lives here; the numbers a card actually does live on the subclass, one per
/// card (see Bash).
/// </summary>
[CreateAssetMenu(menuName = "CardData")]
public class CardData : ScriptableObject
{

    [field: SerializeField] public string cardName { get; private set; }
    [field: SerializeField] public int cost { get; private set; }

    [field: Tooltip("Which tiles this card may be aimed at, measured from the acting character's tile. "
                    + "Left at Anywhere, any tile on the board is legal.")]
    [field: SerializeField] public TargetRange range { get; private set; }

    [field: Tooltip("Which character may hold this card. Any means everyone. Enforced when a deck is "
                    + "built, not when the card is played.")]
    [field: SerializeField] public CharacterClass requiredClass { get; private set; }

    [field: SerializeField] public string description { get; private set; }
    [field: SerializeField] public Sprite image { get; private set; }
    [field: SerializeField] public List<CardEffect> effects { get; private set; }

    [field: Tooltip("Reusable tags this card carries - Innate, Cooldown, and whatever gets added later.")]
    [field: SerializeField] public List<CardKeywordEntry> keywords { get; private set; } = new();

    /// <summary>
    /// Also the hook a post-combat reward or draft screen filters on - which is the actual reason
    /// class restriction exists in a deckbuilder.
    ///
    /// Any means "no restriction" on both sides. A card marked Any goes in anybody's deck, and a
    /// character marked Any may hold anything - which is what unclassed things like goblins are.
    /// Reading it as a restriction in only one direction quietly emptied every enemy deck of every
    /// card that happened to be authored for a class.
    /// </summary>
    public bool CanBeUsedBy(Character character) =>
        requiredClass == CharacterClass.Any
        || character == null
        || character.Class == CharacterClass.Any
        || character.Class == requiredClass;

    //public string CardName => cardName;
    //public int Cost => cost;
    //public string Description => description;
    // public Sprite Image => image;

    // public List<CardEffect> Effects => effects;

}
