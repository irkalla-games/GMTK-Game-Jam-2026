using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Heal")]
public class HealEffect : CardEffect
{
    [SerializeField] private int healAmount;

    [Tooltip("Off, this may only be aimed at your own side - which is what the Mage's Heal wants.")]
    [SerializeField] private bool canHitEnemies;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new HealAction(healAmount), ctx);
    }

    /// Healing needs somebody to heal, and by default that somebody is an ally. Asked before the card
    /// is paid for, so aiming at thin air costs nothing - and it is the same question GridManager asks
    /// to light up tiles, so only the tiles this can actually heal are highlighted.
    public override string Refusal(Character source, GridTile target)
    {
        if (!canHitEnemies) { return RefuseByOccupant(source, target, wantAlly: true); }

        return target != null && target.Occupant != null ? null : "there is nobody there";
    }
}
