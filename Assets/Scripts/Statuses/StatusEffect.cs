using UnityEngine;

/// <summary>
/// A status a character carries itself: applied by a card, held in its own list, aged by its own
/// turns, and spending real charges when a hook uses one.
///
/// The other half of the hierarchy is Aura, which a Totem projects onto whoever stands in range.
/// Everything a StatusEffect can do an Aura can do too - the split is about ownership and lifetime,
/// not capability. What only exists here is the bookkeeping that belongs to being *owned*: merging
/// when the same type is applied twice, and the factory that turns a StatusType into the object
/// implementing it.
///
/// StatusType is the type and this is the copy, the same split as CardData/Card - the enum says what a
/// Poison is, this says how much of it is on this particular goblin and for how long.
///
/// Two independent ways to expire, and one may use either, both, or neither:
///
///   duration  turnsRemaining ticks down at the end of the carrier's own turn, and it drops at 0.
///                                                              Poison, Frozen, Rooted.
///   charge    an event spends a stack.        DoubleNextAttack, Block, Parry, Shield.
///   neither   Indefinite, lasts the whole combat.                          Strength.
///
/// Keeping those separate is what lets one class cover all of them. Folding duration into stacks - the
/// Slay the Spire trick where poison's stack count doubles as its remaining turns - would force
/// Strength and Poison into different storage.
/// </summary>
public abstract class StatusEffect : Status
{
    private readonly StatusType statusType;

    public override StatusType type => statusType;

    public override int stacks { get; set; }

    public override int turnsRemaining { get; set; }

    protected StatusEffect(StatusType type, int stacks, int turnsRemaining)
    {
        statusType = type;
        this.stacks = stacks;
        this.turnsRemaining = turnsRemaining;
    }

    /// <summary>
    /// Folds a fresh application of this same type into the one already present.
    ///
    /// Only carried statuses merge - an Aura is owned by its totem and rebuilt per query, so there is
    /// nothing to fold into. That is why this lives here rather than on Status.
    ///
    /// Takes the *longer* of the two durations so a top-up can never shorten what is already there -
    /// and Indefinite, being -1, has to be special-cased or Mathf.Max would treat it as the shortest.
    /// </summary>
    public virtual void Merge(StatusEffect incoming)
    {
        stacks += incoming.stacks;

        if (turnsRemaining == Indefinite) { return; }

        turnsRemaining = incoming.turnsRemaining == Indefinite
            ? Indefinite
            : Mathf.Max(turnsRemaining, incoming.turnsRemaining);
    }

    /// <summary>
    /// The one place a StatusType turns into the object that implements it. Returns null for None, and
    /// for anything not yet implemented, so an unset dropdown does nothing rather than throwing.
    ///
    /// Block is the awkward one: it needs a per-hit amount *and* a charge count, and this signature
    /// only carries one number. Here `stacks` is read as the per-hit amount for a single hit, which is
    /// the sensible reading of "apply Block 5". A card that wants Block 5 three times over uses
    /// BlockEffect, which has both fields.
    /// </summary>
    public static StatusEffect Create(StatusType type, int stacks, int turnsRemaining) => type switch
    {
        StatusType.Strength => new StrengthStatus(stacks, turnsRemaining),
        StatusType.DoubleNextAttack => new DoubleNextAttackStatus(stacks, turnsRemaining),
        StatusType.Poison => new PoisonStatus(stacks, turnsRemaining),
        StatusType.Frozen => new FrozenStatus(stacks, turnsRemaining),
        StatusType.Rooted => new RootedStatus(stacks, turnsRemaining),
        StatusType.Shield => new ShieldStatus(stacks, turnsRemaining),
        StatusType.Block => new BlockStatus(stacks, 1, turnsRemaining),
        StatusType.Parry => new ParryStatus(stacks, turnsRemaining),
        StatusType.DoubleShield => new DoubleShieldStatus(stacks, turnsRemaining),
        StatusType.Dodge => new DodgeStatus(stacks, turnsRemaining),
        _ => null,
    };
}
