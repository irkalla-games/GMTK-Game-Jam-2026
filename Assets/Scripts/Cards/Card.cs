using System;
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

    /// A small footprint glyph summarizing this card's reach into neighbouring tiles - what CardViewer
    /// draws in the corner of the card face. Never null: a card with no area at all draws a lone dot,
    /// so "hits one tile" and "glyph missing" cannot look the same. Built once and cached per effective
    /// shape: a plain card's shape never changes after construction, and a CardModifier-bearing one only
    /// ever changes shape through ResetToAuthored (see its doc comment), which is what clears this cache
    /// - so between those two calls the cache is still exactly as good as "never changes" used to make it.
    public Sprite AreaIcon()
    {
        if (!areaIconBuilt)
        {
            areaIcon = CardAreaIconBuilder.Build(effectEntries);
            areaIconBuilt = true;
        }

        return areaIcon;
    }

    /// <summary>
    /// Who this card affects where it is aimed - what the face's audience stripe is painted from and
    /// what the expanded tooltip puts into words.
    ///
    /// Source-aimed entries are excluded, on the same reasoning Refusal excludes them: they land on
    /// the caster no matter where the click went, so a self-shield riding along on Attack and Block
    /// says nothing about what you must aim at. Area entries *are* included, unlike in Refusal - the
    /// question here is "who does this hurt", not "which tile may I click", and a Fireball centred on
    /// empty ground is still an enemy card.
    ///
    /// A tile-aimed entry always wins, and the first one with an opinion settles it - Refusal returns
    /// the first refusal it meets, so the first restrictive effect is the one a player collides with.
    /// Source-aimed entries are only consulted when there are no tile-aimed ones at all: a pure
    /// self-buff like Guardian's Aegis would otherwise have no audience to report.
    /// </summary>
    public TargetAudience Audience
    {
        get
        {
            TargetAudience fromSource = TargetAudience.Unrestricted;

            foreach (CardEffectEntry entry in effectEntries)
            {
                if (entry.effect == null) { continue; }

                TargetAudience audience = entry.effect.Audience;

                if (audience == TargetAudience.Unrestricted) { continue; }

                if (entry.aimsAt != EffectTarget.Source) { return audience; }

                if (fromSource == TargetAudience.Unrestricted) { fromSource = audience; }
            }

            return fromSource;
        }
    }

    /// <summary>
    /// True when the caster is the only character this card can possibly reach: the one legal tile is
    /// their own, and nothing spreads off it.
    ///
    /// Separate from Audience because "ally" and "me" are the same answer to the effect and different
    /// answers to the player. Guardian's Aegis is SelfTile too, but its area carries it to neighbours,
    /// so it is not self-only.
    /// </summary>
    public bool AffectsSelfOnly => range.Shape == RangeShape.SelfTile && WidestAreaReach() == 0;

    /// The asset this copy was built from - what CardFilter/CardTag matching (equipment, upgrades) reads
    /// to decide whether a modifier applies, since a Card itself carries no tags of its own.
    public CardData Data => data;

    /// Read-only so a describer can walk the aims and footprints without being able to re-author them.
    /// CardData already exposes the same list; this is Card's matching window onto it. Use
    /// SetEntry/AddEntry/RemoveEntriesWhere to write - see their doc comments for why those exist at
    /// all rather than exposing the list directly.
    public IReadOnlyList<CardEffectEntry> EffectEntries => effectEntries;

    /// <summary>
    /// A copy of the asset's list, not a reference to it - CardEffectEntry is a struct, so `new List<>`
    /// gives every entry its own independent storage. This used to alias newData.effectEntries directly,
    /// which was fine while nothing but ResolveEffects ever read it; the moment a CardModifier (an
    /// upgraded variant, an equipment tuning) writes into an entry, aliasing would write straight through
    /// into the shared CardData asset - the same "never mutate a ScriptableObject at runtime" hazard cost
    /// and range are already copied to avoid.
    /// </summary>
    private List<CardEffectEntry> effectEntries;

    /// Per-copy runtime instances built from data.keywords - see CardKeyword.
    private readonly List<CardKeyword> keywords = new();

    public Card(CardData newData)
    {
        this.data = newData;
        ResetToAuthored();
    }

    /// <summary>
    /// Re-seeds cost, range, effect entries and keywords from the authored CardData, discarding any
    /// CardModifier writes this copy has accumulated - the reset half of "reset then reapply" that makes
    /// re-equipping mid-battle safe to run more than once. Also what the constructor calls, so there is
    /// exactly one place that knows how a fresh Card is built from its data.
    ///
    /// Clears the AreaIcon cache too: that cache used to be safe to build once and never invalidate
    /// because a Card's shape never changed after construction. A CardModifier changing area is the one
    /// thing that now falsifies that, and this is the only place a modifier's effects are ever discarded,
    /// so it is the only place that needs to know the cache might now be stale.
    /// </summary>
    public void ResetToAuthored()
    {
        this.cost = data.cost;
        this.range = data.range;
        this.effectEntries = new List<CardEffectEntry>(data.effectEntries);

        keywords.Clear();

        // Cooldown starts at 0 - ready the first time, locked again only after each play (see
        // ResolveEffects). Dormant starts locked at its own magnitude and never relocks - that is the
        // entire difference between the two keywords.
        foreach (CardKeywordEntry entry in data.keywords)
        {
            int remaining = entry.type == CardKeywordType.Dormant ? entry.magnitude : 0;
            keywords.Add(new CardKeyword(entry.type, entry.magnitude, remaining));
        }

        areaIconBuilt = false;
    }

    /// How many effect entries this copy currently has - for a CardModifier walking them by index
    /// without being able to add or remove through the read-only EffectEntries view.
    public int EntryCount => effectEntries.Count;

    public CardEffectEntry GetEntry(int index) => effectEntries[index];

    /// Overwrites one entry outright. The only way a CardModifier changes an entry's area, amount
    /// adjustment or effect asset - callers read GetEntry, mutate the returned copy (entries are
    /// structs), and write it back here.
    public void SetEntry(int index, CardEffectEntry entry)
    {
        effectEntries[index] = entry;
        areaIconBuilt = false;
    }

    /// Appends a whole new effect entry - AddEntryModifier's write path (an upgraded Poison Dagger that
    /// also draws a card, say).
    public void AddEntry(CardEffectEntry entry)
    {
        effectEntries.Add(entry);
        areaIconBuilt = false;
    }

    /// Drops every entry the predicate matches - RemoveEntryModifier's write path. Walked backwards so
    /// removing by index is safe mid-loop.
    public void RemoveEntriesWhere(Func<CardEffectEntry, int, bool> predicate)
    {
        for (int i = effectEntries.Count - 1; i >= 0; i--)
        {
            if (predicate(effectEntries[i], i)) { effectEntries.RemoveAt(i); }
        }

        areaIconBuilt = false;
    }

    /// Adds a keyword this copy did not have before - KeywordModifier's write path. If one of this type
    /// is already present it is replaced outright rather than merged: keyword magnitude is authoring,
    /// not a stacking counter the way Status.stacks is.
    public void AddKeyword(CardKeywordType type, int magnitude)
    {
        keywords.RemoveAll(k => k.type == type);

        int remaining = type == CardKeywordType.Dormant ? magnitude : 0;
        keywords.Add(new CardKeyword(type, magnitude, remaining));
    }

    /// Strips a keyword this copy had - KeywordModifier's other write path (an upgraded Bash that lost
    /// its Cooldown).
    public void RemoveKeyword(CardKeywordType type)
    {
        keywords.RemoveAll(k => k.type == type);
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
    /// RefuseByAudience only runs at resolve time in ResolveEffects below.
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
    /// One entry's landed tiles, filtered by its own Refusal exactly like ResolveEffects - the walk
    /// shared by DamageFootprint (which only wants the tiles) and PreviewDamage (which also wants an
    /// amount per tile). `aim` is the entry's own aim tile, already resolved by the caller since a
    /// Source-aimed entry reads the caster's tile instead of `target`.
    /// </summary>
    private IEnumerable<GridTile> EntryFootprint(Character source, GridTile casterTile, GridTile aim, CardEffectEntry entry)
    {
        if (entry.area.IsSingle || !entry.effect.SupportsArea || GridManager.Instance == null)
        {
            if (entry.effect.Refusal(source, aim) == null) { yield return aim; }
            yield break;
        }

        foreach (GridTile tile in GridManager.Instance.GetTilesInArea(casterTile, aim, entry.area))
        {
            if (entry.effect.Refusal(source, tile) == null) { yield return tile; }
        }
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

            foreach (GridTile tile in EntryFootprint(source, casterTile, aim, entry)) { landed.Add(tile); }
        }

        return landed;
    }

    /// <summary>
    /// Health each character in this card's damage footprint would actually lose if it were played on
    /// `target` right now - what GridManager.ShowDamagePreview reads to paint the hover preview.
    ///
    /// Runs the real pipeline both directions with nothing spent: Character.ComputeOutgoingDamage on
    /// the attacker (Strength, Double Attack) once per entry, matching DamageAction.Execute's own
    /// "once, not per tile" rule so an AoE does not report the buff several times over; then
    /// Character.ComputeIncomingDamage on each occupant (Block, Shield, Parry, Dodge). Both calls pass
    /// shouldConsume: false, so hovering never spends a real charge - see DamageInfo.consumeCharges.
    ///
    /// A card with two damage entries landing on the same character under-reports: neither previewed
    /// hit depletes the charges the other would face in a real play, where the first entry's
    /// DamageAction would already have spent them before the second resolved. Every damage card today
    /// has a single entry; a truthful multi-hit preview would need to clone the status list per entry,
    /// which is not worth it for that case.
    /// </summary>
    public Dictionary<Character, int> PreviewDamage(Character source, GridTile target)
    {
        Dictionary<Character, int> loss = new();

        if (source == null) { return loss; }

        GridTile casterTile = source.Tile;

        foreach (var entry in effectEntries)
        {
            if (entry.effect is not DamageEffect damage) { continue; }

            GridTile aim = entry.aimsAt == EffectTarget.Source ? casterTile : target;
            if (aim == null) { continue; }

            ActionContext ctx = new(this, source, Array.Empty<GridTile>(), aim, entry.amountDelta, entry.amountPercent);
            int outgoing = source.ComputeOutgoingDamage(ctx.Amount(damage.Damage), shouldConsume: false);

            foreach (GridTile tile in EntryFootprint(source, casterTile, aim, entry))
            {
                Character victim = tile.Occupant;
                if (victim == null) { continue; }

                int lost = victim.ComputeIncomingDamage(outgoing, source, shouldConsume: false).amount;
                if (lost <= 0) { continue; }

                loss[victim] = loss.TryGetValue(victim, out int existing) ? existing + lost : lost;
            }
        }

        return loss;
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

            entry.effect.Resolve(new ActionContext(this, source, landed, aim, entry.amountDelta, entry.amountPercent));
        }
    }
}
