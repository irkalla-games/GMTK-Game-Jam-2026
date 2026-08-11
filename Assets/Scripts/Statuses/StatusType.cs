/// <summary>
/// Every temporary modifier a character can carry. Buffs and curses are the same machinery - they
/// differ only in their numbers and in which side wants them.
///
/// These values are written into .asset files by ApplyStatusEffect, so they are load-bearing: append
/// new statuses at the end, never reorder. None = 0 so an effect asset whose dropdown was never set does
/// nothing loudly rather than silently granting whatever happened to be listed first. (That is the
/// opposite choice to RangeShape.Anywhere = 0, which had to keep pre-existing assets working - there
/// are no status assets yet, so 0 is free to mean "unset".)
///
/// The rule each one carries lives in its Status subclass, not in a switch somewhere - see Status.
/// </summary>
public enum StatusType
{
    None = 0,

    /// Buff. Adds its stack count to outgoing damage. Indefinite.
    Strength = 1,

    /// Buff. Doubles outgoing damage, then spends a charge. No duration - it waits.
    DoubleNextAttack = 2,

    /// Curse. Deals its stack count in damage at the end of the carrier's own phase, bypassing every
    /// defense - see Character.TakeUnblockableDamage.
    Poison = 3,

    /// Curse. The carrier loses its whole turn - no cards, no attack, no movement.
    Frozen = 4,

    /// A pool of extra health that absorbs damage ahead of Health, wiped at the start of every turn.
    /// stacks is the pool.
    Shield = 5,

    /// A flat reduction applied to each of the next few hits. stacks is the charge count; the
    /// per-hit amount lives on BlockStatus, which is why this one cannot be authored through the
    /// generic Apply Status asset - see BlockEffect.
    Block = 6,

    /// Negates each of the next few hits outright and reflects them at the attacker. stacks is the
    /// charge count.
    Parry = 7,

    /// Curse. The carrier cannot move. Everything else - attacking, playing cards - still works.
    /// Frozen's smaller sibling; the two are separate so they compose.
    Rooted = 8,

    DoubleShield = 9,

    /// Negates each of the next few hits outright and sidesteps to a nearby tile, no reflection.
    /// stacks is the charge count - see DodgeStatus and GridManager.StepAwayFrom.
    Dodge = 10,
}
