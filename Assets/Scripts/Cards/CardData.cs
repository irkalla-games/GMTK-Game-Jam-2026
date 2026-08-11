using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One keyword this card is authored with, and its magnitude. Meaning depends on type - Cooldown and
/// Dormant both read magnitude as a length in turns, Innate ignores it. Lives here rather than as a
/// fixed set of bool fields so a future keyword only needs a new CardKeywordType, not a new field on
/// every card.
/// </summary>
[System.Serializable]
public struct CardKeywordEntry
{
    public CardKeywordType type;

    [Tooltip("Meaning depends on type. Cooldown: turns before it may be played again after each use "
             + "(ready the first time). Dormant: turns before it may be played the first time, counted "
             + "from when this copy was created. Innate: unused.")]
    public int magnitude;
}

/// <summary>
/// One effect this card resolves, and where and how wide it lands. The binding lives here rather than
/// on the CardEffect asset because CardEffect assets are shared - Damage 5.asset is reused by Slash,
/// Quick Attack and Bash, so a radius or a Source aim baked into the asset would silently retarget all
/// of them. A card that wants one of its effects wide just gives its own entry a non-Single area; the
/// same Damage 5.asset stays single-target everywhere else it's referenced.
/// </summary>
[System.Serializable]
public struct CardEffectEntry
{
    public CardEffect effect;

    [Tooltip("Where this effect lands. Source aims it at the caster instead of the clicked tile, "
             + "which is how one card can damage an enemy and buff its own player.")]
    public EffectTarget aimsAt;

    [Tooltip("The footprint around the aim tile this effect covers. Single (the default) is one tile, "
             + "exactly like today.")]
    public AreaShape area;
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

    [field: Tooltip("How good this card is as a reward. NotOffered keeps it out of every LootTable and "
                    + "reward panel - for enemy-only cards and cards that would be a trap as a reward.")]
    [field: SerializeField] public Rarity rarity { get; private set; }

    [field: Tooltip("Themes this card carries, for LootTable.tagWeights to bias on - a poison skeleton "
                    + "favours cards tagged Poison. A card can carry more than one, or none.")]
    [field: SerializeField] public List<CardTag> tags { get; private set; } = new();

    [field: Tooltip("Keeps this card out of every reward pool while leaving its rarity alone - for a "
                    + "card every deck already starts with, like Move. Rarity.NotOffered is the other "
                    + "half of this and stays for enemy-only cards; this one is the 'it is a real "
                    + "Common, just not a reward' case.")]
    [field: SerializeField] public bool excludeFromRewards { get; private set; }

    [field: SerializeField] public string description { get; private set; }
    [field: SerializeField] public Sprite image { get; private set; }

    // Legacy input for CardEffectEntryMigration only. Every card should have an equivalent, same-order
    // effectEntries list below - read that one, not this. Kept only until it is deleted in a
    // follow-up once the migration is verified. HideInInspector needs the field: target, or Unity's
    // Inspector (which reads the backing field, not the property) won't honour it.
    [field: HideInInspector]
    [field: SerializeField] public List<CardEffect> effects { get; private set; }

    [field: Tooltip("What this card actually does when played, one entry per effect: the effect asset "
                    + "itself, where it aims, and how wide it lands.")]
    [field: SerializeField] public List<CardEffectEntry> effectEntries { get; private set; } = new();

    [field: Tooltip("How this card's actions look when played. Left empty, every action animates "
                    + "exactly as it would by itself - a plain swing, a plain cast - which is what "
                    + "every card authored before this field existed keeps doing. Only needed to "
                    + "redirect a cue (Fireball casting instead of swinging) or to send a projectile.")]
    [field: SerializeField] public CardAnimation animation { get; private set; }

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
    ///
    /// requiredClass is a mask now (see CharacterClass), so a character's single class only needs to
    /// share one bit with it, not equal it exactly - that is what lets one card belong to more than
    /// one class.
    /// </summary>
    public bool CanBeUsedBy(Character character) =>
        requiredClass == CharacterClass.Any
        || character == null
        || character.Class == CharacterClass.Any
        || (requiredClass & character.Class) != 0;

    //public string CardName => cardName;
    //public int Cost => cost;
    //public string Description => description;
    // public Sprite Image => image;

    // public List<CardEffect> Effects => effects;

}
