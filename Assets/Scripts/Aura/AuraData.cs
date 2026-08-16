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

    [Tooltip("GainMultiplier, Potency and TurnTick only: which status this one modifies or grants - "
             + "the keyword a Strength/Block/Weaken/Vulnerable totem cares about.")]
    [SerializeField] private StatusType subject;

    [Tooltip("GainMultiplier, Potency and TurnTick only: the number that isn't stacks - the multiplier "
             + "for GainMultiplier, the flat bonus for Potency, the amount granted for TurnTick.")]
    [SerializeField] private int magnitude;

    [Tooltip("TurnTick only: which end of the round the grant lands. See TurnTiming - the wrong half "
             + "can erase the grant in the same pass it lands.")]
    [SerializeField] private TurnTiming timing;

    public StatusType Type => type;

    public int Stacks => stacks;

    /// <summary>
    /// Builds the StatusEffect this entry describes. Routes GainMultiplier, Potency and TurnTick to
    /// their own constructors - StatusEffect.Create has nowhere to put subject/magnitude/timing, the
    /// same reason it returns null for Taunt - and falls back to StatusEffect.Create for every ordinary
    /// type, so an existing totem authored before these fields existed (subject None, magnitude 0,
    /// timing TurnEnd, none of which its type reads) builds exactly as it did before.
    ///
    /// Totem.Project is the only caller.
    /// </summary>
    public StatusEffect CreateEffect() => type switch
    {
        StatusType.GainMultiplier => new GainMultiplierStatus(subject, magnitude, stacks),
        StatusType.Potency => new PotencyStatus(subject, magnitude, stacks),
        StatusType.TurnTick => new TurnTickStatus(subject, magnitude, timing, stacks),
        _ => StatusEffect.Create(type, stacks),
    };
}
