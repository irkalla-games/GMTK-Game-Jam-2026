/// <summary>
/// Doubles one attack, then spends a charge. No duration - it waits for the swing.
///
/// Only doubles the card's own number; Strength is added on top by StrengthStatus. That ordering is
/// deliberate where it holds, because it keeps this buff's value tied to the card it lands on rather
/// than scaling with however much Strength has piled up. Under FIFO hooks it only holds when this
/// status was applied first - see Status.
///
/// It spends on the carrier's *next* attack, not their best one, which is the trap: buff the Knight
/// and if they play Quick Attack (3) before Slash (9) the doubling burns for +3. That is real
/// sequencing skill, but it needs a visible icon or the first accidental waste reads as a bug.
/// </summary>
public class DoubleNextAttackStatus : StatusEffect
{
    public DoubleNextAttackStatus(int stacks) : base(StatusType.DoubleNextAttack, stacks) { }

    public override DamageInfo OnDealDamage(DamageInfo info)
    {
        if (stacks <= 0) { return info; }

        // Looking is free. Only an actual swing spends the charge - see DamageInfo.consumeCharges.
        if (info.consumeCharges) { stacks--; }

        return info.WithAmount(info.amount * 2);
    }

    public override string Describe() => stacks > 1 ? $"Double Attack x{stacks}" : "Double Attack";
}
