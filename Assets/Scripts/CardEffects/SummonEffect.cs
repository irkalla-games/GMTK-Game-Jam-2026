using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Summon")]
public class SummonEffect : CardEffect
{
    [SerializeField] private GameObject summonedObject;

    [Tooltip("How many of the summon's own turns it survives before crumbling on its own - 0 means "
             + "permanent. Applied as a Summoned status, the same self-ticking shape as Poison or "
             + "Frozen, so a temporary summon needs no bespoke expiry code anywhere else.")]
    [SerializeField] private int lifetimeTurns;

    /// Read by Glossary.SummonContent, the same tooltip data the card's own description points to -
    /// see SummonedObject.
    public int LifetimeTurns => lifetimeTurns;

    /// The prefab this effect summons, exposed so a glossary row can read its Character/Totem stats
    /// straight off the same asset the card actually plays rather than a hand-typed copy of them.
    public GameObject SummonedObject => summonedObject;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new SummonAction(summonedObject, lifetimeTurns), ctx);
    }

    // Mirrors GridTile.SummonObject's own occupancy check, so a click on an occupied tile is refused
    // up front instead of costing energy for nothing.
    public override TargetAudience Audience => TargetAudience.EmptyTile;
}
