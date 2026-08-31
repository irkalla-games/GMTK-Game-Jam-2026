using System.Collections;
using UnityEngine;

/// <summary>
/// Strips every carried Poison stack off each target, then either afflicts the nearest living enemy
/// of that target with the same number of stacks, or heals the target for it - see
/// CleansePoisonEffect, the authoring half this mirrors.
/// </summary>
public class CleansePoisonAction : GameAction
{
    private readonly bool giveToNearestEnemy;

    public CleansePoisonAction(bool giveToNearestEnemy)
    {
        this.giveToNearestEnemy = giveToNearestEnemy;
    }

    protected override AnimationCue Cue(ActionContext ctx) => AnimationCue.Cast;

    public override IEnumerator Execute(ActionContext ctx)
    {
        yield return Perform(ctx);

        foreach (GridTile target in ctx.targets)
        {
            Character occupant = target.Occupant;

            if (occupant == null) { continue; }

            int removed = occupant.RemoveStatus(StatusType.Poison);

            if (removed <= 0) { continue; }

            if (giveToNearestEnemy)
            {
                GiveToNearestEnemy(occupant, removed);
            }
            else
            {
                occupant.Heal(removed);
            }
        }

        yield return new WaitForSeconds(ResolveDelay);
    }

    /// Finds the enemy nearest the cleansed ally's own tile - not the caster's - and drops the
    /// removed Poison stacks on it. Reuses Board.TryNearestEnemy, the same walk-target picker enemy
    /// brains already use, over a fresh GridManager.Read() snapshot.
    private static void GiveToNearestEnemy(Character source, int stacks)
    {
        if (GridManager.Instance == null || source.Tile == null) { return; }

        Board board = GridManager.Instance.Read();

        if (!board.TryNearestEnemy(source.Tile.Coordinates, source.Affiliation, out Vector2Int nearest))
        {
            return;
        }

        GridTile nearestTile = GridManager.Instance.GetTile(nearest);

        if (nearestTile != null) { nearestTile.ApplyStatus(StatusType.Poison, stacks); }
    }
}
