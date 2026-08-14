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

    /// <summary>
    /// Healing needs somebody to heal, by default an ally, and that somebody has to have room to be
    /// healed - Character.Heal clamps to MaxHealth, so a full-health target would spend the energy and
    /// the card for nothing at all.
    ///
    /// Asked before the card is paid for, so a refused play costs nothing, and it is the same question
    /// GridManager asks to light up tiles - a full-health ally's tile stays dark rather than promising
    /// a heal the click would then refuse.
    ///
    /// For an area entry Card.Refusal skips this and ResolveEffects runs it per tile instead, which is
    /// what makes the full-health rule do the right thing on both: a single-target heal is refused
    /// outright, while a splash heal stays legal and simply drops the allies who do not need it.
    /// </summary>
    public override string Refusal(Character source, GridTile target)
    {
        string refusal = canHitEnemies
            ? (target != null && target.Occupant != null ? null : "there is nobody there")
            : RefuseByOccupant(source, target, wantAlly: true);

        if (refusal != null) { return refusal; }

        // Both branches above have already established there is somebody standing here.
        Character occupant = target.Occupant;

        return occupant.Health >= occupant.MaxHealth
            ? $"{occupant.name} is already at full health"
            : null;
    }
}
