/// <summary>
/// Negates each of the next few hits outright and reflects them back at whoever swung. `stacks` is the
/// charge count.
///
/// The reflection is delivered by Character.TakeDamage, not here - this only records the amount on the
/// DamageInfo it returns. A status has no business deciding when in the sequence the counter-hit
/// lands, and doing it here would mean reflecting before Block and Shield had finished with the hit.
///
/// Last in the incoming chain (see Order) and the only one that reads what came before it: Block and
/// Shield have already had their say by the time this runs, so it only ever fires - and only ever
/// spends a charge - on whatever damage is still standing. A hit Block or Shield has already reduced to
/// zero passes through untouched, charge and all held in reserve for the next one.
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

    /// Last of the incoming chain - see the class doc and Status.Order.
    public override int Order => 10;

    public override DamageInfo OnTakeDamage(DamageInfo info)
    {
        // Nothing left to negate - Block and/or Shield already finished the job. Leave the charge
        // unspent rather than reflecting a zero.
        if (stacks <= 0 || info.amount <= 0) { return info; }

        // Looking is free. Only an actual hit spends the charge - see DamageInfo.consumeCharges.
        if (info.consumeCharges) { stacks--; }

        // Reflected() also marks the hit negated, but Block and Shield have already run by this point -
        // see Order.
        return info.Reflected();
    }

    public override string Describe() => $"Parry x{stacks}";
}
