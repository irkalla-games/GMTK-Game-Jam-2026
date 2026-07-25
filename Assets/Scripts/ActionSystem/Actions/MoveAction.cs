using NUnit.Framework;
using System;
using System.Collections;
using UnityEngine;

public class MoveAction : GameAction
{
    public override IEnumerator Execute(ActionContext ctx)
    {
        if (ctx.targets.Count > 1 || ctx.targets.Count == 0)
        {
            throw new ArgumentException("This has to be 1 Tile");
        }
        else
        {
            ctx.source.Tile.MoveCharacter(ctx.targets[0]);
            yield return new WaitForSeconds(0.15f);
        }
    }
}
