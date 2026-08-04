/// <summary>
/// Deals its stack count at the end of the carrier's own phase, bypassing every defense.
///
/// Unblockable on purpose: Shield, Block and Parry are things you hold up against something being
/// thrown at you, and they do nothing about something already in your blood. That makes Poison the
/// answer to a target your damage cannot solve - one that armors up every turn, or one you cannot
/// reach - rather than a second way to do the same job as an attack card.
///
/// Because it bites at the *end* of the carrier's phase, poison on an enemy ticks after that enemy has
/// already acted, so it never denies an action. It kills, it does not disrupt.
/// </summary>
public class PoisonStatus : StatusEffect
{
    public PoisonStatus(int stacks, int turnsRemaining)
        : base(StatusType.Poison, stacks, turnsRemaining) { }

    public override void OnTurnEnd(Character carrier)
    {
        carrier.TakeUnblockableDamage(stacks);
    }
}
