/// <summary>
/// Deepens whatever status type `subject` names every time the carrier applies one to somebody else -
/// the Weaken Ring's "the Weaken you apply cuts 1 deeper". The applier-side counterpart to
/// PotencyStatus, which boosts a status the carrier is already holding.
///
/// The distinction is the whole reason this exists rather than reusing GainBonusStatus: that one reacts
/// to OnGainStatus, which Character.AddStatus runs over the *receiver's* statuses, so it can only ever
/// answer "statuses applied to me". It also moves the stack count, which for Weaken and Vulnerable is
/// now the duration - "+1 Weaken" meaning "one turn longer" is not what a potency relic says. This moves
/// Status.Amount instead, and it is asked of whoever is swinging: see Character.AppliedPotency, which
/// StatusAction consults for a card and Totem.Project for an aura.
///
/// Follows the summoner to the summon. A totem is its own Character with its own empty equipment list,
/// so a mage's ring would otherwise stop at the mage and leave their Sap Totem casting a plain Weaken.
/// OnSummoned copies this onto the summon as a real carried status, which is what Totem.Project then
/// reads - see Character.CarriedAppliedPotency for why it has to be carried rather than projected.
///
/// Reports StatusType.None for the same reason GainBonusStatus does: equipment has its own UI, and every
/// status row treats None as nothing to show. stacks is inert on the wearer's projected copy (rebuilt
/// fresh every ActiveStatuses call) and merely non-zero on a summon's carried copy, which has no turn
/// hook here to age it.
/// </summary>
public class AppliedPotencyStatus : StatusEffect
{
    private readonly StatusType subject;
    private readonly int bonus;

    public AppliedPotencyStatus(StatusType subject, int bonus) : base(StatusType.None, 1)
    {
        this.subject = subject;
        this.bonus = bonus;
    }

    public override int AppliedPotency(StatusType type) => type == subject ? bonus : 0;

    /// <summary>
    /// Hands the same potency to anything the carrier summons, so a totem projects the wearer's
    /// deepened Weaken rather than the plain one.
    ///
    /// A fresh instance rather than adding `this`: the wearer's copy is a throwaway rebuilt on every
    /// ActiveStatuses call (EquipmentModifier.Project), so parking it in the summon's own carried list
    /// would keep a reference to an object nothing else expects to outlive the query. Guarded against
    /// re-granting, since a second summon must not compound the bonus on a totem that already has it.
    /// </summary>
    public override void OnSummoned(Character summoner, Character summon)
    {
        if (summon == null || bonus == 0 || subject == StatusType.None) { return; }

        if (summon.CarriedAppliedPotency(subject) != 0) { return; }

        summon.AddStatus(new AppliedPotencyStatus(subject, bonus));
    }

    public override string Describe() => $"{subject} you apply: +{bonus}";
}
