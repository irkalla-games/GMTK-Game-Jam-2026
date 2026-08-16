/// <summary>
/// Negates each of the next few hits outright and reflects them back at whoever swung. `stacks` is the
/// charge count.
///
/// The reflection is delivered by Character.TakeDamage, not here - this only records the amount on the
/// DamageInfo it returns. A status has no business deciding when in the sequence the counter-hit
/// lands, and doing it here would mean reflecting before Block and Shield had finished with the hit.
///
/// **A parry can itself be parried.** The counter-hit goes back through the ordinary TakeDamage door,
/// so the original attacker's own statuses all get their say - their Shield absorbs it, their Block
/// reduces it, and their Parry sends it straight back again. Two characters holding Parry will bounce
/// one hit between them until somebody runs out of charges, which is exactly as funny as it sounds and
/// is bounded by Character.MaxParryBounces.
/// </summary>
public class ParryStatus : StatusEffect
{
    public ParryStatus(int stacks) : base(StatusType.Parry, stacks) { }

    public override DamageInfo OnTakeDamage(DamageInfo info)
    {
        if (stacks <= 0) { return info; }

        // Looking is free. Only an actual hit spends the charge - see DamageInfo.consumeCharges.
        if (info.consumeCharges) { stacks--; }

        // Reflected() also marks the hit negated, so Block and Shield are never reached - there is
        // nothing left of it to reduce.
        return info.Reflected();
    }

    public override string Describe() => $"Parry x{stacks}";
}
