using UnityEngine;

/// <summary>
/// Adds into a matching entry's amountDelta/amountPercent - an upgraded Slash dealing more damage, or
/// Whetstone-style equipment nudging every attack up. Additive with whatever is already on the entry
/// (an equipped bonus stacking with an upgrade's own delta), never a replacement - see
/// CardEffectEntry.amountDelta and ActionContext.Amount for how the two combine at resolve time.
/// </summary>
[CreateAssetMenu(menuName = "Card Modifiers/Magnitude")]
public class MagnitudeModifier : CardModifier
{
    [SerializeField] private EntrySelector on;
    [SerializeField] private int delta;
    [SerializeField] private int percent;

    public override void Apply(Card card)
    {
        for (int i = 0; i < card.EntryCount; i++)
        {
            CardEffectEntry entry = card.GetEntry(i);

            if (entry.effect == null || !on.Matches(entry, i)) { continue; }

            entry.amountDelta += delta;
            entry.amountPercent += percent;
            card.SetEntry(i, entry);
        }
    }

    public override string Describe()
    {
        if (delta != 0 && percent != 0) { return $"{Signed(delta)} and {Signed(percent)}%"; }
        if (delta != 0) { return Signed(delta); }
        if (percent != 0) { return $"{Signed(percent)}%"; }
        return "No change";
    }

    private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();
}
