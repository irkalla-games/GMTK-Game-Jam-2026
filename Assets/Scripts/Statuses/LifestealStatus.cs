/// <summary>
/// For as long as it lasts, every hit the carrier lands also heals the carrier for half the damage
/// dealt. The healing counterpart to PoisonBladeStatus - see OnHitStatus, the template both share.
///
/// Fires on any connecting hit, mitigated or not: a hit Shield or Block reduces to zero still counts
/// as landing, because the blade touched skin either way - see Character.TakeDamage, which only
/// calls the rider when the hit was not negated (Dodge, Parry).
/// </summary>
public class LifestealStatus : OnHitStatus
{
    public LifestealStatus(int stacks) : base(StatusType.Lifesteal, stacks) { }

    public override void OnDamageDealt(DamageInfo info)
    {
        if (info.attacker == null || info.amount <= 0) { return; }

        info.attacker.Heal(info.amount / 2);
    }

    // stacks is remaining turns and is worth showing, unlike Taunt's bare name - the same call
    // PoisonBladeStatus makes for the same reason (its own OnHitStatus sibling).
    public override string Describe() => $"Lifesteal x{stacks}";
}
