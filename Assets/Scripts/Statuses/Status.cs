using UnityEngine;

/// <summary>
/// One status on one character, and the rule it carries.
///
/// StatusType is the type and this is the copy, the same split as CardData/Card - the enum says what a
/// Poison is, this says how much of it is on this particular goblin and for how long.
///
/// A status is a *behaviour*, not a record. Everything a status does to the game lives in its own
/// subclass, reached through the hooks below. The alternative - a plain record plus a switch inside
/// Character - is what this replaced: TakeDamage hard-coded the Parry/Block/Shield order,
/// ComputeOutgoingDamage hard-coded Strength and Double Attack, and every new status meant editing
/// Character again.
///
/// Two independent ways to expire, and a status may use either, both, or neither:
///
///   duration  turnsRemaining ticks down at the end of the carrier's own turn, and the status drops
///             at 0.                                                     Poison, Frozen, Rooted.
///   charge    an event spends a stack.               DoubleNextAttack, Block, Parry, Shield.
///   neither   Indefinite, lasts the whole combat.                                    Strength.
///
/// Keeping those separate is what lets one class cover all of them. Folding duration into stacks - the
/// Slay the Spire trick where poison's stack count doubles as its remaining turns - would force
/// Strength and Poison into different storage.
///
/// Hooks run in FIFO order: the order the statuses sit on the character, auras first. There is no
/// priority key, so mitigation is not pinned to Parry -> Block -> Shield and the outgoing total
/// depends on which buff landed first. If that ever needs pinning, a `virtual int Order` here plus a
/// stable sort in Character.ActiveStatuses restores it without touching any individual status.
/// </summary>
public abstract class Status
{
    /// A turnsRemaining that never ticks down.
    public const int Indefinite = -1;

    public readonly StatusType type;

    /// Magnitude for most statuses, remaining charges for the ones spent by an event.
    public int stacks;

    public int turnsRemaining;

    protected Status(StatusType type, int stacks, int turnsRemaining)
    {
        this.type = type;
        this.stacks = stacks;
        this.turnsRemaining = turnsRemaining;
    }

    public bool IsExpired => stacks <= 0 || turnsRemaining == 0;

    /// <summary>
    /// Why this status stops its carrier doing anything at all, or null if it does not object. Frozen.
    ///
    /// A gate, not a notification, which is why it is not one of the OnX hooks below. Those fire when
    /// something has already happened; this is asked *before*, over and over - on every click, by the
    /// turn loop each frame, and by each enemy before it acts. It also has to answer correctly the
    /// instant it is applied: freezing an enemy during PlayerActing must deny the action it takes in
    /// EnemyResolve that same round, which a turn-start hook could not do because it already ran.
    /// </summary>
    public virtual string ActRefusal(Character carrier) => null;

    /// The carrier is about to deal damage. Return the hit as this status leaves it - `info` is
    /// immutable, so a status that has nothing to say returns what it was given.
    public virtual DamageInfo OnDealDamage(DamageInfo info) => info;

    /// The carrier is being hit. Return it reduced, or negated outright - see DamageInfo.
    public virtual DamageInfo OnTakeDamage(DamageInfo info) => info;

    /// The top of a round, before anybody acts. Shield wipes itself here.
    public virtual void OnTurnStart(Character carrier) { }

    /// The end of the carrier's own phase. Poison bites here. Durations age *after* this runs, so a
    /// status with one turn left still gets its last tick.
    public virtual void OnTurnEnd(Character carrier) { }

    /// <summary>
    /// Why this status stops its carrier stepping onto `destination`, or null if it does not object.
    /// Rooted. The other gate, same shape as ActRefusal.
    ///
    /// A refusal rather than an "OnMove" notification because the answer is needed *before* the move:
    /// GridManager.MoveRefusal is what MoveEffect.Refusal asks, which is what gates the click above the
    /// commit point and what builds the tile highlight. A hook that only fired after the fact could not
    /// refuse anything.
    /// </summary>
    public virtual string MoveRefusal(Character carrier, GridTile destination) => null;

    /// <summary>
    /// Folds a fresh application of this same type into the one already present.
    ///
    /// Takes the *longer* of the two durations so a top-up can never shorten what is already there -
    /// and Indefinite, being -1, has to be special-cased or Mathf.Max would treat it as the shortest.
    /// </summary>
    public virtual void Merge(Status incoming)
    {
        stacks += incoming.stacks;

        if (turnsRemaining == Indefinite) { return; }

        turnsRemaining = incoming.turnsRemaining == Indefinite
            ? Indefinite
            : Mathf.Max(turnsRemaining, incoming.turnsRemaining);
    }

    /// <summary>
    /// What a UI shows for this status, without its duration - the caller appends that, so every
    /// status displays it the same way.
    ///
    /// Virtual because the numbers do not all mean the same thing: Block has two of them, and a
    /// binary status like Frozen has none worth showing.
    /// </summary>
    public virtual string Describe() => $"{type} x{stacks}";

    /// <summary>
    /// The one place a StatusType turns into the object that implements it. Returns null for None, and
    /// for anything not yet implemented, so an unset dropdown does nothing rather than throwing.
    ///
    /// Block is the awkward one: it needs a per-hit amount *and* a charge count, and this signature
    /// only carries one number. Here `stacks` is read as the per-hit amount for a single hit, which is
    /// the sensible reading of "apply Block 5". A card that wants Block 5 three times over uses
    /// BlockEffect, which has both fields.
    /// </summary>
    public static Status Create(StatusType type, int stacks, int turnsRemaining) => type switch
    {
        StatusType.Strength => new StrengthStatus(stacks, turnsRemaining),
        StatusType.DoubleNextAttack => new DoubleNextAttackStatus(stacks, turnsRemaining),
        StatusType.Poison => new PoisonStatus(stacks, turnsRemaining),
        StatusType.Frozen => new FrozenStatus(stacks, turnsRemaining),
        StatusType.Rooted => new RootedStatus(stacks, turnsRemaining),
        StatusType.Shield => new ShieldStatus(stacks, turnsRemaining),
        StatusType.Block => new BlockStatus(stacks, 1, turnsRemaining),
        StatusType.Parry => new ParryStatus(stacks, turnsRemaining),
        _ => null,
    };
}
