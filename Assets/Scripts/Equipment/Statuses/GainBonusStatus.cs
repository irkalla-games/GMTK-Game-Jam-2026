/// <summary>
/// Adds a flat bonus to the *stack count* of whatever status type `subject` names, whenever that type
/// is applied to the carrier - Tower Shield's "+2 Shield from any card", Ironbound Vambrace's "+1 Block
/// charge". GainMultiplierStatus's sibling, additive rather than multiplicative: that one scales how
/// many stacks land, this adds a flat amount on top.
///
/// Equipment-only, the same reason GainMultiplierStatus is aura-only: nothing carries this itself, so
/// it is never built by StatusEffect.Create. Reacts through OnGainStatus, which Character.AddStatus runs
/// before merging or appending the incoming StatusEffect - the same pipeline a totem's GainMultiplier
/// already rides, so equipment and an aura granting the same subject compound rather than fight.
///
/// Reports StatusType.None rather than a real type: an equipment-granted status is not meant to render
/// as its own chip in the status row (equipment gets its own UI, built from EquipmentData directly - see
/// EquipmentChip), and every UI that walks StatusType already treats None as "nothing to show". stacks
/// is likewise inert here - equipment statuses are rebuilt fresh on every Character.ActiveStatuses call,
/// exactly like an Aura, so there is nothing for IsExpired or PruneExpired to act on.
/// </summary>
public class GainBonusStatus : StatusEffect
{
    private readonly StatusType subject;
    private readonly int bonus;

    public GainBonusStatus(StatusType subject, int bonus) : base(StatusType.None, 1)
    {
        this.subject = subject;
        this.bonus = bonus;
    }

    public override StatusGainInfo OnGainStatus(StatusGainInfo info)
    {
        if (info.type != subject || bonus == 0) { return info; }

        return info.WithStacks(info.stacks + bonus);
    }

    public override string Describe() => $"{subject} +{bonus} per grant";
}
