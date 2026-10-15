/// <summary>
/// Adds `bonus` stacks to every `subject` the carrier applies to somebody else - Wardens' Braid. Read
/// through Character.AppliedStacks by StatusAction (a card) and PoisonBladeStatus (an on-hit proc).
///
/// Wearer-only: unlike AppliedPotencyStatus it does not follow the summoner to a summon, because a
/// totem's Poison is a projected aura ticking on whoever stands in range, not a status it applies.
///
/// Reports StatusType.None for the same reason the other equipment statuses do - equipment has its own
/// UI, and every status row treats None as nothing to show.
/// </summary>
public class AppliedStacksStatus : StatusEffect
{
    private readonly StatusType subject;
    private readonly int bonus;

    public AppliedStacksStatus(StatusType subject, int bonus) : base(StatusType.None, 1)
    {
        this.subject = subject;
        this.bonus = bonus;
    }

    public override int AppliedStacks(StatusType type) => type == subject ? bonus : 0;

    public override string Describe() => $"{subject} you apply: +{bonus} stacks";
}
