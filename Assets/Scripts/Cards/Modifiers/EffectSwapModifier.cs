using UnityEngine;

/// <summary>
/// Replaces a matching entry's CardEffect asset outright - an upgraded card trading Damage for
/// Damage+Poison, say. The blunt sibling of MagnitudeModifier: that one nudges a number, this one
/// changes what the entry does entirely. aimsAt and area are left as authored; only the effect changes.
/// </summary>
[CreateAssetMenu(menuName = "Card Modifiers/Swap Effect")]
public class EffectSwapModifier : CardModifier
{
    [SerializeField] private EntrySelector on;
    [SerializeField] private CardEffect replacement;

    public override void Apply(Card card)
    {
        if (replacement == null) { return; }

        for (int i = 0; i < card.EntryCount; i++)
        {
            CardEffectEntry entry = card.GetEntry(i);

            if (!on.Matches(entry, i)) { continue; }

            entry.effect = replacement;
            card.SetEntry(i, entry);
        }
    }

    public override string Describe() => replacement != null ? $"Becomes {replacement.name}" : "No change";
}
