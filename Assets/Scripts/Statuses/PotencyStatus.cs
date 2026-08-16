/// <summary>
/// Adds a flat bonus to the *magnitude* of whatever status type `subject` names, without touching its
/// stack count - more damage per Strength swing, more reduction per Block charge, a deeper cut per
/// Weaken, a harder hit per Vulnerable. GainMultiplierStatus's sibling: that one scales how many
/// charges land, this scales what each charge is worth.
///
/// Aura-only, same reason GainMultiplierStatus is - only ever built by AuraData.CreateEffect.
///
/// Unlike every other status here, which subject decides is not "which side of the pipeline" the way
/// OnDealDamage vs OnTakeDamage usually splits along attacker/target - Strength and Weaken both alter
/// the carrier's *outgoing* total, Block and Vulnerable both alter its *incoming* total. So this reacts
/// on both OnDealDamage and OnTakeDamage and switches on `subject` inside each to decide whether it has
/// anything to say. That switch is the one place the "parameterised over one field" design costs
/// something: a fifth Potency subject added later needs a new case in both methods rather than a new
/// subclass.
///
/// Reads whether the carrier holds a *carried* charge of `subject` with StatusStacks(subject) > 0,
/// which is correct only because auras resolve first in Character.ActiveStatuses - this status is
/// asked before the carrier's own Strength/Block/Weaken/Vulnerable has had a chance to spend its
/// charge, so "greater than zero" is exactly "about to apply this swing".
/// </summary>
public class PotencyStatus : StatusEffect
{
    private readonly StatusType subject;
    private readonly int bonus;

    public PotencyStatus(StatusType subject, int bonus, int stacks) : base(StatusType.Potency, stacks)
    {
        this.subject = subject;
        this.bonus = bonus;
    }

    public override DamageInfo OnDealDamage(DamageInfo info)
    {
        Character carrier = info.attacker;

        if (carrier == null || bonus == 0) { return info; }

        return subject switch
        {
            StatusType.Strength when carrier.StatusStacks(StatusType.Strength) > 0 =>
                info.WithAmount(info.amount + bonus),
            StatusType.Weaken when carrier.StatusStacks(StatusType.Weaken) > 0 =>
                info.WithAmount(info.amount - bonus),
            _ => info,
        };
    }

    public override DamageInfo OnTakeDamage(DamageInfo info)
    {
        Character carrier = info.target;

        if (carrier == null || info.negated || bonus == 0) { return info; }

        return subject switch
        {
            StatusType.Block when carrier.StatusStacks(StatusType.Block) > 0 => info.Reduced(bonus),
            StatusType.Vulnerable when carrier.StatusStacks(StatusType.Vulnerable) > 0 =>
                info.WithAmount(info.amount + bonus),
            _ => info,
        };
    }

    public override string Describe() => $"{subject} potency +{bonus}";
}
