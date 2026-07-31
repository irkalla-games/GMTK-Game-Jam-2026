using System.Collections;
using UnityEngine;

public class SummonAction : GameAction
{
    private GameObject summonObject;
    public SummonAction(GameObject summonObject)
    {
        this.summonObject = summonObject;
    }
    public override IEnumerator Execute(ActionContext ctx)
    {
        foreach(GridTile target in ctx.targets)
        {
            target.SummonObject(summonObject);
        }

        yield return new WaitForSeconds(ResolveDelay);
    }
}
