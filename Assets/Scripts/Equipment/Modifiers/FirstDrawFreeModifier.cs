using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Opaline Charm's "the first card with a Draw effect you play each turn costs 0". Projects a
/// FirstDrawFreeStatus; see that class and Character.PlayedDrawCardThisTurn.
/// </summary>
[CreateAssetMenu(menuName = "Equipment Modifiers/First Draw Free")]
public class FirstDrawFreeModifier : EquipmentModifier
{
    public override void Project(Character carrier, List<Status> into) =>
        into.Add(new FirstDrawFreeStatus());

    public override string Describe() => "First Draw card each turn costs 0";
}
