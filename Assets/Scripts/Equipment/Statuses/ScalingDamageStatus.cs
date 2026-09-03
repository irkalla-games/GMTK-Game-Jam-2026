/// <summary>
/// Adds outgoing damage per stack of a named status the carrier currently holds - Stormsplit Ring's
/// "+1 damage per Strength stack you hold". Reads Character.StatusStacks fresh on every swing, the same
/// "auras and own statuses combined" total that status row already reports - a fresh source of totem
/// Strength counts exactly as much as a self-carried charge.
///
/// Safe to call StatusStacks from inside this hook even though StatusStacks itself walks
/// ActiveStatuses: unlike CarriedAppliedPotency, this status is never asked by Totem.Project (equipment
/// is not part of the totem aura chain), so there is no cycle here to worry about - just one extra,
/// independent rebuild of the active list.
/// </summary>
public class ScalingDamageStatus : StatusEffect
{
    private readonly StatusType subject;
    private readonly int perStack;

    public ScalingDamageStatus(StatusType subject, int perStack) : base(StatusType.None, 1)
    {
        this.subject = subject;
        this.perStack = perStack;
    }

    public override DamageInfo OnDealDamage(DamageInfo info)
    {
        if (perStack == 0 || info.amount <= 0 || info.attacker == null) { return info; }

        int bonus = perStack * info.attacker.StatusStacks(subject);

        if (bonus == 0) { return info; }

        return info.WithAmount(info.amount + bonus);
    }

    public override string Describe() => $"+{perStack} damage per {subject} stack held";
}
