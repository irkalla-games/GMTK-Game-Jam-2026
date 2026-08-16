using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grants the wearer a permanent status - always Strength 2, always Stealth, whatever an ordinary card
/// could apply. Built exactly like a Totem's own aura (AuraData.CreateEffect's fallback arm): routes
/// through StatusEffect.Create, so it can name any status a card can. Rebuilt fresh every
/// Character.ActiveStatuses call, so a charge-spending status (Block, Parry) never actually depletes
/// while equipped - the same trade an aura-projected one already makes, documented on Aura.
/// </summary>
[CreateAssetMenu(menuName = "Equipment Modifiers/Grant Status")]
public class StatusGrantModifier : EquipmentModifier
{
    [SerializeField] private StatusType type;
    [SerializeField] private int stacks = 1;

    public override void Project(Character carrier, List<Status> into)
    {
        StatusEffect effect = StatusEffect.Create(type, stacks);

        if (effect != null) { into.Add(effect); }
    }

    public override string Describe() => $"Permanent {type} {stacks}";
}
