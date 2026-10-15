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

        // A negative reduction is a drawback - Deepcoral Signet's "take 1 more". Same rule as the
        // outgoing bonus above: it worsens a real hit, never turns a 0 into a 1.
        if (incomingReduction < 0 && info.amount <= 0) { return info; }

        return info.Reduced(incomingReduction);
    }

    public override string Describe() => DescribeAmounts(outgoingBonus, incomingReduction);

    /// One wording for both owners - this status's tooltip and DamageBonusModifier's equipment line -
    /// so a negative reduction reads "+1 damage taken" in both, never "--1".
    public static string DescribeAmounts(int outgoing, int incomingReduction)
    {
        string dealt = outgoing != 0 ? $"{Signed(outgoing)} damage dealt" : null;
        string taken = incomingReduction != 0 ? $"{Signed(-incomingReduction)} damage taken" : null;

        if (dealt != null && taken != null) { return $"{dealt}, {taken}"; }
        return dealt ?? taken ?? "No change";
    }

    private static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();
}
