using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Adds flat outgoing damage while the carrier's own health meets a condition - Bloodshard Ring,
/// Panther's Eye. Projects a ConditionalDamageStatus; see that class and DamageCondition for the
/// thresholds available.
/// </summary>
[CreateAssetMenu(menuName = "Equipment Modifiers/Conditional Damage")]
public class ConditionalDamageModifier : EquipmentModifier
{
    [SerializeField] private DamageCondition condition;
    [SerializeField] private int bonus;

    public override void Project(Character carrier, List<Status> into) =>
        into.Add(new ConditionalDamageStatus(condition, bonus));

    public override string Describe() => condition switch
    {
        DamageCondition.BelowHalfHealth => $"+{bonus} damage while below half health",
        DamageCondition.AtFullHealth => $"+{bonus} damage while at full health",
        _ => $"+{bonus} damage",
    };
}
