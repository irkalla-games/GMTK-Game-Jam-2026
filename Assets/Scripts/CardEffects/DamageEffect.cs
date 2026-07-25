using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Deal Damage")]
public class DamageEffect : CardEffect
{
    [SerializeField] int damageAmount;

    [Tooltip("Off, this may only be aimed at a character on the other side - which is what Fireball "
             + "wants. On, it will happily burn your own.")]
    [SerializeField] private bool canHitAllies;

    public override void Resolve(ActionContext ctx)
    {
        CardPlayManager.Instance.actionManager.AddAction(new DamageAction(damageAmount), ctx);
    }

    /// <summary>
    /// Damage needs somebody to land on, and by default that somebody has to be an enemy. Asked before
    /// the card is paid for, so aiming at thin air or at your own side costs nothing - and it is the
    /// same question GridManager asks to decide which tiles to light up, so only the tiles this can
    /// actually burn are highlighted.
    ///
    /// Note the default is `false`: an effect asset authored before this field existed deserializes to
    /// enemies-only, which is what every damage card wants anyway.
    /// </summary>
    public override string Refusal(Character source, GridTile target)
    {
        Character occupant = target != null ? target.Occupant : null;

        if (occupant == null) { return "there is nobody there to damage"; }

        if (canHitAllies) { return null; }

        // Sides are just "player controlled or not" for now - there are no teams beyond that yet.
        if (source != null && occupant.IsPlayerControlled == source.IsPlayerControlled)
        {
            return $"{occupant.name} is on your own side";
        }

        return null;
    }
}
