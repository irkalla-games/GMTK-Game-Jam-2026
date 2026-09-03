using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grants a flat energy and/or hand-size bonus every turn - Ring of Focus, Astrologer's Loop, Opaline
/// Charm. Projects a ResourceStatus; see that class for why the bonuses are read off Character rather
/// than applied directly here.
/// </summary>
[CreateAssetMenu(menuName = "Equipment Modifiers/Resource")]
public class ResourceModifier : EquipmentModifier
{
    [SerializeField] private int bonusEnergy;
    [SerializeField] private int bonusHandSize;

    public override void Project(Character carrier, List<Status> into) =>
        into.Add(new ResourceStatus(bonusEnergy, bonusHandSize));

    public override string Describe()
    {
        if (bonusEnergy != 0 && bonusHandSize != 0) { return $"+{bonusEnergy} energy, +{bonusHandSize} cards each turn"; }
        if (bonusEnergy != 0) { return $"+{bonusEnergy} energy each turn"; }
        if (bonusHandSize != 0) { return $"+{bonusHandSize} cards each turn"; }
        return "No change";
    }
}
