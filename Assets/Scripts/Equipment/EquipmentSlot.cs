/// <summary>
/// Where a piece of equipment is worn. Ring is unlimited - a character may carry any number at once -
/// while every other slot holds exactly one item, and equipping a second replaces the first. See
/// EquipmentSlots.IsUnlimited and Character.Equip.
///
/// Ring = 0 on purpose, the same reasoning as RangeShape.Anywhere and CharacterClass.Any: the 8
/// EquipmentData assets authored before this field existed deserialize to 0, and Ring is the
/// permissive value - no replacement, no limit - so an un-migrated asset keeps behaving exactly as it
/// does today instead of silently claiming a slot and evicting something. Append new slots, never
/// reorder; these ints are written into every EquipmentData asset on disk.
/// </summary>
public enum EquipmentSlot
{
    Ring = 0,
    Weapon = 1,
    Armor = 2,
    Hat = 3,
    Boots = 4,
}

/// <summary>
/// One place for the Ring-is-unlimited rule rather than an `== EquipmentSlot.Ring` comparison
/// scattered at every call site.
/// </summary>
public static class EquipmentSlots
{
    public static bool IsUnlimited(EquipmentSlot slot) => slot == EquipmentSlot.Ring;
}
