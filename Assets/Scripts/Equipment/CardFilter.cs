using System;
using System.Collections.Generic;

/// <summary>
/// Which cards a piece of equipment's CardTuningModifier reaches - "every Fire card" (tags), "Slash and
/// Quick Attack specifically" (cards), or both at once. A card matches if either list names it, so a
/// relic can widen an entire theme and still call out one card by name in the same filter.
///
/// Empty on both sides matches nothing, not everything - the safer default for a struct that
/// deserializes to zero-length lists on a freshly authored equipment asset. A modifier authored without
/// ever touching this filter should do nothing rather than silently reaching every card in the game.
/// </summary>
[Serializable]
public struct CardFilter
{
    public List<CardTag> tags;
    public List<CardData> cards;

    public bool Matches(CardData data)
    {
        if (data == null) { return false; }

        if (cards != null && cards.Contains(data)) { return true; }

        if (tags != null && data.tags != null)
        {
            foreach (CardTag tag in data.tags)
            {
                if (tags.Contains(tag)) { return true; }
            }
        }

        return false;
    }
}
