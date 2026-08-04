/// <summary>
/// Anything that modifies a character by taking part in the combat rules. The base of two halves:
///
///   StatusEffect  the character carries it. Ages, merges when re-applied, spends real charges.
///   Aura          a Totem projects it while the character stands in range. No duration of its own.
///
/// Both have exactly the same capabilities - every hook below is available to either, so a curse
/// cloud around a totem can poison you the same way a poison dart can. The only difference is
/// ownership and lifetime, which is why the split is here rather than a bool on one class.
///
/// A status is a *behaviour*, not a record. Everything a status does to the game lives in its own
/// subclass, reached through the hooks below. The alternative - a plain record plus a switch inside
/// Character - is what this replaced: TakeDamage hard-coded the Parry/Block/Shield order,
/// ComputeOutgoingDamage hard-coded Strength and Double Attack, and every new status meant editing
/// Character again.
///
/// Two kinds of member, and the difference matters:
///
///   OnX        notifications. Something has happened; react to it.
///   XRefusal   gates. Asked *before*, repeatedly, and answered null-or-reason like every other
///              Refusal in this codebase.
///
/// Hooks run in FIFO order: the order Character.ActiveStatuses returns them, which is auras first and
/// then the character's own in the order they were gained. There is no priority key, so mitigation is
/// not pinned to Parry -> Block -> Shield and the outgoing total depends on which buff landed first.
/// If that ever needs pinning, a `virtual int Order` here plus a stable sort in ActiveStatuses
/// restores it without touching any individual status.
/// </summary>
public abstract class Status
{
    /// A turnsRemaining that never ticks down.
    public const int Indefinite = -1;

    /// Which modifier this is. A property rather than a field so an Aura can answer for the effect it
    /// is projecting instead of keeping a second copy that could drift.
    public abstract StatusType type { get; }

    /// Magnitude for most statuses, remaining charges for the ones spent by an event.
    public abstract int stacks { get; set; }

    public abstract int turnsRemaining { get; set; }

    public bool IsExpired => stacks <= 0 || turnsRemaining == 0;

    /// <summary>
    /// Why this status stops its carrier doing anything at all, or null if it does not object. Frozen.
    ///
    /// A gate, not a notification, which is why it is not one of the OnX hooks. Those fire when
    /// something has already happened; this is asked *before*, over and over - on every click, by the
    /// turn loop each frame, and by each enemy before it acts. It also has to answer correctly the
    /// instant it is applied: freezing an enemy during PlayerActing must deny the action it takes in
    /// EnemyResolve that same round, which a turn-start hook could not do because it already ran.
    /// </summary>
    public virtual string ActRefusal(Character carrier) => null;

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

    /// The carrier is about to deal damage. Return the hit as this status leaves it - DamageInfo is
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
    /// What a UI shows for this status, without its duration - the caller appends that, so every
    /// status displays it the same way.
    ///
    /// Virtual because the numbers do not all mean the same thing: Block has two of them, and a binary
    /// status like Frozen has none worth showing.
    /// </summary>
    public virtual string Describe() => $"{type} x{stacks}";
}
