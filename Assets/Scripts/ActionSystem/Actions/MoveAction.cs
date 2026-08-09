using System;
using System.Collections;
using UnityEngine;

public class MoveAction : GameAction
{
    protected override AnimationCue Cue(ActionContext ctx) => AnimationCue.Move;

    /// <summary>
    /// Unlike every other action, the animation here plays *during* the tween rather than before it -
    /// a walk cycle that finished playing before the character actually moved would look wrong. So
    /// this does not call the shared Perform helper; it fires the cue as its own coroutine and moves
    /// on immediately, then waits out GridManager's own tween duration rather than ResolveDelay, which
    /// used to only coincidentally match it.
    ///
    /// A card may still redirect the cue - Teleport asking for a blink instead of a walk - through the
    /// same CardAnimation override table every other action reads; CueOverride.impactPrefab plays as
    /// an arrival VFX once the character has actually landed.
    /// </summary>
    public override IEnumerator Execute(ActionContext ctx)
    {
        if (ctx.targets.Count != 1)
        {
            throw new ArgumentException("Move requires exactly one target tile");
        }

        GridTile destination = ctx.targets[0];
        CharacterAnimator animation = ctx.source != null ? ctx.source.Animation : null;
        AnimationCue cue = Cue(ctx);
        CardAnimation cardAnimation = ctx.card != null ? ctx.card.animation : null;
        CueOverride entry = cardAnimation != null
            ? cardAnimation.For(cue)
            : new CueOverride { when = cue, play = cue };

        if (animation != null)
        {
            animation.SetFacing(destination.transform.position);

            // Held for exactly the tween's length rather than the walk clip's own, so the cycle runs
            // for the whole slide and stops the moment the character lands.
            if (entry.play != AnimationCue.None && entry.play != AnimationCue.Silent
                && ActionManager.Instance != null)
            {
                ActionManager.Instance.StartCoroutine(
                    animation.Play(entry.play, entry.stateOverride, GridManager.Instance.MoveDuration));
            }
        }

        // GridManager owns the move: it guards against occupied tiles, swaps occupancy, and tweens
        // the character's transform. Going through Character.MoveTo alone would only swap references.
        GridManager.Instance.MoveCharacter(ctx.source, destination);

        yield return new WaitForSeconds(GridManager.Instance.MoveDuration);

        if (entry.impactPrefab != null)
        {
            UnityEngine.Object.Instantiate(entry.impactPrefab, destination.transform.position,
                                            Quaternion.identity);
        }
    }
}
