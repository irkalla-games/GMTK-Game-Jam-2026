using System;
using UnityEngine;

/// <summary>
/// One aura as *authored* on a Totem - Strength 1 while near the Cleric, for example. `AuraData` is
/// to `Aura` what `CardData` is to `Card`: this is the shared authoring entry, and Totem.Project
/// builds a runtime Aura from it for each character in range.
///
/// Nothing is granted or withdrawn: Character.ActiveStatuses asks the totems what they are projecting
/// each time it is called, and builds fresh from this every time. So the entry describes a
/// *maintained* effect, which suits magnitude statuses cleanly and charge-spending ones oddly -
/// Block, Parry and Double Attack mutate a throwaway, so they never deplete while you stand in range.
/// If you want a charge that actually gets spent, put it on an AuraReaction, which is a one-shot grant
/// onto the character's own list.
/// </summary>
[Serializable]
public class AuraData
{
    [SerializeField] private StatusType type;

    [Tooltip("Magnitude, same meaning as on the Apply Status card effect.")]
    [SerializeField] private int stacks = 1;

    public StatusType Type => type;

    public int Stacks => stacks;
}
