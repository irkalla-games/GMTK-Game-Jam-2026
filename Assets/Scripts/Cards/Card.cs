using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One physical copy of a card.
///
/// CardData is a shared asset - two copies of Bash in a deck point at the same object, and writing to
/// it during Play Mode persists into the .asset file on disk. So the asset is treated as immutable,
/// and anything that varies per copy or changes during a run lives here.
/// </summary>
public class Card
{
    private readonly CardData data;

    /// Mutable per copy: seeded from the asset, then free to change during a run.
    public int cost;

    public string cardName => data.cardName;
    public string description => data.description;
    public Sprite image => data.image;

    public List<CardEffect> effects;

    public Card(CardData newData)
    {
        this.data = newData;
        this.cost = newData.cost;
    }



}
