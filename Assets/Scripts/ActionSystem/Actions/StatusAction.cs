using System.Collections;
using UnityEngine;

/// Applies one status to whoever is standing on each target tile. Buffs and curses both - the only
/// difference is which status type the effect asset was authored with.
public class StatusAction : GameAction
{
    private readonly StatusType status;
    private readonly int stacks;

    public StatusAction(StatusType status, int stacks)
    {
        this.status = status;
        this.stacks = stacks;
    }

    public override IEnumerator Execute(ActionContext ctx)
    {
        // Asked once, of whoever is playing the card, not once per tile: how deep a Weaken this
        // character applies is a fact about their equipment, not about which enemy it lands on. See
        // Character.AppliedPotency.
        int amountBonus = ctx.source != null ? ctx.source.AppliedPotency(status) : 0;

        foreach (GridTile target in ctx.targets)
        {
            target.ApplyStatus(status, stacks, amountBonus);
        }

        yield return new WaitForSeconds(ResolveDelay);
    }
}
