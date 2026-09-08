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
/// There is one counter and the subclass decides what spends it - see Status for the two shapes.
/// This class used to hold a second field, turnsRemaining, and its own docstring argued that folding
/// duration into stacks "would force Strength and Poison into different storage". That was true only
/// while something *outside* the status did the ageing: once each status ages itself, Strength spends
/// its counter on its own attacks and Poison decays in its own OnTurnEnd, and one field covers both.
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
    /// Whether `incoming` - already known to be this same type - folds into this status, or stands
    /// beside it as its own.
    ///
    /// True for almost everything: one counter means one Poison pool, one Shield pool, one Frozen clock
    /// that a re-freeze extends. Weaken and Vulnerable answer it by comparing sizes, because they carry
    /// a size as well as a clock (see Status.Amount): two Weaken 3s are the same debuff twice and should
    /// simply last longer, while a Weaken 3 and a Weaken 5 are genuinely different and merging would
    /// have to invent one number for both. The ones that stay apart are ranked instead -
    /// Character.FindStatus hands back the biggest and the damage pipeline applies only that one.
    ///
    /// A comparison rather than a plain bool because the answer depends on the pair, not on the type:
    /// the same Weaken folds into one neighbour and not another. Character.AddStatus therefore keeps
    /// looking through the carried list rather than stopping at the first same-type entry.
    /// </summary>
    public virtual bool MergesWith(StatusEffect incoming) => true;

    /// <summary>
    /// The one place a StatusType turns into the object that implements it. Returns null for None, and
    /// for anything not yet implemented, so an unset dropdown does nothing rather than throwing.
    ///
    /// `amountBonus` is what the *applier's* equipment adds to the size of the status being built -
    /// Character.AppliedPotency's answer, threaded through by StatusAction for a card and Totem.Project
    /// for an aura. Only the types carrying a size read it (Weaken, Vulnerable); for everything else the
    /// size is either fixed or is the counter itself, so it goes unused and the default 0 keeps every
    /// existing caller building exactly what it did before.
    ///
    /// Block used to be the awkward one here, needing a per-hit amount as well as a charge count. Its
    /// amount is a constant still (BlockStatus.AmountPerHit), so it builds like everything else.
    ///
    /// Taunt is the one type still missing on purpose: it carries a reference to the character that
    /// applied it, which this signature has nowhere to put. TauntEffect builds it directly.
    /// </summary>
    public static StatusEffect Create(StatusType type, int stacks, int amountBonus = 0) => type switch
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
        StatusType.Weaken => new WeakenStatus(stacks, WeakenStatus.BaseAmount + amountBonus),
        StatusType.Summoned => new SummonedStatus(stacks),
        StatusType.PoisonBlade => new PoisonBladeStatus(stacks),
        StatusType.Stealth => new StealthStatus(stacks),
        StatusType.Vulnerable => new VulnerableStatus(stacks, VulnerableStatus.BaseAmount + amountBonus),
        StatusType.Regeneration => new RegenerationStatus(stacks),
        StatusType.Lifesteal => new LifestealStatus(stacks),

        // stacks is the damage returned rather than a duration - see ThornsStatus. That makes it the
        // one entry here where "3 stacks" reads as "3 damage back" instead of "3 turns".
        StatusType.Thorns => new ThornsStatus(stacks),

        StatusType.Pilfered => new PilferedStatus(stacks),
        StatusType.Sapped => new SappedStatus(stacks),

        // GainMultiplier, Potency and TurnTick are aura-only, same reason Taunt is missing here: each
        // needs a subject StatusType (and TurnTick a TurnTiming) that this signature has nowhere to
        // put. AuraData.CreateEffect builds them directly - see Totem.Project.
        _ => null,
    };
}
