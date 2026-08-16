using UnityEngine;

/// <summary>
/// Swaps the caster with whoever is standing on the target tile - Shambles. Works on an ally or an
/// enemy alike; the only requirement is that somebody other than the caster is actually standing there.
/// </summary>
[CreateAssetMenu(menuName = "Card Effects/Swap")]
public class SwapEffect : CardEffect
{
    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new SwapAction(), ctx);
    }

    /// Either side will do - a swap displaces whoever is there, friend or foe.
    public override TargetAudience Audience => TargetAudience.AnyCharacter;

    public override string Refusal(Character source, GridTile target)
    {
        string refusal = base.Refusal(source, target);

        if (refusal != null) { return refusal; }

        // base has already established somebody is standing there; the one thing AnyCharacter cannot
        // express is that the somebody must not be you.
        return target.Occupant == source ? "cannot swap with yourself" : null;
    }

    // A swap has exactly two participants - the caster and whoever is on the one tile clicked. Same
    // reason MoveEffect refuses an area: SwapAction throws unless it gets exactly one target tile.
    public override bool SupportsArea => false;
}
