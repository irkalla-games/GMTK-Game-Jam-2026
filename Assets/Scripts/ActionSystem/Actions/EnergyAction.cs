using System.Collections;
using UnityEngine;

public class EnergyAction : GameAction
{
    private readonly int amount;

    public EnergyAction(int amount)
    {
        this.amount = amount;
    }

    public override IEnumerator Execute(ActionContext ctx)
    {
        if (ctx.source != null) { ctx.source.GainEnergy(amount); }

        yield return new WaitForSeconds(ResolveDelay);
    }
}
