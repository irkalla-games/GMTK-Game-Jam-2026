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

    private List<CardEffect> effects;

    public Card(CardData newData)
    {
        this.data = newData;
        this.cost = newData.cost;
        // Effects are shared, stateless ScriptableObject resolvers, so aliasing the asset's list is safe.
        this.effects = newData.effects;
    }

    /// Resolves every effect on this card, each against a context aimed at the played tile.
    public void ResolveEffects(Character source, GridTile target)
    {
        foreach (var effect in effects)
        {
            effect.Resolve(new ActionContext(this, source, target));
        }
    }
}
