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

    /// <summary>
    /// Freezing the holder while it is committed to this card knocks the card out for `magnitude`
    /// turns - see BattleManager's frozen branch in EnemyResolve and Card.Interrupt.
    ///
    /// The point is that Frozen stops being "one lost swing" against a boss whose threat is a single
    /// telegraphed card. Denying the Evil Knight's Burst Call for a turn is worth far more than
    /// denying it a 6-damage poke, and this is what lets a player read the intent row and spend a
    /// Freeze on the summon specifically.
    ///
    /// Only the card the holder had actually committed to is hit, never every Interruptible card in
    /// hand: the lock is on the plan (Character.LockedPlan), so a boss cycling debuffs loses the one
    /// it was winding up and keeps the rest. Carries its own countdown rather than borrowing
    /// Cooldown's, so a card can be interruptible without also being on a cooldown of its own.
    /// </summary>
    Interruptible = 5,
}
