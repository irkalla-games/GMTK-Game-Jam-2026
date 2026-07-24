using System.Collections;
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
        foreach (Tiles target in ctx.targets)
        {
            target.gainParry(reflectTotal, parryCount);
        }
        yield return new WaitForSeconds(0.15f);
    }
}
