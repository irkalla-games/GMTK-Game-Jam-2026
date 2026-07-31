/// <summary>
/// Every reusable tag a card can carry. Values are written into .asset files, so append only, never
/// reorder - the same rule as StatusType and RangeShape.
/// </summary>
public enum CardKeywordType
{
    None = 0,

    /// Always in hand, independent of the draw. Never enters the draw or discard pile.
    Innate = 1,

    /// Unusable for `magnitude` turns after being played, and for `magnitude` turns from battle start
    /// before its first use.
    Cooldown = 2,
}
