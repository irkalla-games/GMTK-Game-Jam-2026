using System.Collections;
using UnityEngine;

/// <summary>
/// Points whoever is standing on each target tile at the character that played the card.
///
/// The taunter is ctx.source rather than a constructor argument, which is the whole reason this action
/// exists next to StatusAction: the duration is an authoring number and belongs on the field, but who
/// is doing the taunting varies per play and belongs in the context.
/// </summary>
public class TauntAction : GameAction
{
    private readonly int turnsRemaining;

    public TauntAction(int turnsRemaining)
    {
        this.turnsRemaining = turnsRemaining;
    }

    public override IEnumerator Execute(ActionContext ctx)
    {
        foreach (GridTile target in ctx.targets)
        {
            target.Taunt(ctx.source, turnsRemaining);
        }

        yield return new WaitForSeconds(ResolveDelay);
    }
}
