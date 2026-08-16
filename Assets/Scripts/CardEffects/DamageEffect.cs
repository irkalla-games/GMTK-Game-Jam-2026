using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Deal Damage")]
public class DamageEffect : CardEffect
{
    [SerializeField] int damageAmount;

    [Tooltip("Off, this may only be aimed at a character on the other side - which is what Fireball "
             + "wants. On, it will happily burn your own.")]
    [SerializeField] private bool canHitAllies;

    /// The authored base amount before ActionContext.Amount adjusts it - what Card.PreviewDamage reads
    /// to project a hit without a live ActionContext.
    public int Damage => damageAmount;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new DamageAction(ctx.Amount(damageAmount)), ctx);
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
    public override TargetAudience Audience =>
        canHitAllies ? TargetAudience.AnyCharacter : TargetAudience.Enemy;
}
