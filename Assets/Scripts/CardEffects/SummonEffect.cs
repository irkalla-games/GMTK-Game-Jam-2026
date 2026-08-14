using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Summon")]
public class SummonEffect : CardEffect
{
    [SerializeField] private GameObject summonedObject;

    [Tooltip("How many of the summon's own turns it survives before crumbling on its own - 0 means "
             + "permanent. Applied as a Summoned status, the same self-ticking shape as Poison or "
             + "Frozen, so a temporary summon needs no bespoke expiry code anywhere else.")]
    [SerializeField] private int lifetimeTurns;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new SummonAction(summonedObject, lifetimeTurns), ctx);
    }

    // Mirrors GridTile.SummonObject's own occupancy check, so a click on an occupied tile is refused
    // up front instead of costing energy for nothing.
    public override string Refusal(Character source, GridTile target) =>
        target != null && target.Occupant != null ? $"{target.Occupant.name} is already standing there" : null;
}
