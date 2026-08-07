using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every CardData asset in the game, in one place - the pool LootManager offers rewards from.
///
/// Nothing else in the project can enumerate "every card that exists"; each character only knows its
/// own authored deck. Kept as an explicit, inspectable list rather than a runtime Resources.LoadAll
/// scan so the reward pool never silently includes a card nobody meant to ship, and so it costs
/// nothing to look at in the Inspector. Assets/Editor/CardLibraryEditor.cs is what keeps this list
/// honest - a Rescan button, plus an AssetPostprocessor that re-runs it whenever a CardData asset is
/// imported, so a newly authored card is offerable without anyone remembering to touch this asset.
/// </summary>
[CreateAssetMenu(menuName = "Loot/Card Library")]
public class CardLibrary : ScriptableObject
{
    [SerializeField] private List<CardData> cards = new();

    public IReadOnlyList<CardData> Cards => cards;

#if UNITY_EDITOR
    /// Editor-only write path for CardLibraryEditor's Rescan button and its AssetPostprocessor. Not
    /// behind a public setter in player code - nothing at runtime should ever rewrite this list.
    public void SetCards(List<CardData> found)
    {
        cards = found;
    }
#endif

    /// <summary>
    /// Every card at this exact tier that `picker` is allowed to hold, NotOffered already excluded.
    ///
    /// Reuses CardData.CanBeUsedBy rather than re-deriving the class check - that predicate is
    /// documented as "the hook a post-combat reward or draft screen filters on", and this is the first
    /// caller that makes that true.
    /// </summary>
    public List<CardData> Offerable(Rarity tier, Character picker)
    {
        List<CardData> results = new();

        foreach (CardData card in cards)
        {
            if (card == null || card.rarity != tier || card.rarity == Rarity.NotOffered) { continue; }
            if (!card.CanBeUsedBy(picker)) { continue; }

            results.Add(card);
        }

        return results;
    }
}
