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
    public CardAnimation animation => data.animation;

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

        // Cooldown starts at 0 - ready the first time, locked again only after each play (see
        // ResolveEffects). Dormant starts locked at its own magnitude and never relocks - that is the
        // entire difference between the two keywords.
        foreach (CardKeywordEntry entry in newData.keywords)
        {
            int remaining = entry.type == CardKeywordType.Dormant ? entry.magnitude : 0;
            keywords.Add(new CardKeyword(entry.type, entry.magnitude, remaining));
        }
    }

    /// Read-only so a caller can list them - the hover tooltip explaining what Innate and Cooldown mean
    /// - without being able to add one a CardData never authored.
    public IReadOnlyList<CardKeyword> Keywords => keywords;

    public bool HasKeyword(CardKeywordType type) => keywords.Exists(k => k.type == type);

    private CardKeyword Keyword(CardKeywordType type) => keywords.Find(k => k.type == type);

    /// Turns left before Cooldown allows this card to be played again, or 0 if it has no Cooldown
    /// keyword at all. Also what a tooltip should show - it is already the exact number to display.
    public int CooldownRemaining => Keyword(CardKeywordType.Cooldown)?.remaining ?? 0;

    /// Turns left before Dormant allows this card to be played for the first time, or 0 if it has no
    /// Dormant keyword at all.
    public int DormantRemaining => Keyword(CardKeywordType.Dormant)?.remaining ?? 0;

    /// The number a face-up card badge should show - whichever lock is currently longer, since a card
    /// carrying both is held by whichever one has not run out yet. 0 means fully unlocked.
    public int LockedTurns => Mathf.Max(CooldownRemaining, DormantRemaining);

    /// True if this card's effects include one of this type - how the enemy brain tells a Summon card
    /// apart from a Move card, since both are only legal on an empty tile.
    public bool HasEffect<TEffect>() where TEffect : CardEffect => effects.Exists(e => e is TEffect);

    /// Why a timed keyword refuses to let this card be played right now, or null if neither does.
    /// Dormant checked first since it is the rarer, longer-lived lock - a card counting down both
    /// reads as dormant until that finishes, then as cooling down.
    private string LockRefusal()
    {
        if (DormantRemaining > 0) { return $"dormant for {DormantRemaining} more turn(s)"; }
        if (CooldownRemaining > 0) { return $"needs {CooldownRemaining} more turn(s) to recharge"; }
        return null;
    }

    /// <summary>
    /// Ticks every timed keyword (Cooldown, Dormant) down by one round. Called once per round for
    /// every card a character owns, regardless of which pile it is sitting in - a card recharges, or
    /// wakes up, whether or not it is in hand.
    /// </summary>
    public void TickTimers()
    {
        foreach (CardKeyword keyword in keywords)
        {
            if (keyword.type != CardKeywordType.Cooldown && keyword.type != CardKeywordType.Dormant) { continue; }

            if (keyword.remaining > 0) { keyword.remaining--; }
        }
    }

    /// <summary>
    /// Why `source` cannot play this card at all right now, wherever they aim it - or null if they
    /// can.
    ///
    /// The half of playability that does not depend on the clicked tile: Frozen, Cooldown, and the
    /// energy to pay for it. Refusal below answers the other half, the targeting one. Splitting them
    /// is what lets a highlight exist at all: a card in hand has no target yet, so it can only be
    /// asked this question, while a tile highlight can only be built from the other.
    ///
    /// Deliberately does not sweep the board for a legal target. A card with nowhere to aim still
    /// reads playable and then lights no tiles when you pick it up - one board sweep per card per
    /// refresh to close that gap is not worth it, and the empty highlight already says as much.
    /// </summary>
    public string PlayRefusal(Character source)
    {
        if (source == null) { return "nobody to play it"; }

        string actRefusal = source.ActRefusal();

        if (actRefusal != null) { return actRefusal; }

        string lockRefusal = LockRefusal();
        if (lockRefusal != null) { return lockRefusal; }

        if (!source.CanAfford(cost))
        {
            return $"{source.name} cannot afford {cost} (energy {source.Energy})";
        }

        return null;
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

        string lockRefusal = LockRefusal();
        if (lockRefusal != null) { return lockRefusal; }

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
        // Dormant is deliberately never reset here - it locks a fresh copy once and is done; that is
        // the whole difference between the two keywords.
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
