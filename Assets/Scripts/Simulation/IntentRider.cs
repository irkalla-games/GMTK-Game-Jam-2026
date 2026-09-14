/// <summary>
/// A secondary effect an enemy's committed card would apply alongside (or instead of) its damage -
/// what the intent readout badges beside the primary icon. Root, Vulnerable and Weaken are the
/// common case; Card.OutgoingRiders is what walks a card's effect entries into a list of these.
///
/// Two independent keys because a rider is one of two authoring shapes, never both: `status` for an
/// ApplyStatusEffect entry (StatusIcons already has art for all thirty), `kind` for the handful of
/// named effects worth telegraphing that are not statuses at all - Heal, Draw, Push. Exactly one of
/// the two is ever non-None on a given instance; the reader picks its registry by which key is set.
///
/// Append new IntentRiderKind values, never reorder - same rule as IntentKind and StatusType, and
/// for the same reason: these values are written into IntentIcons.asset.
/// </summary>
public enum IntentRiderKind
{
    /// Not a named rider - this slot in the enum is here only so StatusType.None and
    /// IntentRiderKind.None can both mean "the other key is the real one" with the same shape.
    None = 0,

    Heal = 1,

    Draw = 2,

    Push = 3,
}

/// <summary>
/// One badge's worth of data: what it shows, how big the number beside it is, and whether it lands
/// on the enemy itself rather than on you. A readonly struct - built fresh every refresh by
/// Card.OutgoingRiders, never stored, so there is nothing here worth mutating in place.
/// </summary>
public readonly struct IntentRider
{
    /// The status this rider applies, or StatusType.None if this rider is a named effect instead -
    /// see `kind`. Never both non-None at once.
    public readonly StatusType status;

    /// The named effect this rider applies, or IntentRiderKind.None if this rider is a status
    /// instead - see `status`. Never both non-None at once.
    public readonly IntentRiderKind kind;

    /// Already run through ActionContext.Amount, the same path Card.OutgoingDamage sums its own
    /// numbers through - so a Weaken badge's stack count and the damage number beside it can never
    /// disagree about what amountDelta/amountPercent did to the authored value. 0 means the badge
    /// shows a bare glyph with no digit (Push has no count).
    public readonly int amount;

    /// True when this rider's entry.aimsAt is EffectTarget.Source - the enemy is applying this to
    /// itself, not to whatever it is attacking. The intent readout gives a self-aimed rider a
    /// different plate colour so "it curses you" and "it buffs itself" never look the same badge.
    public readonly bool selfTargeted;

    public IntentRider(StatusType status, IntentRiderKind kind, int amount, bool selfTargeted)
    {
        this.status = status;
        this.kind = kind;
        this.amount = amount;
        this.selfTargeted = selfTargeted;
    }
}
