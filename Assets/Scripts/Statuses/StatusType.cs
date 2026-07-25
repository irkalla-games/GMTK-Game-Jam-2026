/// <summary>
/// Every temporary modifier a character can carry. Buffs and curses are the same machinery - they
/// differ only in their numbers and in which side wants them.
///
/// These values are written into .asset files by StatusEffect, so they are load-bearing: append new
/// statuses at the end, never reorder. None = 0 so an effect asset whose dropdown was never set does
/// nothing loudly rather than silently granting whatever happened to be listed first. (That is the
/// opposite choice to RangeShape.Anywhere = 0, which had to keep pre-existing assets working - there
/// are no status assets yet, so 0 is free to mean "unset".)
/// </summary>
public enum StatusType
{
    None = 0,

    /// Buff. Adds its stack count to outgoing damage. Indefinite.
    Strength = 1,

    /// Buff. Doubles outgoing damage, then spends a charge. No duration - it waits.
    DoubleNextAttack = 2,

    /// Curse. Deals its stack count in damage at the start of each turn, bypassing armor.
    Poison = 3,

    /// Curse. The character cannot act at all while any stack remains.
    Frozen = 4,
}
