using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rarity -> presentation, the same shape as IntentIcons. Used by both ItemPickup (the tint on the
/// board) and RewardPanel (the card frame), so the two can never disagree about what purple means.
/// </summary>
[CreateAssetMenu(menuName = "Loot/Rarity Style")]
public class RarityStyle : ScriptableObject
{
    [System.Serializable]
    private struct Entry
    {
        public Rarity rarity;
        public Color tint;
        public Sprite badge;
    }

    [SerializeField] private List<Entry> entries = new();

    public Color TintFor(Rarity rarity)
    {
        foreach (Entry entry in entries)
        {
            if (entry.rarity == rarity) { return entry.tint; }
        }

        return Color.white;
    }

    public Sprite BadgeFor(Rarity rarity)
    {
        foreach (Entry entry in entries)
        {
            if (entry.rarity == rarity) { return entry.badge; }
        }

        return null;
    }
}
