using System;
using UnityEngine;

/// <summary>
/// One status a Totem projects onto every character currently standing in its range - Strength while
/// near the Cleric, for example.
///
/// Nothing is granted or withdrawn: Character.ActiveStatuses asks the totems what they are projecting
/// each time it is called, and builds a fresh Status from this every time. So the entry describes a
/// *maintained* effect, which suits magnitude statuses cleanly and charge-spending ones oddly -
/// Block, Parry and Double Attack mutate a throwaway here, so they never deplete while you stand in
/// range. If you want a charge that actually gets spent, put it on an AuraReaction, which is a
/// one-shot grant onto the character's own list.
/// </summary>
[Serializable]
public class AuraPassiveBuff
{
    [SerializeField] private StatusType type;

    [Tooltip("Magnitude, same meaning as on StatusEffect.")]
    [SerializeField] private int stacks = 1;

    public StatusType Type => type;

    public int Stacks => stacks;
}
