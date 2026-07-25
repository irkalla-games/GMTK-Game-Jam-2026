using System.Collections;
using UnityEngine;
using System.Collections.Generic;

public class DamageAction : GameAction
{
    private readonly int damageAmount;

    public DamageAction(int damageAmount)
    {
        this.damageAmount = damageAmount;
    }

    public override IEnumerator Execute(ActionContext ctx)
    {
        foreach (GridTile target in ctx.targets)
        {
            target.DealDamage(damageAmount);
        }
        yield return new WaitForSeconds(0.15f);
    }
}
