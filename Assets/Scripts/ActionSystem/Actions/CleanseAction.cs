using System.Collections;
using UnityEngine;

/// <summary>
/// Strips every curse in CleanseEffect.Curses off each target and heals it for the stacks removed -
/// see CleanseEffect, the authoring half this mirrors.
/// </summary>
public class CleanseAction : GameAction
{
    protected override AnimationCue Cue(ActionContext ctx) => AnimationCue.Cast;

    public override IEnumerator Execute(ActionContext ctx)
    {
        yield return Perform(ctx);

        foreach (GridTile target in ctx.targets)
        {
            Character occupant = target.Occupant;

            if (occupant == null) { continue; }

            int removed = 0;

            foreach (StatusType curse in CleanseEffect.Curses) { removed += occupant.RemoveStatus(curse); }

            if (removed > 0) { occupant.Heal(removed); }
        }

        yield return new WaitForSeconds(ResolveDelay);
    }
}
