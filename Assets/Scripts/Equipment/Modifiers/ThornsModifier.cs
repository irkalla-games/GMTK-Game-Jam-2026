using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bramblewreath's "attackers take 2 damage" - projects a ThornsStatus; see that class for why the
/// retaliation is unblockable and gated on info.consumeCharges.
/// </summary>
[CreateAssetMenu(menuName = "Equipment Modifiers/Thorns")]
public class ThornsModifier : EquipmentModifier
{
    [SerializeField] private int amount = 1;

    public override void Project(Character carrier, List<Status> into) =>
        into.Add(new ThornsStatus(amount));

    public override string Describe() => $"Attackers take {amount} damage";
}
