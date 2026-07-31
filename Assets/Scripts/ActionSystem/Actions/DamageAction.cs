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
        // Resolved once, before the loop. Strength and Double Attack belong to the attacker, not to
        // any one tile, and shouldConsume spends the Double Attack charge - so an AoE must not run
        // this per tile, or the buff would be spent several times and only the first tile doubled.
        //
        // Computed here rather than passed down to GridTile.DealDamage: a tile forwards effects onto
        // its occupant and has no business knowing who swung.
        int amount = ctx.source != null
            ? ctx.source.ComputeOutgoingDamage(damageAmount, shouldConsume: true)
            : damageAmount;

        foreach (GridTile target in ctx.targets)
        {
            target.DealDamage(amount, ctx.source);
        }

        yield return new WaitForSeconds(ResolveDelay);
    }
}
