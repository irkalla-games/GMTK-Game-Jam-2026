using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Summon")]
public class SummonEffect : CardEffect
{
    [SerializeField] private GameObject summonedObject;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new SummonAction(summonedObject), ctx);
    }

    // Mirrors GridTile.SummonObject's own occupancy check, so a click on an occupied tile is refused
    // up front instead of costing energy for nothing.
    public override string Refusal(Character source, GridTile target) =>
        target != null && target.Occupant != null ? $"{target.Occupant.name} is already standing there" : null;
}
