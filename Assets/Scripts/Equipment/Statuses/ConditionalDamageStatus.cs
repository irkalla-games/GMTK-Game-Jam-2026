/// <summary>
/// Adds a flat amount to outgoing damage while the carrier's own health meets `condition` -
/// Bloodshard Ring's "+3 damage while below half health", Panther's Eye's "+3 damage while at full
/// health". Checked against the carrier fresh on every swing, not latched, so crossing the threshold
/// mid-battle turns the bonus on or off immediately rather than needing to be re-equipped.
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
        if (bonus == 0 || info.amount <= 0 || info.attacker == null) { return info; }
        if (!Met(info.attacker)) { return info; }

        return info.WithAmount(info.amount + bonus);
    }

    private bool Met(Character carrier) => condition switch
    {
        DamageCondition.BelowHalfHealth => carrier.Health * 2 <= carrier.MaxHealth,
        DamageCondition.AtFullHealth => carrier.Health >= carrier.MaxHealth,
        _ => false,
    };

    public override string Describe() => condition switch
    {
        DamageCondition.BelowHalfHealth => $"+{bonus} damage while below half health",
        DamageCondition.AtFullHealth => $"+{bonus} damage while at full health",
        _ => $"+{bonus} damage",
    };
}
