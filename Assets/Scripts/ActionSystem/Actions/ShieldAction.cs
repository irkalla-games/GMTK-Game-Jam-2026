using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShieldAction : GameAction
{
    private readonly int shieldAmount;

    public ShieldAction(int shieldAmount)
    {
        this.shieldAmount = shieldAmount;
    }

    public override IEnumerator Execute(ActionContext ctx)
    {
        foreach (GridTile target in ctx.targets)
        {
            target.GainShield(shieldAmount);
        }
        yield return new WaitForSeconds(ResolveDelay);
    }
}
