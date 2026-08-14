/// <summary>
/// Anything that modifies a character by taking part in the combat rules. The base of two halves:
///
///   StatusEffect  the character carries it. Merges when re-applied, spends real charges, and ages
///                 itself if ageing is what its counter means.
///   Aura          a Totem projects it while the character stands in range. Rebuilt per query, so
///                 nothing it does to its own counter survives.
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
/// Three kinds of member, and the difference matters:
///
///   OnX        notifications. Something has happened; react to it.
///   XRefusal   gates. Asked *before*, repeatedly, and answered null-or-reason like every other
///              Refusal in this codebase.
///   query      answers "instead of what you were going to do, do this". ForcedQuarry is the only
///              one so far - it replaces a decision rather than permitting or reacting to it.
///
/// Hooks run in FIFO order: the order Character.ActiveStatuses returns them, which is auras first and
/// then the character's own in the order they were gained. There is no priority key, so mitigation is
/// not pinned to Parry -> Block -> Shield and the outgoing total depends on which buff landed first.
/// If that ever needs pinning, a `virtual int Order` here plus a stable sort in ActiveStatuses
/// restores it without touching any individual status.
///
/// **One number, and the status decides what spends it.** There is a single counter, `stacks`, and
/// what it counts is the subclass's business - magnitude, charges, or remaining turns. Nothing outside
/// a status ages it. That is what collapsed the old stacks/turnsRemaining pair: almost every status
/// left one of the two permanently inert, and the blanket "decrement everybody's duration" loop that
/// used to live in Character.OnTurnEnd was the last combat rule outside a subclass.
///
/// Three shapes, and every status is exactly one of them:
///
///   never ticks   the counter is a magnitude and nothing consumes it.              Strength.
///   charge-spent  the event the status reacts to spends one.
///                            Block, Parry, Dodge, Double Attack, Double Shield, Shield.
///   self-ticking  its own OnTurnStart/OnTurnEnd spends it, by decrementing (Poison, Frozen, Rooted,
///                 Taunt) or by zeroing outright (Weaken, Shield).
///
/// Shield is deliberately in two of those: its pool is drained by damage *and* wiped at turn start.
/// Poison is what proves the merge works - its counter is damage and duration at once, so it deals
/// what it says and then decays, which needs no second field.
/// </summary>
public abstract class Status
{
    /// Which modifier this is. A property rather than a field so an Aura can answer for the effect it
    /// is projecting instead of keeping a second copy that could drift.
    public abstract StatusType type { get; }

    /// <summary>
    /// The one number. What it counts depends on the subclass: magnitude for Strength and Weaken,
    /// remaining charges for the ones an event spends, remaining turns for the ones that age, and both
    /// at once for Poison.
    ///
    /// Still called `stacks` rather than something neutral like `counter` because Character.StatusStacks
    /// and the Glossary's "{stacks}" token are both built on the name, and every authored tooltip body
    /// in the Glossary asset spells it - renaming would mean re-authoring all of them for no
    /// behavioural gain.
    /// </summary>
    public abstract int stacks { get; set; }

    /// Nothing left to hold: a status is done when its counter runs out, whatever the counter meant.
    public bool IsExpired => stacks <= 0;

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

    /// <summary>
    /// Who this status forces its carrier to go after, overriding whatever its TargetingPattern would
    /// have named, or null if it does not redirect. Taunt.
    ///
    /// A third kind of member, alongside the OnX notifications and the XRefusal gates: a *query*, and
    /// the only one of the three that answers "instead of what you were going to do, do this". It is
    /// asked by TargetSelector.TryPick, which is the single place both the attack's victim and the
    /// movement quarry are chosen - so one answer here redirects the swing and the walk together.
    ///
    /// Returning a character the caller cannot use is fine and is the whole design: an attack asks with
    /// only the enemies it can legally hit as candidates, so a forced quarry that is out of reach comes
    /// back as "no attack this action point" rather than as a fallback to the pattern.
    ///
    /// On Status rather than on StatusEffect so an Aura can redirect too - a totem projecting a taunt
    /// cloud needs no further plumbing.
    /// </summary>
    public virtual Character ForcedQuarry(Character carrier) => null;

    /// The carrier is about to deal damage. Return the hit as this status leaves it - DamageInfo is
    /// immutable, so a status that has nothing to say returns what it was given.
    public virtual DamageInfo OnDealDamage(DamageInfo info) => info;

    /// The carrier is being hit. Return it reduced, or negated outright - see DamageInfo.
    public virtual DamageInfo OnTakeDamage(DamageInfo info) => info;

    /// The carrier is about to gain shield. Return it doubled, reduced, or untouched - see ShieldInfo.
    /// Only reaches statuses on the carrier receiving the shield; there is no "dealing" side to this
    /// one the way OnDealDamage has an attacker, since nobody swings to grant shield.
    public virtual ShieldInfo OnGainShield(ShieldInfo info) => info;

    /// The top of a round, before anybody acts. Shield wipes itself here.
    public virtual void OnTurnStart(Character carrier) { }

    /// <summary>
    /// The end of the carrier's own phase. Poison bites here, and every status that ages does its own
    /// ageing here - there is no separate pass afterwards.
    ///
    /// A status that ages does both jobs in this one method, in that order: do the thing, then spend
    /// the counter. Poison deals `stacks` and *then* decrements, which is what gives a status with one
    /// turn left its last tick. Character.PruneExpired drops whatever hit zero.
    /// </summary>
    public virtual void OnTurnEnd(Character carrier) { }

    /// <summary>
    /// What a UI shows for this status, without its duration - the caller appends that, so every
    /// status displays it the same way.
    ///
    /// Virtual because the numbers do not all mean the same thing: Block has two of them, and a binary
    /// status like Frozen has none worth showing.
    /// </summary>
    public virtual string Describe() => $"{type} x{stacks}";

    /// <summary>
    /// The same job as Describe(), but filling numbers into a sentence somebody else authored - the
    /// tooltip's "Negates the damage for {stacks} hits and reflects it back".
    ///
    /// Virtual for the same reason Describe() is, and it is the whole reason the caller does not just
    /// run string.Format itself: only BlockStatus knows it has a second number, and only it should have
    /// to. A status with nothing extra to say fills {stacks} and hands the rest back untouched.
    ///
    /// Tokens it does not recognise are left in place on purpose - Glossary fills whatever survives
    /// from the authored defaults, so a card describing a status the character does not carry still
    /// reads as a sentence.
    /// </summary>
    public virtual string Describe(string template)
    {
        return template == null ? null : template.Replace(Glossary.StacksToken, stacks.ToString());
    }
}
