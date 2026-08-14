/// <summary>
/// A summoned body's own lifetime clock. Counts down each of the carrier's own turns and, at zero,
/// kills it outright via TakeUnblockableDamage(carrier.Health) - which is what routes the death through
/// the ordinary CheckDeath -> Died path, so loot, roster removal and death animation all behave exactly
/// as they would for any other death. No new death API, no new field on Character.
///
/// Applied directly by SummonAction rather than picked off a card's Apply Status dropdown -
/// SummonEffect.lifetimeTurns is its authoring half. 0 there means "skip this entirely, permanent
/// summon", so a lifetime of 0 must never reach here - see SummonAction.
/// </summary>
public class SummonedStatus : StatusEffect
{
    public SummonedStatus(int turns) : base(StatusType.Summoned, turns) { }

    public override void OnTurnEnd(Character carrier)
    {
        // Decay first, then check - the last turn authored is the last turn the carrier actually gets
        // to stand around in, and dies at the end of it rather than one tick early.
        stacks--;

        if (stacks <= 0 && carrier != null && !carrier.IsDead)
        {
            carrier.TakeUnblockableDamage(carrier.Health);
        }
    }

    public override string Describe() => $"Summoned ({stacks} turns left)";
}
