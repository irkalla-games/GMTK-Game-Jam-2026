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
        ActionManager.Instance.AddAction(new DamageAction(damageAmount), ctx);
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
        if (canHitAllies)
        {
            return target != null && target.Occupant != null ? null : "there is nobody there to damage";
        }

        return RefuseByOccupant(source, target, wantAlly: false);
    }
}
