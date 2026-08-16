using UnityEngine;

/// <summary>
/// Appends a whole new effect entry - an upgraded Poison Dagger that also draws a card, an upgraded
/// Quick Attack that also applies Weaken. The entry is authored complete (effect, aimsAt, area) exactly
/// as a CardData's own effectEntries row would be.
/// </summary>
[CreateAssetMenu(menuName = "Card Modifiers/Add Entry")]
public class AddEntryModifier : CardModifier
{
    [SerializeField] private CardEffectEntry entry;

    public override void Apply(Card card) => card.AddEntry(entry);

    public override string Describe() =>
        entry.effect != null ? $"Also: {entry.effect.name}" : "Adds an effect";
}
