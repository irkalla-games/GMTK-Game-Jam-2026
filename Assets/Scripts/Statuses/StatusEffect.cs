/// <summary>
/// A status a character carries itself: applied by a card, held in its own list, spending real charges
/// when a hook uses one, and doing its own ageing if ageing is what its counter means.
///
/// The other half of the hierarchy is Aura, which a Totem projects onto whoever stands in range.
/// Everything a StatusEffect can do an Aura can do too - the split is about ownership and lifetime,
/// not capability. What only exists here is the bookkeeping that belongs to being *owned*: merging
/// when the same type is applied twice, and the factory that turns a StatusType into the object
/// implementing it.
///
/// StatusType is the type and this is the copy, the same split as CardData/Card - the enum says what a
/// Poison is, this says how much of it is on this particular goblin.
///
/// There is one counter and the subclass decides what spends it - see Status for the three shapes.
/// This class used to hold a second field, turnsRemaining, and its own docstring argued that folding
/// duration into stacks "would force Strength and Poison into different storage". That was true only
/// while something *outside* the status did the ageing: once each status ages itself, Strength simply
/// never ticks and Poison decays in its own OnTurnEnd, and one field covers both.
/// </summary>
public abstract class StatusEffect : Status
{
    private readonly StatusType statusType;

    public override StatusType type => statusType;

    public override int stacks { get; set; }

    protected StatusEffect(StatusType type, int stacks)
    {
        statusType = type;
        this.stacks = stacks;
    }

    /// <summary>
    /// Folds a fresh application of this same type into the one already present.
    ///
    /// Only carried statuses merge - an Aura is owned by its totem and rebuilt per query, so there is
    /// nothing to fold into. That is why this lives here rather than on Status.
    ///
    /// Adding is the only reading that works for one counter, and it means duration statuses now
    /// *extend* where the two-field version took the longer of the two: freezing an already-frozen
    /// enemy gives two turns rather than one. A status wanting something else overrides this, as
    /// TauntStatus does to replace rather than accumulate.
    /// </summary>
    public virtual void Merge(StatusEffect incoming)
    {
        stacks += incoming.stacks;
    }

    /// <summary>
    /// The one place a StatusType turns into the object that implements it. Returns null for None, and
    /// for anything not yet implemented, so an unset dropdown does nothing rather than throwing.
    ///
    /// Block used to be the awkward one here, needing a per-hit amount as well as a charge count. Its
    /// amount is a constant now (BlockStatus.AmountPerHit), so it builds like everything else.
    ///
    /// Taunt is the one type still missing on purpose: it carries a reference to the character that
    /// applied it, which this signature has nowhere to put. TauntEffect builds it directly.
    /// </summary>
    public static StatusEffect Create(StatusType type, int stacks) => type switch
    {
        StatusType.Strength => new StrengthStatus(stacks),
        StatusType.DoubleNextAttack => new DoubleNextAttackStatus(stacks),
        StatusType.Poison => new PoisonStatus(stacks),
        StatusType.Frozen => new FrozenStatus(stacks),
        StatusType.Rooted => new RootedStatus(stacks),
        StatusType.Shield => new ShieldStatus(stacks),
        StatusType.Block => new BlockStatus(stacks),
        StatusType.Parry => new ParryStatus(stacks),
        StatusType.DoubleShield => new DoubleShieldStatus(stacks),
        StatusType.Dodge => new DodgeStatus(stacks),
        StatusType.Weaken => new WeakenStatus(stacks),
        StatusType.Summoned => new SummonedStatus(stacks),
        _ => null,
    };
}
