/// <summary>
/// Adds a flat amount to outgoing damage while `condition` holds - Bloodshard Ring's "+4 damage while
/// below half health", Panther's Eye's "+4 damage while at full health", Sporecap Ring's "+4 damage
/// against Vulnerable enemies". Checked fresh on every swing, not latched, so crossing the threshold
/// mid-battle turns the bonus on or off immediately rather than needing to be re-equipped.
///
/// A health condition is about the carrier, so it rides OnDealDamage, once per swing. A target
/// condition rides OnStrike instead, once per victim - an AoE hitting one Vulnerable body and one
/// healthy one must add the bonus to the first only.
/// </summary>
public class ConditionalDamageStatus : StatusEffect
{
    private readonly DamageCondition condition;
    private readonly int bonus;

    public ConditionalDamageStatus(DamageCondition condition, int bonus) : base(StatusType.None, 1)
    {
        this.condition = condition;
        this.bonus = bonus;
    }

    public override DamageInfo OnDealDamage(DamageInfo info)
    {
        if (bonus == 0 || info.amount <= 0 || info.attacker == null || IsTargetCondition) { return info; }
        if (!MetBy(info.attacker)) { return info; }

        return info.WithAmount(info.amount + bonus);
    }

    public override DamageInfo OnStrike(DamageInfo info)
    {
        if (bonus == 0 || info.amount <= 0 || info.target == null || !IsTargetCondition) { return info; }
        if (!MetOn(info.target)) { return info; }

        return info.WithAmount(info.amount + bonus);
    }

    private bool IsTargetCondition => condition == DamageCondition.TargetVulnerable;

    private bool MetBy(Character carrier) => condition switch
    {
        DamageCondition.BelowHalfHealth => carrier.Health * 2 <= carrier.MaxHealth,
        DamageCondition.AtFullHealth => carrier.Health >= carrier.MaxHealth,
        _ => false,
    };

    private bool MetOn(Character target) => condition switch
    {
        DamageCondition.TargetVulnerable => target.StatusStacks(StatusType.Vulnerable) > 0,
        _ => false,
    };

    public override string Describe() => condition switch
    {
        DamageCondition.BelowHalfHealth => $"+{bonus} damage while below half health",
        DamageCondition.AtFullHealth => $"+{bonus} damage while at full health",
        DamageCondition.TargetVulnerable => $"+{bonus} damage against Vulnerable enemies",
        _ => $"+{bonus} damage",
    };
}
