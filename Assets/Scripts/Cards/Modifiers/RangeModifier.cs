using UnityEngine;

/// <summary>
/// Shifts a card's own TargetRange - Long Lens' "+1 range on attack cards", or an upgraded Fireball
/// reaching further as well as splashing wider.
/// </summary>
[CreateAssetMenu(menuName = "Card Modifiers/Range")]
public class RangeModifier : CardModifier
{
    [SerializeField] private int minDelta;
    [SerializeField] private int maxDelta;

    [Tooltip("Off (the default), the card's own shape is left alone and only min/max shift. On, the "
             + "shape is replaced by Shape below - Chebyshev/Anywhere/etc are never inferred, so a "
             + "modifier that only cares about reach can leave this off.")]
    [SerializeField] private bool overrideShape;

    [SerializeField] private RangeShape shape;

    public override void Apply(Card card)
    {
        RangeShape resolvedShape = overrideShape ? shape : card.range.Shape;
        int min = Mathf.Max(0, card.range.MinDistance + minDelta);
        int max = Mathf.Max(min, card.range.MaxDistance + maxDelta);

        card.range = new TargetRange(resolvedShape, min, max);
    }

    public override string Describe() => maxDelta >= 0 ? $"Range +{maxDelta}" : $"Range {maxDelta}";
}
