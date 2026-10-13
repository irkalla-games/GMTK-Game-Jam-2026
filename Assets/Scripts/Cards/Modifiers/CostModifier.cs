using UnityEngine;

/// <summary>
/// Shifts a card's energy cost - Featherweight Grips' "Movement cards cost 1 less".
/// </summary>
[CreateAssetMenu(menuName = "Card Modifiers/Cost")]
public class CostModifier : CardModifier
{
    [SerializeField] private int delta;

    /// Writes the printed cost, never the live one - Card.cost subtracts a board-condition discount on
    /// every read, and baking one in here would keep it after the condition stopped holding.
    public override void Apply(Card card)
    {
        card.baseCost = Mathf.Max(0, card.baseCost + delta);
    }

    public override string Describe() => delta >= 0 ? $"Cost +{delta}" : $"Cost {delta}";
}
