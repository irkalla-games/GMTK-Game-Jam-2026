using System.Collections;
using UnityEngine;

/// <summary>
/// A pure animation beat with no mechanical effect - see AnimateEffect. Its cue comes from the effect
/// that built it rather than being a fixed override like every other action's Cue, because this is the
/// one action whose entire purpose is to be that cue.
/// </summary>
public class AnimateAction : GameAction
{
    private readonly AnimationCue cue;

    public AnimateAction(AnimationCue cue)
    {
        this.cue = cue;
    }

    protected override AnimationCue Cue(ActionContext ctx) => cue;

    public override IEnumerator Execute(ActionContext ctx)
    {
        yield return Perform(ctx);
        yield return new WaitForSeconds(ResolveDelay);
    }
}
