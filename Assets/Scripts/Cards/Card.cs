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
            if (effect == null) { continue; }

            // Source-aimed effects are not asked. They land on the caster no matter where the click
            // went, so letting one object would mean Steely Attack refusing itself the moment
            // ShieldEffect grew a rule - the caster's own tile is, of course, occupied by the caster.
            if (effect.AimsAt == EffectTarget.Source) { continue; }

            string refusal = effect.Refusal(source, target);

            if (refusal != null) { return refusal; }
        }

        return null;
    }

    /// <summary>
    /// Resolves every effect on this card, each against its own context.
    ///
    /// A context per effect, not per card - that is what lets one card point its effects at different
    /// things. Steely Attack damages the tile you clicked and armors the character who played it.
    /// </summary>
    public void ResolveEffects(Character source, GridTile target)
    {
        foreach (var effect in effects)
        {
            if (effect == null) { continue; }

            GridTile aim = effect.AimsAt == EffectTarget.Source
                ? (source != null ? source.Tile : null)
                : target;

            effect.Resolve(new ActionContext(this, source, aim));
        }
    }
}
