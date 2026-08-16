using UnityEngine;

/// <summary>
/// Shifts a card's energy cost - Featherweight Grips' "Movement cards cost 1 less".
/// </summary>
[CreateAssetMenu(menuName = "Card Modifiers/Cost")]
public class CostModifier : CardModifier
{
    [SerializeField] private int delta;

    public override void Apply(Card card)
    {
        card.cost = Mathf.Max(0, card.cost + delta);
    }

    public override string Describe() => delta >= 0 ? $"Cost +{delta}" : $"Cost {delta}";
}
