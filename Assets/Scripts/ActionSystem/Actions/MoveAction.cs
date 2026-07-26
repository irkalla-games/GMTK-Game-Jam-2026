using System;
using System.Collections;
using UnityEngine;

public class MoveAction : GameAction
{
    public override IEnumerator Execute(ActionContext ctx)
    {
        if (ctx.targets.Count != 1)
        {
            throw new ArgumentException("Move requires exactly one target tile");
        }

        // GridManager owns the move: it guards against occupied tiles, swaps occupancy, and tweens
        // the character's transform. Going through Character.MoveTo alone would only swap references.
        GridManager.Instance.MoveCharacter(ctx.source, ctx.targets[0]);
        yield return new WaitForSeconds(ResolveDelay);
    }
}
