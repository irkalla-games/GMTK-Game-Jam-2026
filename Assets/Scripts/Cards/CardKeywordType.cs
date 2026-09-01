/// <summary>
/// Every reusable tag a card can carry. Values are written into .asset files, so append only, never
/// reorder - the same rule as StatusType and RangeShape.
/// </summary>
public enum CardKeywordType
{
    None = 0,

    /// Always in hand, independent of the draw. Never enters the draw or discard pile.
    Innate = 1,

    /// Unusable for `magnitude` turns after being played. Ready immediately the first time - see
    /// Dormant for a keyword that locks a fresh copy instead.
    Cooldown = 2,

    /// Unusable for `magnitude` turns from the moment this copy is created - battle start for an
    /// authored deck card, the turn it is summoned for a summon's card, the turn it is picked up for
    /// a reward. Never re-locks after that; pair with Cooldown for something that is also unusable
    /// for a while after each play.
    Dormant = 3,

    /// <summary>
    /// Returns to hand the instant it resolves, instead of going to the discard pile.
    ///
    /// Innate's impatient cousin, and the two compose: Innate says "you have this at the start of
    /// every turn", Rebound says "you still have it a moment after playing it". A card carrying both
    /// is playable as many times in one turn as its cost allows - which for a 0-cost card is
    /// unlimited, so pair Rebound with a real cost or a Cooldown unless that is the point.
    ///
    /// Deliberately not spelled as "does not discard": Innate already means that and behaves quite
    /// differently, waiting for RestoreInnateCards at the next turn start. See Character.Discard,
    /// where the two part ways.
    /// </summary>
    Rebound = 4,
}
