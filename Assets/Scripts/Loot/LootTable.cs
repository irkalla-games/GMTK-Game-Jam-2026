using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Everything the *dropper* decides about a reward: which tier, how many choices, and what to favour
/// within that tier. LevelData holds the default for the level; a Character may override it with its
/// own - see Character.LootTable. The table travels with a stolen item (Character.CarriedLoot) so loot
/// a poison skeleton dropped still favours poison after a different enemy carries it off.
/// </summary>
[CreateAssetMenu(menuName = "Loot/Loot Table")]
public class LootTable : ScriptableObject
{
    [System.Serializable]
    public struct TierWeight
    {
        public Rarity rarity;
        public int weight;
    }

    [System.Serializable]
    public struct TagWeight
    {
        public CardTag tag;
        public int weight;
    }

    [System.Serializable]
    public struct CardWeight
    {
        public CardData card;
        public int weight;
    }

    [Tooltip("Odds of each rarity tier when a character carrying this table dies. Tiers left out of "
             + "this list are never rolled.")]
    [SerializeField] private List<TierWeight> tierWeights = new();

    [Tooltip("Multiplies the weight of any offerable card carrying this tag. A card with no matching "
             + "tag keeps its base weight of 1 - this biases the pool, it never excludes from it.")]
    [SerializeField] private List<TagWeight> tagWeights = new();

    [Tooltip("Sets a specific card's weight outright, overriding tagWeights for it - for a boss with "
             + "one signature drop. Leave empty if tag weighting already covers what you need.")]
    [SerializeField] private List<CardWeight> cardWeights = new();

    [Tooltip("Cards this table never offers, even where they are offerable everywhere else. A hard "
             + "gate, unlike cardWeights which only biases - a weight of 0 still leaves a card in the "
             + "pool. Takes priority over guaranteedCards if a card is listed in both.")]
    [SerializeField] private List<CardData> excludedCards = new();

    [Tooltip("Cards this table always offers, ignoring the rarity roll and excludeFromRewards - a boss "
             + "whose drop is a fixed signature card. Each one takes a slot out of choiceCount; the "
             + "rest are rolled normally. Still skipped for a picker who cannot hold the card, and "
             + "loses to excludedCards if a card is listed in both.")]
    [SerializeField] private List<CardData> guaranteedCards = new();

    [Tooltip("How many cards the reward panel offers. Clamped to at least 1.")]
    [SerializeField] private int choiceCount = 3;

    public int ChoiceCount => Mathf.Max(1, choiceCount);

    public IReadOnlyList<CardData> GuaranteedCards => guaranteedCards;

    /// True if `card` is on this table's hard-exclude list. Checked ahead of guaranteedCards too - see
    /// the tooltips above for why exclusion wins a contradiction rather than the guarantee.
    public bool Excludes(CardData card) => card != null && excludedCards.Contains(card);

    /// <summary>
    /// Rolls a rarity tier by weight. Returns Common if nothing is authored, since Common == 0 is
    /// already every other enum's safe default in this project.
    /// </summary>
    public Rarity Roll()
    {
        int total = 0;

        foreach (TierWeight entry in tierWeights)
        {
            if (entry.rarity == Rarity.NotOffered)
            {
                Debug.LogWarning($"{name}: NotOffered listed in tierWeights - skipped, it can never "
                                 + "be rolled as a drop.");
                continue;
            }

            if (entry.weight > 0) { total += entry.weight; }
        }

        if (total <= 0) { return Rarity.Common; }

        int roll = Random.Range(0, total);

        foreach (TierWeight entry in tierWeights)
        {
            if (entry.rarity == Rarity.NotOffered || entry.weight <= 0) { continue; }

            if (roll < entry.weight) { return entry.rarity; }

            roll -= entry.weight;
        }

        return Rarity.Common;
    }

    /// <summary>
    /// How strongly this table favours `card` among the choices being offered. Every card starts at 1
    /// - unlisted is never zero, only out-weighted - then a cardWeights entry sets the result outright,
    /// or each matching tagWeights entry multiplies it.
    /// </summary>
    public int WeightOf(CardData card)
    {
        if (card == null) { return 0; }

        foreach (CardWeight entry in cardWeights)
        {
            if (entry.card == card) { return Mathf.Max(0, entry.weight); }
        }

        int weight = 1;

        foreach (CardTag tag in card.tags)
        {
            foreach (TagWeight entry in tagWeights)
            {
                if (entry.tag == tag && entry.weight > 0) { weight *= entry.weight; }
            }
        }

        return weight;
    }
}
