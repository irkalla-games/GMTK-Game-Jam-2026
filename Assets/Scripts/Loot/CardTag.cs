/// <summary>
/// A theme a card carries, authored as a list on CardData rather than a single category - a card can
/// be both Attack and Poison. This is the axis LootTable.tagWeights biases on, which is what lets a
/// poison skeleton favour poison cards without listing them by name: every poison card added later is
/// automatically in its pool.
///
/// A List&lt;CardTag&gt; rather than a [Flags] enum - Unity's Inspector serializes a list cleanly with no
/// custom drawer, where flags need one to render as checkboxes instead of a dropdown.
///
/// None is 0 so an unauthored card's tag list defaults to empty rather than to a real tag. Append-only,
/// same hazard as Rarity - the ints are written into every CardData asset on disk.
/// </summary>
public enum CardTag
{
    None = 0,
    Attack = 1,
    Defence = 2,
    Movement = 3,
    Poison = 4,
    Fire = 5,
    Summon = 6,
    Healing = 7,
}
