using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Unconditional flat damage adjustment - Whetstone's "+1 to every attack". Projects a FlatDamageStatus;
/// see that class for why this always applies rather than being gated on the wearer already holding some
/// other charge, unlike PotencyStatus.
/// </summary>
[CreateAssetMenu(menuName = "Equipment Modifiers/Damage Bonus")]
public class DamageBonusModifier : EquipmentModifier
{
    [SerializeField] private int outgoing;
    [Tooltip("Taken off every hit the wearer takes. Negative is a drawback: -1 means each hit lands for 1 more.")]
    [SerializeField] private int incomingReduction;

    public override void Project(Character carrier, List<Status> into) =>
        into.Add(new FlatDamageStatus(outgoing, incomingReduction));

    public override string Describe() => FlatDamageStatus.DescribeAmounts(outgoing, incomingReduction);
}
