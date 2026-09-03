using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Adds outgoing damage per stack of a named status the carrier holds - Stormsplit Ring's "+1 damage
/// per Strength stack you hold". Projects a ScalingDamageStatus.
/// </summary>
[CreateAssetMenu(menuName = "Equipment Modifiers/Scaling Damage")]
public class ScalingDamageModifier : EquipmentModifier
{
    [SerializeField] private StatusType subject;
    [SerializeField] private int perStack = 1;

    public override void Project(Character carrier, List<Status> into) =>
        into.Add(new ScalingDamageStatus(subject, perStack));

    public override string Describe() => $"+{perStack} damage per {subject} stack held";
}
