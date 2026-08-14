/// <summary>
/// Takes its counter off every attack the carrier makes, then wears off at the end of that carrier's
/// own phase. Strength's mirror, but for exactly one turn.
///
/// The counter is the *reduction*, not a duration - which is why OnTurnEnd zeroes it outright instead
/// of decrementing. Decrementing would shrink the debuff a point at a time rather than expiring it,
/// leaving a Weaken 3 reading -3, then -2, then -1. Zeroing is the same move ShieldStatus.OnTurnStart
/// makes for the same reason: the counter means something other than turns, so ageing it by one would
/// age the wrong quantity.
///
/// One turn is the whole design. It lands as a tempo card - blunt one incoming attack - rather than as
/// a slow damage-over-time debuff, and stacking two Weakens deepens the cut instead of extending it.
/// </summary>
public class WeakenStatus : StatusEffect
{
    public WeakenStatus(int stacks) : base(StatusType.Weaken, stacks) { }

    public override DamageInfo OnDealDamage(DamageInfo info) => info.WithAmount(info.amount - stacks);

    public override void OnTurnEnd(Character carrier)
    {
        stacks = 0;
    }
}
