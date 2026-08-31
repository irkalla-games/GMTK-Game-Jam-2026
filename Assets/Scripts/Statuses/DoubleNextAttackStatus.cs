/// <summary>
/// Doubles one attack, then spends a charge. No duration - it waits for the swing.
///
/// Only doubles the card's own number; Strength is added on top by StrengthStatus, undoubled - see
/// Order, which is what pins this ahead of Strength regardless of which was granted first.
///
/// It spends on the carrier's *next* attack, not their best one, which is the trap: buff the Knight
/// and if they play Quick Attack (3) before Slash (9) the doubling burns for +3. That is real
/// sequencing skill, but it needs a visible icon or the first accidental waste reads as a bug.
/// </summary>
public class DoubleNextAttackStatus : StatusEffect
{
    public DoubleNextAttackStatus(int stacks) : base(StatusType.DoubleNextAttack, stacks) { }

    /// Before Strength (StrengthStatus.Order, the default 0) so the multiplier only ever sees the
    /// card's own base number - see the class doc.
    public override int Order => -10;

    public override DamageInfo OnDealDamage(DamageInfo info)
    {
        if (stacks <= 0) { return info; }

        // Looking is free. Only an actual swing spends the charge - see DamageInfo.consumeCharges.
        if (info.consumeCharges) { stacks--; }

        return info.WithAmount(info.amount * 2);
    }

    public override string Describe() => stacks > 1 ? $"Double Attack x{stacks}" : "Double Attack";
}
