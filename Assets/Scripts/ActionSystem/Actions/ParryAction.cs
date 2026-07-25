using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ParryAction : GameAction
{
    private readonly int reflectTotal;
    private readonly int parryCount;

    public ParryAction(int reflectTotal, int parryCount)
    {
        this.reflectTotal = reflectTotal;
        this.parryCount = parryCount;
    }

    //Parry negates damage and reflects some amount of it back.
    public override IEnumerator Execute(ActionContext ctx)
    {
        foreach (GridTile target in ctx.targets)
        {
            target.GainParry(reflectTotal, parryCount);
        }
        yield return new WaitForSeconds(0.15f);
    }
}
