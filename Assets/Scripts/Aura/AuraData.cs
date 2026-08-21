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

    public StatusType Subject => subject;

    public int Magnitude => magnitude;

    public TurnTiming Timing => timing;

    /// True for GainMultiplier, Potency and TurnTick - the three types CreateEffect below builds from
    /// subject/magnitude/timing instead of StatusEffect.Create. Exposed so Glossary.SummonContent can
    /// describe an aura from those same fields without re-deriving this exact three-type list itself.
    /// See StatusTypes.IsTotemOnly, which this is now built on - the same three types also decide
    /// whether a status shows up on a character at all.
    public bool HasSubject => type.IsTotemOnly();

    /// <summary>
    /// Builds the StatusEffect this entry describes. Routes GainMultiplier, Potency and TurnTick to
    /// their own constructors - StatusEffect.Create has nowhere to put subject/magnitude/timing, the
    /// same reason it returns null for Taunt - and falls back to StatusEffect.Create for every ordinary
    /// type, so an existing totem authored before these fields existed (subject None, magnitude 0,
    /// timing TurnEnd, none of which its type reads) builds exactly as it did before.
    ///
    /// `amountBonus` is what the totem's own equipment-style potency adds to the size of the projected
    /// status - Totem.Project asks its owner, which inherited the summoner's bonus at summon time, so a
    /// mage's Weaken ring deepens the Weaken their Sap Totem casts. The three parameterised types ignore
    /// it: their magnitude is authored outright rather than being a status size.
    ///
    /// Totem.Project is the only caller.
    /// </summary>
    public StatusEffect CreateEffect(int amountBonus = 0) => type switch
    {
        StatusType.GainMultiplier => new GainMultiplierStatus(subject, magnitude, stacks),
        StatusType.Potency => new PotencyStatus(subject, magnitude, stacks),
        StatusType.TurnTick => new TurnTickStatus(subject, magnitude, timing, stacks),
        _ => StatusEffect.Create(type, stacks, amountBonus),
    };
}
