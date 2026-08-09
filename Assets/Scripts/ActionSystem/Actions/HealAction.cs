using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HealAction : GameAction
{
    private readonly int healAmount;

    public HealAction(int healAmount)
    {
        this.healAmount = healAmount;
    }

    protected override AnimationCue Cue(ActionContext ctx) => AnimationCue.Cast;

    public override IEnumerator Execute(ActionContext ctx)
    {
        yield return Perform(ctx);

        foreach (GridTile target in ctx.targets)
        {
            target.Heal(healAmount);
        }
        yield return new WaitForSeconds(ResolveDelay);
    }
}
