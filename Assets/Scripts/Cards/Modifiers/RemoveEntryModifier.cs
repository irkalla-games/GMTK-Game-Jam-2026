using UnityEngine;

/// <summary>
/// Drops matching entries outright - an upgraded card that trades away a drawback entry, or a downside
/// half of a two-entry card (SelfDamageEffect, say) that an upgrade removes entirely.
/// </summary>
[CreateAssetMenu(menuName = "Card Modifiers/Remove Entry")]
public class RemoveEntryModifier : CardModifier
{
    [SerializeField] private EntrySelector on;

    public override void Apply(Card card) => card.RemoveEntriesWhere((entry, i) => on.Matches(entry, i));

    public override string Describe() => "Removes an effect";
}
