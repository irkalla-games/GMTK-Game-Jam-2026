/// <summary>
/// What ConditionalDamageStatus gates its bonus on - the carrier's own health (Bloodshard Ring's "while
/// below half health", Panther's Eye's "while at full health") or the body being hit (Sporecap Ring's
/// "against Vulnerable enemies"). A target condition is checked per victim through Status.OnStrike, a
/// health condition once per swing through OnDealDamage.
///
/// Written into ConditionalDamageModifier assets, so append-only like every other authored enum here -
/// see StatusType.
/// </summary>
public enum DamageCondition
{
    BelowHalfHealth = 0,
    AtFullHealth = 1,
    TargetVulnerable = 2,
}
