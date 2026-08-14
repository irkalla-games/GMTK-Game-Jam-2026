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

    public override string Refusal(Character source, GridTile target)
    {
        Character occupant = target != null ? target.Occupant : null;

        if (occupant == null) { return "there is nobody there"; }
        if (occupant == source) { return "cannot swap with yourself"; }

        return null;
    }

    // A swap has exactly two participants - the caster and whoever is on the one tile clicked. Same
    // reason MoveEffect refuses an area: SwapAction throws unless it gets exactly one target tile.
    public override bool SupportsArea => false;
}
