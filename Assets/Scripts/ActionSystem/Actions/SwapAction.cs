using System;
using System.Collections;
using UnityEngine;

/// Swaps the caster with whoever is standing on the one target tile. Thin: GridManager owns the actual
/// occupancy bookkeeping and tween, same relationship MoveAction has with GridManager.MoveCharacter.
public class SwapAction : GameAction
{
    protected override AnimationCue Cue(ActionContext ctx) => AnimationCue.Cast;

    public override IEnumerator Execute(ActionContext ctx)
    {
        if (ctx.targets.Count != 1)
        {
            throw new ArgumentException("Swap requires exactly one target tile");
        }

        yield return Perform(ctx);

        Character other = ctx.targets[0].Occupant;

        GridManager.Instance.SwapCharacters(ctx.source, other);

        yield return new WaitForSeconds(GridManager.Instance.MoveDuration);
    }
}
