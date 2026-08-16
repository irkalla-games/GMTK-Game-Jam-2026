using UnityEngine;

/// <summary>
/// Lays a persistent effect on the target tile(s) themselves rather than on whoever is standing there -
/// Wall of Force and Wall of Flames. The tile-level mirror of ApplyStatusEffect: one asset type covers
/// every kind of tile effect, and which one a card grants is just a dropdown.
///
/// Refuses nothing of its own: a wall may be laid on an occupied tile without objection - Wall of Force
/// only gates *entry* from then on, and dropping Wall of Flames under a standing enemy is the entire
/// point of the card.
/// </summary>
[CreateAssetMenu(menuName = "Card Effects/Apply Tile Effect")]
public class ApplyTileEffect : CardEffect
{
    [SerializeField] private TileEffectType effect;

    [Tooltip("How many end-of-player-turn ticks this effect survives.")]
    [SerializeField] private int turns = 2;

    [Tooltip("What this effect's own number means, if it has one - damage for Wall of Flames, unused "
             + "by Wall of Force.")]
    [SerializeField] private int magnitude = 4;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new TileEffectAction(effect, turns, ctx.Amount(magnitude)), ctx);
    }
}
