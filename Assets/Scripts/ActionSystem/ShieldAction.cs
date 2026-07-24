using System.Collections;
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
        foreach (Tiles target in ctx.targets)
        {
            target.addShield(shieldAmount);
        }
        yield return new WaitForSeconds(0.15f);
    }
}
