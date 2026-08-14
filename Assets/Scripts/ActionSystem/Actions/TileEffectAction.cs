using System.Collections;
using UnityEngine;

/// Lays one tile effect on every target tile - a fresh TileEffect instance per tile, since each tile
/// ages its own copy independently (the way StatusAction hands every target its own status, not one
/// shared object).
public class TileEffectAction : GameAction
{
    private readonly TileEffectType effect;
    private readonly int turns;
    private readonly int magnitude;

    public TileEffectAction(TileEffectType effect, int turns, int magnitude)
    {
        this.effect = effect;
        this.turns = turns;
        this.magnitude = magnitude;
    }

    protected override AnimationCue Cue(ActionContext ctx) => AnimationCue.Cast;

    public override IEnumerator Execute(ActionContext ctx)
    {
        yield return Perform(ctx);

        foreach (GridTile target in ctx.targets)
        {
            target.AddTileEffect(TileEffect.Create(effect, turns, magnitude));
        }

        yield return new WaitForSeconds(ResolveDelay);
    }
}
