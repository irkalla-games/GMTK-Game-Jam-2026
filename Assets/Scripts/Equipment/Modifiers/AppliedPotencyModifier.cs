using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Deepens the status the wearer *applies to others* - the Weaken Ring's "the Weaken you apply cuts 1
/// deeper". Projects an AppliedPotencyStatus; see that class for why this is a different thing from
/// GrantBonusModifier, which adds to the stack count of statuses applied *to* the wearer.
///
/// Only Weaken and Vulnerable carry a size for this to move today (Status.Amount); naming any other
/// subject projects a status nothing reads, rather than failing loudly, which matches how
/// GrantBonusModifier handles a subject no card grants.
/// </summary>
[CreateAssetMenu(menuName = "Equipment Modifiers/Applied Potency")]
public class AppliedPotencyModifier : EquipmentModifier
{
    [Tooltip("Which status this deepens when the wearer applies it. Weaken and Vulnerable are the two "
             + "with a size to deepen - see Status.Amount.")]
    [SerializeField] private StatusType subject;

    [SerializeField] private int bonus = 1;

    public override void Project(Character carrier, List<Status> into) =>
        into.Add(new AppliedPotencyStatus(subject, bonus));

    public override string Describe() => $"{subject} you apply: +{bonus}";
}
