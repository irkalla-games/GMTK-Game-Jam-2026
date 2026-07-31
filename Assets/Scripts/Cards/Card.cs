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

    /// Per-copy runtime instances built from data.keywords - see CardKeyword.
    private readonly List<CardKeyword> keywords = new();

    public Card(CardData newData)
    {
        this.data = newData;
        this.cost = newData.cost;
        this.range = newData.range;
        // Effects are shared, stateless ScriptableObject resolvers, so aliasing the asset's list is safe.
        this.effects = newData.effects;

        // remaining starts equal to magnitude so a fresh Cooldown card is locked for its own warmup
        // before its first play, exactly as it is again after every later play.
        foreach (CardKeywordEntry entry in newData.keywords)
        {
            keywords.Add(new CardKeyword(entry.type, entry.magnitude, entry.magnitude));
        }
    }

    public bool HasKeyword(CardKeywordType type) => keywords.Exists(k => k.type == type);

    private CardKeyword Keyword(CardKeywordType type) => keywords.Find(k => k.type == type);

    /// Turns left before Cooldown allows this card to be played again, or 0 if it has no Cooldown
    /// keyword at all. Also what a tooltip should show - it is already the exact number to display.
    public int CooldownRemaining => Keyword(CardKeywordType.Cooldown)?.remaining ?? 0;

    /// True if this card's effects include one of this type - how the enemy brain tells a Summon card
    /// apart from a Move card, since both are only legal on an empty tile.
    public bool HasEffect<TEffect>() where TEffect : CardEffect => effects.Exists(e => e is TEffect);

    /// <summary>
    /// Ticks Cooldown down by one round. Called once per round for every card a character owns,
    /// regardless of which pile it is sitting in - a card recharges whether or not it is in hand.
    /// </summary>
    public void TickCooldown()
    {
        CardKeyword cooldown = Keyword(CardKeywordType.Cooldown);

        if (cooldown != null && cooldown.remaining > 0) { cooldown.remaining--; }
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

        if (CooldownRemaining > 0) { return $"needs {CooldownRemaining} more turn(s) to recharge"; }

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
        // Resets right when the card is actually played, not when it is merely legal - a Cooldown
        // card that never gets played (e.g. discarded unused at TurnStart) must not re-lock itself.
        CardKeyword cooldown = Keyword(CardKeywordType.Cooldown);
        if (cooldown != null) { cooldown.remaining = cooldown.magnitude; }

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
