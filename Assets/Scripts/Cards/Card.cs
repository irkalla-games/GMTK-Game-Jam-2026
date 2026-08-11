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

    private Sprite areaIcon;
    private bool areaIconBuilt;

    /// A small footprint glyph summarizing every non-Single area entry on this card, or null if every
    /// entry is Single - what CardViewer draws in the corner of the card face. Built once and cached:
    /// every copy of the same CardData draws the identical shape and it never changes after the card
    /// is constructed, so there is nothing to invalidate the cache on.
    public Sprite AreaIcon()
    {
        if (!areaIconBuilt)
        {
            areaIcon = CardAreaIconBuilder.Build(effectEntries);
            areaIconBuilt = true;
        }

        return areaIcon;
    }

    /// Aliased from the asset, not copied - each entry's effect is a shared, stateless ScriptableObject
    /// resolver and the entry struct itself (aimsAt, area) is per-card authoring, not per-copy state.
    private List<CardEffectEntry> effectEntries;

    /// Per-copy runtime instances built from data.keywords - see CardKeyword.
    private readonly List<CardKeyword> keywords = new();

    public Card(CardData newData)
    {
        this.data = newData;
        this.cost = newData.cost;
        this.range = newData.range;
        this.effectEntries = newData.effectEntries;

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
    public bool HasEffect<TEffect>() where TEffect : CardEffect =>
        effectEntries.Exists(e => e.effect is TEffect);

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

        foreach (var entry in effectEntries)
        {
            if (entry.effect == null) { continue; }

            // Source-aimed effects are not asked. They land on the caster no matter where the click
            // went, so letting one object would mean Steely Attack refusing itself the moment
            // ShieldEffect grew a rule - the caster's own tile is, of course, occupied by the caster.
            if (entry.aimsAt == EffectTarget.Source) { continue; }

            // An area entry is legal anywhere in range, even a tile its own effect would refuse - a
            // 3x3 Fireball may be centred on empty ground as long as the blast catches an enemy
            // somewhere in it. Whether it actually will is a question for ResolveEffects, not this
            // gate; asking it here would mean sweeping the whole footprint before a single tile is
            // even known to be legal to click.
            if (!entry.area.IsSingle && entry.effect.SupportsArea) { continue; }

            string refusal = entry.effect.Refusal(source, target);

            if (refusal != null) { return refusal; }
        }

        return null;
    }

    /// <summary>
    /// The union of every non-Single entry's area footprint, aimed as it would be if this card were
    /// played on `hovered` right now - what GridManager paints red while aiming. Raw geometry, not
    /// filtered by who is actually standing there: the aiming preview is meant to teach the shape, and
    /// RefuseByOccupant only runs at resolve time in ResolveEffects below.
    /// </summary>
    public IEnumerable<GridTile> AreaFootprint(Character source, GridTile hovered)
    {
        HashSet<GridTile> footprint = new();

        if (GridManager.Instance == null) { return footprint; }

        GridTile casterTile = source != null ? source.Tile : null;

        foreach (var entry in effectEntries)
        {
            if (entry.effect == null || entry.area.IsSingle || !entry.effect.SupportsArea) { continue; }

            GridTile aim = entry.aimsAt == EffectTarget.Source ? casterTile : hovered;
            if (aim == null) { continue; }

            foreach (GridTile tile in GridManager.Instance.GetTilesInArea(casterTile, aim, entry.area))
            {
                footprint.Add(tile);
            }
        }

        return footprint;
    }

    /// <summary>
    /// Every tile a damage-dealing entry on this card would actually land on, aimed as it would be if
    /// played on `target` - filtered by each entry's own Refusal exactly like ResolveEffects, so an
    /// empty tile or a friendly one already drops out the same way a real play would drop it.
    ///
    /// What EnemyBrain.TryFindAttack reads to judge a card by who it would actually hit rather than
    /// just who stands on the clicked tile, which an area entry may legally leave empty.
    /// </summary>
    public IEnumerable<GridTile> DamageFootprint(Character source, GridTile target)
    {
        HashSet<GridTile> landed = new();

        GridTile casterTile = source != null ? source.Tile : null;

        foreach (var entry in effectEntries)
        {
            if (entry.effect is not DamageEffect) { continue; }

            GridTile aim = entry.aimsAt == EffectTarget.Source ? casterTile : target;
            if (aim == null) { continue; }

            if (entry.area.IsSingle || !entry.effect.SupportsArea || GridManager.Instance == null)
            {
                if (entry.effect.Refusal(source, aim) == null) { landed.Add(aim); }
                continue;
            }

            foreach (GridTile tile in GridManager.Instance.GetTilesInArea(casterTile, aim, entry.area))
            {
                if (entry.effect.Refusal(source, tile) == null) { landed.Add(tile); }
            }
        }

        return landed;
    }

    /// The furthest any one entry's area footprint reaches beyond its own aim tile - 0 for a card
    /// with no area entries. What EnemyBrain.LongestReach reads to know a splash card threatens
    /// further out than its plain click range suggests: the blast covers the rest of the gap, so an
    /// archer holding one does not need to stand as close as an ordinary card of the same range would
    /// require.
    public int WidestAreaReach()
    {
        int widest = 0;

        foreach (var entry in effectEntries)
        {
            if (entry.effect == null || !entry.effect.SupportsArea) { continue; }

            widest = Mathf.Max(widest, entry.area.MaxReach);
        }

        return widest;
    }

    /// <summary>
    /// Resolves every effect on this card, each against its own context.
    ///
    /// A context per effect, not per card - that is what lets one card point its effects at different
    /// things. Steely Attack damages the tile you clicked and armors the character who played it. An
    /// entry with a non-Single area fans its aim tile out to every tile its footprint covers and asks
    /// SupportsArea before filtering that footprint by the effect's own Refusal - that filter is the
    /// entire ally/enemy story: a Shield entry's wantAlly:true drops enemies and empty ground the same
    /// way it already does for a single tile, and a Damage entry with canHitAllies off drops allies.
    /// </summary>
    public void ResolveEffects(Character source, GridTile target)
    {
        // Resets right when the card is actually played, not when it is merely legal - a Cooldown
        // card that never gets played (e.g. discarded unused at TurnStart) must not re-lock itself.
        // Dormant is deliberately never reset here - it locks a fresh copy once and is done; that is
        // the whole difference between the two keywords.
        CardKeyword cooldown = Keyword(CardKeywordType.Cooldown);
        if (cooldown != null) { cooldown.remaining = cooldown.magnitude; }

        GridTile casterTile = source != null ? source.Tile : null;

        foreach (var entry in effectEntries)
        {
            if (entry.effect == null) { continue; }

            GridTile aim = entry.aimsAt == EffectTarget.Source ? casterTile : target;

            List<GridTile> landed;

            if (entry.area.IsSingle || !entry.effect.SupportsArea || GridManager.Instance == null)
            {
                landed = aim != null ? new List<GridTile> { aim } : new List<GridTile>();
            }
            else
            {
                landed = GridManager.Instance.GetTilesInArea(casterTile, aim, entry.area);
                landed.RemoveAll(tile => entry.effect.Refusal(source, tile) != null);

                // The aim tile leads the list when it survived its own filter - Perform (GameAction.cs)
                // and CardAnimation both read ctx.epicenter for facing/projectile/impact rather than
                // targets[0], so this ordering is cosmetic now, not load-bearing. Kept anyway so a log
                // or a future reader sees the clicked tile first.
                if (aim != null && landed.Remove(aim)) { landed.Insert(0, aim); }
            }

            entry.effect.Resolve(new ActionContext(this, source, landed, aim));
        }
    }
}
