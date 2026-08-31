using UnityEngine;

/// <summary>
/// Shoves whoever is caught in this entry's footprint outward, onto the ring of tiles surrounding it -
/// Wall of Force's shove, and reusable by any future card that wants the same "clear the area, throw
/// the bodies to its edge" shape. See GridManager.PlanPush for the actual geometry.
/// </summary>
[CreateAssetMenu(menuName = "Card Effects/Push")]
public class PushEffect : CardEffect
{
    /// <summary>
    /// Deliberately Unrestricted, unlike a damage effect. Card.ResolveEffects filters this entry's
    /// footprint by Refusal before handing the surviving tiles to PushAction, and the ring a body is
    /// shoved onto is a property of the *whole* footprint, empty tiles included - declaring an
    /// audience here would drop every empty tile out of ctx.targets and PlanPush would compute the
    /// wrong ring around whatever occupied tiles happened to be left. PushAction itself skips any tile
    /// with nobody standing on it, which is the actual "who does this affect" answer.
    /// </summary>
    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new PushAction(), ctx);
    }
}
