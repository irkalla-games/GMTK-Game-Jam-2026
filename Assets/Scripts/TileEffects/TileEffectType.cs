/// <summary>
/// Every persistent effect a tile itself can carry, independent of whoever is standing on it.
///
/// These values are written into .asset files by ApplyTileEffect, so they are load-bearing: append new
/// kinds at the end, never reorder - same hazard as StatusType. None = 0 for the same reason StatusType
/// is: an effect asset whose dropdown was never set does nothing loudly rather than silently applying
/// whatever happened to be listed first.
///
/// The rule each one carries lives in its TileEffect subclass, not in a switch somewhere - see
/// TileEffect.
/// </summary>
public enum TileEffectType
{
    None = 0,

    /// Refuses entry to anyone trying to step onto the tile. Nothing already standing there is moved.
    WallOfForce = 1,

    /// Damages whoever is standing on the tile at the end of the player turn, bypassing every defense -
    /// see Character.TakeUnblockableDamage.
    WallOfFlames = 2,
}
