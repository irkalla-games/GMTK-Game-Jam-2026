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

    [Tooltip("GainMultiplier, Potency, TurnTick and GainBonus only: which status this one modifies or "
             + "grants - the keyword a Strength/Block/Weaken/Vulnerable totem cares about. The two "
             + "FlatDamage types name no subject; they only carry a magnitude.")]
    [SerializeField] private StatusType subject;

    [Tooltip("The number that isn't stacks - the multiplier for GainMultiplier, the flat bonus for "
             + "Potency, the amount granted for TurnTick, the flat bonus per grant for GainBonus, and "
             + "the damage added or removed for the two FlatDamage types.")]
    [SerializeField] private int magnitude;

    [Tooltip("TurnTick only: which end of the round the grant lands. See TurnTiming - the wrong half "
             + "can erase the grant in the same pass it lands.")]
    [SerializeField] private TurnTiming timing;

    public StatusType Type => type;

    public int Stacks => stacks;

    public StatusType Subject => subject;

    public int Magnitude => magnitude;

    public TurnTiming Timing => timing;

    /// True for the types CreateEffect below builds from subject/magnitude/timing instead of
    /// StatusEffect.Create. Exposed so Glossary.SummonContent can describe an aura from those same
    /// fields without re-deriving this exact list itself. See StatusTypes.IsTotemOnly, which this is
    /// built on - the same types also decide whether a status shows up on a character at all.
    ///
    /// Slightly wider than its name suggests since FlatDamageDealt/FlatDamageTaken joined: those two
    /// name no subject, they only carry a magnitude. The name stays because what every caller actually
    /// asks is "does this read the aura entry's own fields rather than a stack count", and for those
    /// two it does.
    public bool HasSubject => type.IsTotemOnly();

    /// <summary>
    /// Builds the StatusEffect this entry describes. Routes the totem-only types to their own
    /// constructors - StatusEffect.Create has nowhere to put subject/magnitude/timing - and Taunt to
    /// one built from `owner`, and falls back to StatusEffect.Create for every ordinary type, so an
    /// existing totem authored before these fields existed (subject None, magnitude 0, timing TurnEnd,
    /// none of which its type reads) builds exactly as it did before.
    ///
    /// FlatDamageDealt/FlatDamageTaken and GainBonus reach classes that were written for equipment and
    /// already do exactly the right thing - see FlatDamageStatus and GainBonusStatus. Both take the
    /// StatusType as their last argument so an aura-built one can name itself while an equipment-built
    /// one keeps reporting None; neither class needed its rule touched. They are the authorable
    /// answers to the two places the ordinary statuses cannot carry a number: StrengthStatus and
    /// BlockStatus both hardcode AmountPerHit = 3, so "+5 damage" and "-2 damage taken" have no other
    /// home.
    ///
    /// `amountBonus` is what the totem's own equipment-style potency adds to the size of the projected
    /// status - Totem.Project asks its owner, which inherited the summoner's bonus at summon time, so a
    /// mage's Weaken ring deepens the Weaken their Sap Totem casts. The parameterised types and Taunt
    /// ignore it: their magnitude is authored outright, or in Taunt's case there is no size at all.
    ///
    /// `owner` is the totem's own Character - the taunter a Taunt-type entry names, since a totem has
    /// no other character to point at. StatusEffect.Create alone cannot build a TauntStatus (its
    /// type/stacks signature has nowhere to carry a taunter reference); an aura is the one place that
    /// reference is unambiguous - a taunting totem should pull enemies toward *itself*.
    ///
    /// Totem.Project is the only caller.
    /// </summary>
    public StatusEffect CreateEffect(int amountBonus, Character owner) => type switch
    {
        StatusType.GainMultiplier => new GainMultiplierStatus(subject, magnitude, stacks),
        StatusType.Potency => new PotencyStatus(subject, magnitude, stacks),
        StatusType.TurnTick => new TurnTickStatus(subject, magnitude, timing, stacks),
        StatusType.FlatDamageDealt =>
            new FlatDamageStatus(magnitude, 0, StatusType.FlatDamageDealt),
        StatusType.FlatDamageTaken =>
            new FlatDamageStatus(0, magnitude, StatusType.FlatDamageTaken),
        StatusType.GainBonus => new GainBonusStatus(subject, magnitude, StatusType.GainBonus),
        StatusType.Taunt => owner != null ? new TauntStatus(owner, stacks) : null,
        _ => StatusEffect.Create(type, stacks, amountBonus),
    };
}
