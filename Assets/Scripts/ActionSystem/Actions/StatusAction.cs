using System.Collections;
using UnityEngine;

/// Applies one status to whoever is standing on each target tile. Buffs and curses both - the only
/// difference is which status type the effect asset was authored with.
public class StatusAction : GameAction
{
    private readonly StatusType status;
    private readonly int stacks;
    private readonly int turnsRemaining;

    public StatusAction(StatusType status, int stacks, int turnsRemaining)
    {
        this.status = status;
        this.stacks = stacks;
        this.turnsRemaining = turnsRemaining;
    }

    public override IEnumerator Execute(ActionContext ctx)
    {
        foreach (GridTile target in ctx.targets)
        {
            target.ApplyStatus(status, stacks, turnsRemaining);
        }

        yield return new WaitForSeconds(ResolveDelay);
    }
}
