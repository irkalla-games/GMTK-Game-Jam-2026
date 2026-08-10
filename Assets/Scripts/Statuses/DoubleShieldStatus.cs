/// <summary>
/// Doubles the next shield gained, then spends a charge. No duration - it waits for the gain, same
/// shape as DoubleNextAttackStatus for the deal-damage side.
///
/// Worth less than it looks: Shield wipes itself at the start of every turn (ShieldStatus.OnTurnStart),
/// so this only pays off when it is spent on a gain in the same turn you need the shield up. And it
/// spends on the carrier's *next* gain, not their best one - stack it before Shield 3 and Shield 7
/// arrive in that order, and it burns on the 3. Same sequencing trap DoubleNextAttackStatus documents.
/// </summary>
public class DoubleShieldStatus : StatusEffect
{
    public DoubleShieldStatus(int stacks, int turnsRemaining)
        : base(StatusType.DoubleShield, stacks, turnsRemaining) { }

    public override ShieldInfo OnGainShield(ShieldInfo info)
    {
        if (stacks <= 0) { return info; }

        // Looking is free. Only a real gain spends the charge - see ShieldInfo.consumeCharges.
        if (info.consumeCharges) { stacks--; }

        return info.WithAmount(info.amount * 2);
    }

    public override string Describe() => stacks > 1 ? $"Double Shield x{stacks}" : "Double Shield";
}
