using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ParryAction : GameAction
{
    private readonly int parryCount;

    public ParryAction(int parryCount)
    {
        this.parryCount = parryCount;
    }

    // Parry negates a hit outright and reflects the amount it would have dealt back at the attacker -
    // see Character.TakeDamage.
    public override IEnumerator Execute(ActionContext ctx)
    {
        foreach (GridTile target in ctx.targets)
        {
            target.GainParry(parryCount);
        }
        yield return new WaitForSeconds(ResolveDelay);
    }
}
