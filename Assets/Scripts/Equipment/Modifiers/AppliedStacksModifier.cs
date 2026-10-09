using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Adds stacks to a status the wearer *applies to others* - Wardens' Braid's "+1 Poison whenever you
/// apply Poison". Projects an AppliedStacksStatus; see Status.AppliedStacks for where it is read.
///
/// GrantBonusModifier is the receiver-side counterpart ("+1 Block whenever you gain Block") and cannot
/// stand in for this: it rides OnGainStatus, which runs over whoever the status lands on, so authoring
/// Poison there poisons the wearer harder instead. AppliedPotencyModifier deepens a status's size
/// rather than its count, which Poison does not have.
/// </summary>
[CreateAssetMenu(menuName = "Equipment Modifiers/Applied Stacks")]
public class AppliedStacksModifier : EquipmentModifier
{
    [Tooltip("Which status gains the extra stacks when the wearer applies it to somebody else.")]
    [SerializeField] private StatusType subject;

    [SerializeField] private int bonus = 1;

    public override void Project(Character carrier, List<Status> into) =>
        into.Add(new AppliedStacksStatus(subject, bonus));

    public override string Describe() => $"{subject} you apply: +{bonus} stacks";
}
