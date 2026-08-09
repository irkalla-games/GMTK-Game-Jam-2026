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

    /// Ranged past melee reach, same threshold CardViewer uses to pick the bow icon - so Fireball and
    /// EnemyArrow get RangedAttack and Slash gets MeleeAttack, with no card having to say so itself.
    /// A CardAnimation can still redirect either one; this only decides which default it redirects
    /// *from*.
    protected override AnimationCue Cue(ActionContext ctx) =>
        ctx.card != null && ctx.card.range.IsRanged
            ? AnimationCue.RangedAttack
            : AnimationCue.MeleeAttack;

    public override IEnumerator Execute(ActionContext ctx)
    {
        // Plays first and is waited on, so the impact VFX and the health-bar drop below land in the
        // same coroutine and can never desync - the swing connects, then the damage happens.
        yield return Perform(ctx);

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
