using UnityEngine;

/// <summary>
/// An impassable tile. Refuses entry outright; anyone already standing there when it is cast is left
/// alone - it only ever gates a move, never evicts an occupant.
/// </summary>
public class WallOfForceTileEffect : TileEffect
{
    public override TileEffectType type => TileEffectType.WallOfForce;

    public override int turnsRemaining { get; set; }

    public WallOfForceTileEffect(int turns)
    {
        turnsRemaining = turns;
    }

    public override string EnterRefusal(Character mover, GridTile tile) =>
        "a wall of force blocks the way";

    public override void OnTurnEnd(GridTile tile)
    {
        turnsRemaining--;
    }

    public override string Describe() => $"Wall of Force ({turnsRemaining} turns left)";

    public override Color OverlayColor => new(0.55f, 0.62f, 0.85f, 0.55f);
}
