/// <summary>
/// Unconditional flat damage adjustment - Whetstone's "+1 to every attack", or a piece of armour
/// reducing every hit taken by a flat amount. Deliberately unlike PotencyStatus, which only reacts while
/// the carrier currently holds a charge of some specific subject status: equipment worn all the time
/// should not need Strength or Block on the board to do anything, so this always applies.
///
/// Never touches consumeCharges - there is no charge here to spend, so a preview (a damage tooltip, an
/// enemy brain scoring a move) sees the same number a real hit would land.
///
/// Serves two owners now. Equipment leaves `type` at StatusType.None, for the same reason
/// GainBonusStatus does: an equipment-granted modifier is not meant to render as its own status chip.
/// A totem passes FlatDamageDealt or FlatDamageTaken, so the aura can name itself to the tooltip -
/// those two are in StatusTypes.IsTotemOnly, so they stay out of Displayable and still draw no chip.
/// One class, because the rule is identical either way; only who is asking differs.
/// </summary>
public class FlatDamageStatus : StatusEffect
{
    private readonly int outgoingBonus;
    private readonly int incomingReduction;

    /// `type` defaults to None so every existing equipment caller is unchanged - see
    /// DamageBonusModifier, which wants exactly that. AuraData.CreateEffect passes a real type.
    public FlatDamageStatus(int outgoingBonus, int incomingReduction, StatusType type = StatusType.None)
        : base(type, 1)
    {
        this.outgoingBonus = outgoingBonus;
        this.incomingReduction = incomingReduction;
    }

    public override DamageInfo OnDealDamage(DamageInfo info)
    {
        if (outgoingBonus == 0 || info.amount <= 0) { return info; }

        // Only worsens a real hit, never conjures damage out of a 0 - a card that missed its own
        // refusal check and resolved for 0 anyway should stay at 0, not become "just the bonus".
        return info.WithAmount(info.amount + outgoingBonus);
    }

    public override DamageInfo OnTakeDamage(DamageInfo info)
    {
        if (incomingReduction == 0 || info.negated) { return info; }

        return info.Reduced(incomingReduction);
    }

    public override string Describe()
    {
        if (outgoingBonus != 0 && incomingReduction != 0)
        {
            return $"+{outgoingBonus} damage dealt, -{incomingReduction} damage taken";
        }

        if (outgoingBonus != 0) { return $"+{outgoingBonus} damage dealt"; }
        if (incomingReduction != 0) { return $"-{incomingReduction} damage taken"; }
        return "No change";
    }
}
