using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Adds a flat bonus to the stack count of whatever a card grants - Tower Shield's "+2 Shield from any
/// card", Ironbound Vambrace's "+1 Block", Hexbinder's Cord's "+1 Weaken", Venom Reservoir's "+1 Poison".
/// Projects a GainBonusStatus - see that class for how it reaches every application path uniformly
/// through Character.AddStatus's OnGainStatus pipeline.
/// </summary>
[CreateAssetMenu(menuName = "Equipment Modifiers/Grant Bonus")]
public class GrantBonusModifier : EquipmentModifier
{
    [SerializeField] private StatusType subject;
    [SerializeField] private int bonus = 1;

    public override void Project(Character carrier, List<Status> into) =>
        into.Add(new GainBonusStatus(subject, bonus));

    public override string Describe() => $"{subject} +{bonus} per grant";
}
