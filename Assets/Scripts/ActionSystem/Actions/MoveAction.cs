using System;
using System.Collections;
using System.Collections.Generic;
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
    ///
    /// A RequiresRoute card (Move 1/2) walks tile by tile rather than sliding straight to the
    /// destination, so a Wall of Force forces a visible detour instead of being crossed in one tween -
    /// see GridManager.Route. A teleport keeps the old single straight-line slide.
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

        bool routed = ctx.card != null && ctx.card.range.RequiresRoute
            && ctx.card.range.Shape != RangeShape.Anywhere;

        List<GridTile> steps = routed
            ? GridManager.Instance.Route(ctx.source.Tile, destination, ctx.card.range.MaxDistance)
            : new List<GridTile> { destination };

        if (steps.Count == 0)
        {
            Debug.LogWarning($"{(ctx.source != null ? ctx.source.name : "somebody")} cannot move to "
                             + $"{destination.Coordinates}: the way is blocked");
            yield break;
        }

        float moveDuration = GridManager.Instance.MoveDuration;

        if (animation != null)
        {
            animation.SetFacing(steps[0].transform.position);

            // Held for the whole walk rather than one tween's length, so the cycle runs for every
            // step and stops the moment the character lands on the final tile.
            if (entry.play != AnimationCue.None && entry.play != AnimationCue.Silent
                && ActionManager.Instance != null)
            {
                ActionManager.Instance.StartCoroutine(
                    animation.Play(entry.play, entry.stateOverride, moveDuration * steps.Count));
            }
        }

        foreach (GridTile step in steps)
        {
            if (animation != null) { animation.SetFacing(step.transform.position); }

            // GridManager owns the move: it guards against occupied tiles, swaps occupancy, and
            // tweens the character's transform. Going through Character.MoveTo alone would only swap
            // references.
            if (!GridManager.Instance.MoveCharacter(ctx.source, step)) { break; }

            yield return new WaitForSeconds(moveDuration);
        }

        if (entry.impactPrefab != null)
        {
            UnityEngine.Object.Instantiate(entry.impactPrefab, destination.transform.position,
                                            Quaternion.identity);
        }
    }
}
