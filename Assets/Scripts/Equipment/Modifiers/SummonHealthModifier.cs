using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Raises the max health of things the wearer summons - Totem Anchor's "+7 totem health". Projects a
/// SummonHealthStatus; see that class for the totemsOnly restriction.
/// </summary>
[CreateAssetMenu(menuName = "Equipment Modifiers/Summon Health")]
public class SummonHealthModifier : EquipmentModifier
{
    [SerializeField] private int bonus;

    [Tooltip("On, only a summon with a Totem component gains the bonus. Off, every summon does.")]
    [SerializeField] private bool totemsOnly = true;

    public override void Project(Character carrier, List<Status> into) =>
        into.Add(new SummonHealthStatus(bonus, totemsOnly));

    public override string Describe() =>
        totemsOnly ? $"Totems: +{bonus} max health" : $"Summons: +{bonus} max health";
}
