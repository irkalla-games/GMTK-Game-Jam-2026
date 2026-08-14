using UnityEngine;

/// <summary>
/// A patch of fire. Damages whoever is standing on the tile at the end of the player turn, bypassing
/// every defense - the same TakeUnblockableDamage Poison bites with, since this is something the tile
/// is doing to you, not an attack any Shield/Block/Parry was ever raised against. Does not refuse
/// entry - standing in flames is legal, it just costs you.
/// </summary>
public class WallOfFlamesTileEffect : TileEffect
{
    private readonly int magnitude;

    public override TileEffectType type => TileEffectType.WallOfFlames;

    public override int turnsRemaining { get; set; }

    public WallOfFlamesTileEffect(int turns, int magnitude)
    {
        turnsRemaining = turns;
        this.magnitude = magnitude;
    }

    public override void OnTurnEnd(GridTile tile)
    {
        // Bite first, decay second - the same order Poison uses, so a wall with one turn left still
        // deals its last tick before expiring.
        if (tile != null && tile.Occupant != null) { tile.Occupant.TakeUnblockableDamage(magnitude); }

        turnsRemaining--;
    }

    public override string Describe() => $"Wall of Flames ({turnsRemaining} turns left)";

    public override Color OverlayColor => new(1f, 0.35f, 0.1f, 0.55f);
}
