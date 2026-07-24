using System.Collections;
using UnityEngine;

public class HealAction : GameAction
{
    private readonly int healAmount;

    public HealAction(int healAmount)
    {
        this.healAmount = healAmount;
    }

    public override IEnumerator Execute(ActionContext ctx)
    {
        foreach (Tiles target in ctx.targets)
        {
            target.Heal(healAmount);
        }
        yield return new WaitForSeconds(0.15f);
    }
}
