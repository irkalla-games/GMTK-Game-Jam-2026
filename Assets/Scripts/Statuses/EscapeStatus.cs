using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Blinks the carrier to the safest tile on the board each time its health crosses another share of
/// the bar - the Evil Wizard refusing to be pinned down, escaping at each third it loses.
///
/// "Safest" is the free tile furthest from the nearest living hero, which is what makes this read as
/// fleeing rather than teleporting at random. It ignores walls and bodies entirely, unlike a Move
/// card: this is a blink, and being uncatchable for a turn is the whole point of it.
/// </summary>
public class EscapeStatus : ThresholdStatus
{
    public EscapeStatus(int triggers = 2) : base(StatusType.Escaping, triggers) { }

    protected override void OnThresholdCrossed(Character carrier)
    {
        if (carrier.IsDead || carrier.Tile == null || GridManager.Instance == null) { return; }

        GridTile refuge = FurthestFreeTile(carrier);

        if (refuge == null) { return; }

        // PlaceCharacter, not MoveCharacter: a blink snaps without walking a route and without the
        // move rules a walk would be gated on. See GridManager's split of the two.
        GridManager.Instance.PlaceCharacter(carrier, refuge.Coordinates);

        Debug.Log($"{carrier.name} escaped to {refuge.Coordinates}");
    }

    /// The free tile whose nearest hunter is furthest away. Null when nowhere beats where the carrier
    /// already stands, which leaves the crossing spent but the body still - better than a teleport
    /// that visibly goes nowhere.
    private static GridTile FurthestFreeTile(Character carrier)
    {
        List<Character> hunters = TargetSelector.LivingEnemiesOf(carrier);

        if (hunters.Count == 0) { return null; }

        GridTile best = null;
        int bestDistance = Safety(carrier.Tile, hunters);

        foreach (GridTile tile in GridManager.Instance.AllTiles)
        {
            if (tile == null || !GridManager.Instance.IsTileAvailable(tile.Coordinates)) { continue; }

            int distance = Safety(tile, hunters);

            if (distance <= bestDistance) { continue; }

            best = tile;
            bestDistance = distance;
        }

        return best;
    }

    /// How far the nearest hunter is from `tile` - the number the escape maximises.
    private static int Safety(GridTile tile, List<Character> hunters)
    {
        int nearest = int.MaxValue;

        foreach (Character hunter in hunters)
        {
            if (hunter == null || hunter.Tile == null) { continue; }

            int distance = Board.ChebyshevDistance(tile.Coordinates, hunter.Tile.Coordinates);

            if (distance < nearest) { nearest = distance; }
        }

        return nearest;
    }

    public override string Describe() => "Blinks away as its health falls";
}
