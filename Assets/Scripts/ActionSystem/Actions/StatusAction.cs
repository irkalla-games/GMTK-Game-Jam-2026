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
        foreach (GridTile target in ctx.targets)
        {
            target.ApplyStatus(status, stacks);
        }

        yield return new WaitForSeconds(ResolveDelay);
    }
}
