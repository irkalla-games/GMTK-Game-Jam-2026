/// <summary>
/// Which health threshold ConditionalDamageStatus gates its bonus on - Bloodshard Ring's "while below
/// half health" versus Panther's Eye's "while at full health".
///
/// Written into ConditionalDamageModifier assets, so append-only like every other authored enum here -
/// see StatusType.
/// </summary>
public enum DamageCondition
{
    BelowHalfHealth = 0,
    AtFullHealth = 1,
}
