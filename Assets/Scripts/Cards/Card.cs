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

    /// Also per copy, so a relic granting +1 range writes here. TargetRange is a value type, so this
    /// is a copy - writing to it can never reach back into the shared CardData asset.
    public TargetRange range;

    public string cardName => data.cardName;
    public string description => data.description;
    public Sprite image => data.image;

    private List<CardEffect> effects;

    public Card(CardData newData)
    {
        this.data = newData;
        this.cost = newData.cost;
        this.range = newData.range;
        // Effects are shared, stateless ScriptableObject resolvers, so aliasing the asset's list is safe.
        this.effects = newData.effects;
    }

    /// <summary>
    /// Why this card cannot be played onto `target` by `source`, or null if it can.
    ///
    /// Asked once per click, before anything is spent - see CardPlayManager.PlaySelectedOn - and again
    /// by GridManager to decide which tiles to light up, so the highlight can never disagree with what
    /// a click actually does. Range is the card's own rule; anything past that is a rule only the
    /// effect knows, so each effect gets asked in turn.
    /// </summary>
    public string Refusal(Character source, GridTile target)
    {
        if (!range.Contains(source != null ? source.Tile : null, target))
        {
            string where = target != null ? target.Coordinates.ToString() : "nowhere";
            return $"{where} is out of range ({range})";
        }

        foreach (var effect in effects)
        {
            string refusal = effect != null ? effect.Refusal(source, target) : null;

            if (refusal != null) { return refusal; }
        }

        return null;
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
