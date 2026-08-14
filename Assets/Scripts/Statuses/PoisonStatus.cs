/// <summary>
/// Deals its counter at the end of the carrier's own phase, bypassing every defense, then decays by
/// one. Poison 4 deals 4, then 3, then 2, then 1, and is gone: 10 damage over four turns.
///
/// The status that proves one counter is enough. It is the only one whose number was ever genuinely
/// two things - damage per tick and number of ticks - and decaying is what collapses them: the counter
/// is both, because spending it is what makes the next tick smaller. Front-loaded and self-limiting,
/// with no second field and no constant anywhere.
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
    public PoisonStatus(int stacks) : base(StatusType.Poison, stacks) { }

    public override void OnTurnEnd(Character carrier)
    {
        // Bite first, decay second, so the last point of poison still deals its 1 before expiring.
        carrier.TakeUnblockableDamage(stacks);

        stacks--;
    }
}
