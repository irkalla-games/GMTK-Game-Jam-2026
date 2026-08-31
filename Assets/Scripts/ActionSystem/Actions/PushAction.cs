using System.Collections;
using UnityEngine;

/// <summary>
/// Shoves whoever is standing on ctx.targets out to the surrounding ring - the resolve-time half of
/// PushEffect. Stateless like every other action: no constructor arguments, since there is no
/// magnitude to author, only a footprint that arrives fresh through ctx.targets every play.
///
/// Re-plans against the board as it stands right now rather than trusting whatever ShowPushPreview
/// computed while the player was aiming - the board can have changed in the meantime (another action
/// earlier on the same card, say), and GridManager.PlanPush is pure, so asking again costs nothing and
/// can never disagree with what actually happens.
/// </summary>
public class PushAction : GameAction
{
    public override IEnumerator Execute(ActionContext ctx)
    {
        if (ctx.source == null || GridManager.Instance == null) { yield break; }

        var plan = GridManager.Instance.PlanPush(ctx.source, ctx.targets);

        foreach (var (mover, destination) in plan)
        {
            // A null destination is a body the ring had no room for. Card.PushRefusal refuses the card
            // outright in that case, so a player play never reaches here with one - this just leaves
            // them standing rather than trusting that.
            if (mover == null || destination == null) { continue; }

            GridManager.Instance.MoveCharacter(mover, destination);

            yield return new WaitForSeconds(GridManager.Instance.MoveDuration);
        }
    }
}
