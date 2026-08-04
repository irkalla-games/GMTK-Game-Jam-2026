/// <summary>
/// Adds its stack count to every attack the carrier makes, for as long as it lasts - normally the
/// whole combat.
///
/// Scales with how *often* you attack rather than how hard, so it is worth most on a character
/// throwing lots of cheap cards: +3 on the free Quick Attack (3 -> 6) doubles it, while on Slash
/// (9 -> 12) it is a third.
/// </summary>
public class StrengthStatus : Status
{
    public StrengthStatus(int stacks, int turnsRemaining)
        : base(StatusType.Strength, stacks, turnsRemaining) { }

    public override DamageInfo OnDealDamage(DamageInfo info) => info.WithAmount(info.amount + stacks);
}
