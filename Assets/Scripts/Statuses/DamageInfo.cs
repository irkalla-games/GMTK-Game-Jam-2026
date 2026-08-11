using UnityEngine;

/// <summary>
/// One damage event on its way through the status hooks.
///
/// Immutable, and passed *through* the hooks rather than handed to them: each status receives one of
/// these and returns the next, so `info = status.OnTakeDamage(info)` is the whole pipeline. A status
/// therefore cannot quietly half-modify a shared object, and a hook that forgets to account for
/// something returns the value it was given rather than leaving a partly-written one behind.
///
/// A readonly struct rather than a class because these are produced one per status per hit and
/// discarded immediately - by value there is nothing to collect. Same reasoning TargetRange documents
/// for being a value type.
///
/// Used in both directions. On the way out (Character.ComputeOutgoingDamage) `attacker` is the carrier
/// and `target` is null - nobody has been picked yet, because an AoE resolves this once for every tile
/// it is about to hit. On the way in (Character.TakeDamage) `target` is the carrier and `attacker` is
/// whoever swung, or null for damage with no author.
/// </summary>
public readonly struct DamageInfo
{
    /// Who swung. Null for sourceless damage, which is also what makes a parried hit vanish instead of
    /// reflecting - there is nobody to reflect it at.
    public readonly Character attacker;

    /// Who is being hit. Null while computing outgoing damage.
    public readonly Character target;

    /// The running total, as it stands after every hook so far.
    public readonly int amount;

    /// Damage to throw back at the attacker once the hooks have finished. Parry fills this in;
    /// Character.TakeDamage is what actually delivers it, because a status has no business deciding
    /// when in the sequence the counter-hit lands.
    public readonly int reflected;

    /// Set by a status that cancelled the hit outright. Stops the remaining hooks - there is nothing
    /// left for Block or Shield to reduce.
    public readonly bool negated;

    /// False when somebody is only *looking* at this number - a tooltip, a damage preview, an enemy
    /// brain scoring a move it has not made yet. Statuses that spend a charge must check this first,
    /// or displaying a number would destroy the buff without an attack ever happening.
    public readonly bool consumeCharges;

    /// <summary>
    /// How much of this hit a Shield status has absorbed into its own pool so far. Tracked separately
    /// from `amount` - which Shield reduces exactly like Block does, there is no other difference in
    /// this pipeline - because a floating damage number wants to credit Shield-absorbed damage as
    /// having landed (it spent a real resource, the shield's pool), while a Block reduction or a Parry
    /// negation simply prevents damage and leaves nothing to show for it. See
    /// Character.DamageRegistered.
    /// </summary>
    public readonly int shieldAbsorbed;

    public DamageInfo(Character attacker, Character target, int amount, bool consumeCharges = true)
        : this(attacker, target, amount, reflected: 0, negated: false, consumeCharges, shieldAbsorbed: 0) { }

    private DamageInfo(Character attacker, Character target, int amount, int reflected, bool negated,
                       bool consumeCharges, int shieldAbsorbed)
    {
        this.attacker = attacker;
        this.target = target;
        this.amount = amount;
        this.reflected = reflected;
        this.negated = negated;
        this.consumeCharges = consumeCharges;
        this.shieldAbsorbed = shieldAbsorbed;
    }

    /// The same hit carrying a different number. Everything else rides along untouched, which is what
    /// keeps a status from having to know what the other fields are for.
    public DamageInfo WithAmount(int newAmount) =>
        new(attacker, target, newAmount, reflected, negated, consumeCharges, shieldAbsorbed);

    /// Reduced by a flat amount, never past zero. Block.
    public DamageInfo Reduced(int reduction) => WithAmount(Mathf.Max(0, amount - reduction));

    /// Cancelled outright, with what it would have dealt queued up to go back at the attacker. Parry.
    public DamageInfo Reflected() =>
        new(attacker, target, amount: 0, reflected + amount, negated: true, consumeCharges, shieldAbsorbed);

    /// Cancelled outright with nothing thrown back - Dodge. Unlike Reflected(), `reflected` is left
    /// untouched: sidestepping a hit is not a counter-attack.
    public DamageInfo Negated() =>
        new(attacker, target, amount: 0, reflected, negated: true, consumeCharges, shieldAbsorbed);

    /// The portion of this hit a Shield status just absorbed into its own pool - reduces `amount` like
    /// any other mitigation, but also credits `shieldAbsorbed` so TakeDamage can still count it as
    /// having registered. ShieldStatus.OnTakeDamage is the only caller.
    public DamageInfo AbsorbedByShield(int amount) =>
        new(attacker, target, this.amount - amount, reflected, negated, consumeCharges,
            shieldAbsorbed + amount);
}
