/// <summary>
/// How good a drop or a card reward is. Shared by CardData and by loot itself, so "a Rare drop offers
/// Rare cards" is a direct enum comparison with no mapping table in between.
///
/// Common is 0 on purpose - the 21 card assets authored before this field existed deserialize to it,
/// which is the correct default for all of them. Same reasoning as RangeShape.Anywhere being 0. Append
/// new tiers at the end; the ints are written into every CardData and LootTable asset on disk.
/// </summary>
public enum Rarity
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Legendary = 3,

    /// Never rolled by a LootTable and never offered as a reward - for cards that exist only to be
    /// held by an enemy (EnemyArrow, EnemySlash) or that would be a trap as a reward (an Innate card
    /// that returns to hand for free every turn).
    NotOffered = 4,
}

/// <summary>
/// Widening rule for a thin reward pool - see LootManager. One place for it rather than repeating the
/// switch at every call site.
/// </summary>
public static class RarityExtensions
{
    /// The next tier down, or Common again once there is nowhere lower to fall back to.
    public static Rarity Lower(this Rarity rarity) => rarity switch
    {
        Rarity.Legendary => Rarity.Rare,
        Rarity.Rare => Rarity.Uncommon,
        Rarity.Uncommon => Rarity.Common,
        _ => Rarity.Common,
    };
}
