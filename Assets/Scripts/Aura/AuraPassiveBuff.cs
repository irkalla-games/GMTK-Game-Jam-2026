using System;
using UnityEngine;

/// <summary>
/// One buff an AuraSource keeps on every character currently standing in its range - Strength while
/// near the Cleric, for example. Granted through Character.ApplyAura the instant a character enters
/// range and withdrawn through RemoveAura the instant they leave, so this only suits statuses with no
/// notion of "already partly spent" - Shield/Block/Parry are charge-consumed by combat and have no
/// clean answer to "how much of this is still the aura's to take back", so a buff meant to grant those
/// belongs on an AuraReaction (a one-shot grant) instead of here.
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
