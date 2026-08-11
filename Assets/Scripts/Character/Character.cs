using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A unit on the board. Everything a card can do to a character arrives through the tile it's standing
/// on, so these are the entry points GridTile forwards to.
///
/// **This class resolves no combat rules.** Mitigation, damage bonuses, poison, freezing and rooting
/// all live in Status subclasses; what remains here is running the hooks and subtracting whatever
/// survives them - see TakeDamage. There is deliberately no Shield/Block/Parry on a Character: it has
/// a status list that may contain one, and anything wanting a number asks the list for it by type.
///
/// Each character owns its own deck and piles. Clicking a character makes it the active one, and its
/// hand is what you see; there is no turn order.
/// </summary>
public class Character : MonoBehaviour
{
    [SerializeField] public int handSize = 5;

    [SerializeField] private int maxHealth = 10;

    [SerializeField] private int maxEnergy = 3;

    [SerializeField] private PlayableCharacter playableCharacter = PlayableCharacter.Enemy;

    [Tooltip("Which cards this character may hold. Cards not matching are skipped when the deck is "
             + "built.")]
    [SingleClass]
    [SerializeField] private CharacterClass characterClass;

    [Tooltip("Shown in player-facing UI instead of this GameObject's raw name (which is either the "
             + "prefab's authoring name, e.g. \"PlayerKnight\", or has a runtime suffix like \"(Clone)\" "
             + "or a spawn coordinate appended). Leave blank to fall back to gameObject.name.")]
    [SerializeField] private string displayName;

    [Tooltip("Actions this character takes per turn. Enemies only - players spend energy instead.")]
    [SerializeField] private int actionPoints = 2;

    [Tooltip("Which rule list this enemy runs. None means it stands there - correct for players.")]
    [SerializeField] private BrainType brain;

    [Tooltip("The order this enemy picks its attack targets in. Leave empty for the old behaviour - "
             + "always the most hurt legal target.")]
    [SerializeField] private TargetingPattern targetingPattern;

    // No attack damage, reach or move speed here. Those are properties of the cards a character
    // holds - a goblin hits for 6 because it is holding a card that deals 6, and reaches two tiles
    // because that card's TargetRange says two. Duplicating them onto the character would be a
    // second answer to a question the card already answers, and the two would drift.

    [Tooltip("Grid cell this character starts on. Placed onto that tile at battle start.")]
    [OneBasedCell]
    [SerializeField] private Vector2Int startCoordinates;

    [Tooltip("Spawned on this character's tile when it dies. Leave empty for characters that drop "
             + "nothing - party members typically do.")]
    [SerializeField] private GameObject itemDropPrefab;

    [Tooltip("Odds and bias for this character's own drop. Leave empty to use the level's default "
             + "LootTable - only set this for an enemy whose loot should differ from the rest, e.g. a "
             + "poison-themed enemy favouring poison cards.")]
    [SerializeField] private LootTable lootTable;

    [Tooltip("This character's deck as authored. Card objects are built from it once, in Awake.")]
    [SerializeField] private List<CardData> deck = new();

    // Card objects live here for the whole battle and move between piles. They are *not* rebuilt on
    // each draw - a Card carries per-copy state (temporary cost, ethereal) that has to survive being
    // played and redrawn.
    private readonly List<Card> drawPile = new();

    private readonly List<Card> hand = new();

    private readonly List<Card> discardPile = new();

    /// Cards with the Innate keyword - always in hand, tracked separately so a played or discarded one
    /// can find its way back. Subset of the Card objects built from `deck`, not a second deck.
    private readonly List<Card> innateCards = new();

    /// The statuses this character carries *itself* - the ones cards applied, that age with its own
    /// turns and merge when re-applied. Buffs and curses alike, one list, because they are the same
    /// machinery. Named for the distinction that matters: auras are never in here, they belong to the
    /// totems projecting them and only join the picture in ActiveStatuses.
    private readonly List<StatusEffect> ownStatusEffects = new();

    /// Current health. Serialized only so the live value is watchable in the Inspector during Play
    /// Mode; [ReadOnlyField] greys it out so nobody can type into it. Awake overwrites whatever was
    /// saved, so the stored value is a readout, never authoring data - edit Max Health instead.
    [field: SerializeField, ReadOnlyField]
    public int Health { get; private set; }

    public int MaxHealth => maxHealth;

    /// Each character has their own pool; playing a card spends the acting character's energy.
    public int Energy { get; private set; }

    public PlayableCharacter Affiliation => playableCharacter;

    /// True only for the party the player clicks to control directly. Ally, EnemyAllied and Neutral
    /// are all AI-resolved, same as Enemy - see BattleManager.LivingEnemies.
    public bool IsPlayerControlled => playableCharacter == PlayableCharacter.AllyPlayable;

    public CharacterClass Class => characterClass;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    public int ActionPoints => actionPoints;

    public BrainType Brain => brain;

    public GameObject ItemDropPrefab => itemDropPrefab;

    /// This character's own loot table, or null to fall back to the level's - see
    /// BattleManager.HandleCharacterDied, which is the only reader.
    public LootTable LootTable => lootTable;

    /// <summary>
    /// Loot this character has stolen by walking over it without picking it up (see
    /// GridTile.TryPickUpItem - only a player-controlled character actually claims an item). Rarity
    /// paired with the table it came from, so a poison skeleton's stolen loot still favours poison
    /// cards if it is recovered from whoever carried it off. The ItemPickup GameObject itself is
    /// destroyed on theft; this only remembers what to respawn on death.
    /// </summary>
    private readonly List<(Rarity rarity, LootTable table)> carriedLoot = new();

    public IReadOnlyList<(Rarity rarity, LootTable table)> CarriedLoot => carriedLoot;

    public void CarryLoot(Rarity rarity, LootTable table) => carriedLoot.Add((rarity, table));

    /// How far through targetingPattern this *copy* is. Per-copy runtime state, exactly like
    /// Card.cost against CardData.cost - the asset is shared by every skeleton on the board and
    /// writing a cursor into it would persist into the .asset on disk when Play Mode exits.
    private int targetingCursor;

    /// Who this character is going after right now. Named by the pattern; the whole point is that it
    /// names the attack's victim *and* the direction a move heads in, so a Move in the sequence walks
    /// toward whoever the next attack means to hit. See TargetSelector.
    public TargetPriority CurrentPriority =>
        targetingPattern != null ? targetingPattern.At(targetingCursor) : TargetPriority.Weakest;

    /// Spent by attacks only, and only once one has actually resolved - see BattleManager.Execute. A
    /// refused or fizzled swing leaves the sequence exactly where it was, so an owed "Attack: Closest"
    /// stays owed.
    public void AdvanceTargetingCursor() => targetingCursor++;

    private Intent committedIntent;

    /// <summary>
    /// What this enemy would do if its turn came right now - what the overhead icon shows.
    ///
    /// Kept live rather than decided once: BattleManager recomputes it whenever the board changes (see
    /// BattleManager.LateUpdate), and re-derives it outright, kind included, the moment the enemy
    /// actually acts. There is no separate "stale but committed" state - moving a hero out of an
    /// archer's reach turns its Attack back into a Move rather than leaving it to swing at nothing.
    /// </summary>
    public Intent CommittedIntent
    {
        get => committedIntent;
        set { committedIntent = value; IntentChanged?.Invoke(this); }
    }

    public bool IsDead => Health <= 0;

    /// <summary>
    /// Whether this character can do anything at all this turn. Frozen is what says no - checked
    /// before a card is paid for, and by the turn loop when it asks whether anybody can still act,
    /// since a fully frozen party would otherwise stall the turn forever.
    ///
    /// Deliberately not a targeting rule: it does not depend on the tile clicked, so it does not
    /// belong in Card.Refusal. Rooted is the one that objects per-destination, and it does that
    /// through GridManager.MoveRefusal instead.
    /// </summary>
    public bool CanAct => ActRefusal() == null;

    /// Why this character cannot act at all right now, or null if it can. The reason rather than a
    /// bare bool so a refused click can say what stopped it - see CardPlayManager.
    public string ActRefusal()
    {
        if (IsDead) { return $"{name} is down"; }

        foreach (Status status in ActiveStatuses())
        {
            string refusal = status.ActRefusal(this);

            if (refusal != null) { return refusal; }
        }

        return null;
    }

    /// The tile this character is standing on.
    public GridTile Tile { get; private set; }

    /// This character's own animation vocabulary, or null on a body with no CharacterAnimator - the
    /// Totem, or any prefab nobody has wired up yet. GameAction.Perform already no-ops on null, so
    /// nothing downstream needs its own guard for this.
    public CharacterAnimator Animation { get; private set; }

    /// The cards currently held. ActiveHandViewer builds the viewers for whichever character is active.
    public IReadOnlyList<Card> Hand => hand;

    /// Raised when a card lands in this hand. ActiveHandViewer listens so the row on screen follows the
    /// active character - drawing itself is none of its business.
    public event Action<Character, Card> CardDrawn;

    /// Raised when a card leaves this hand for the discard pile, whether played or discarded wholesale
    /// at turn start. ActiveHandViewer listens and removes the matching viewer - discarding itself is
    /// none of its business, same as CardDrawn.
    public event Action<Character, Card> CardDiscarded;

    /// Raised once, when this character drops to 0 and leaves the board.
    public event Action<Character> Died;

    /// <summary>
    /// Raised from TakeDamage once a hit has been through every mitigation and something actually got
    /// through - `amount` is what survived, never the raw incoming number. A hit Shield or Block
    /// absorbed entirely raises nothing, which is deliberate: CharacterAnimator plays a flinch off this
    /// event, and a fully blocked hit should visibly not flinch. Not raised by TakeUnblockableDamage -
    /// poison is not an attack, same distinction that method's own doc comment draws.
    /// </summary>
    public event Action<Character, int> Damaged;

    /// <summary>
    /// Raised for every attack TakeDamage processes, carrying how much of it actually registered
    /// against this character - Health lost, plus anything a Shield status absorbed into its own pool
    /// (DamageInfo.shieldAbsorbed). A Block reduction and a Parry negation are both excluded from that
    /// figure on purpose: neither redirects the damage anywhere, they simply prevent it, so a hit that
    /// Block eats three points of reports the five that got through rather than the eight that were
    /// swung, and a hit Parry eats outright reports zero rather than nothing.
    ///
    /// Unlike Damaged this fires even when the total is zero. Damaged skips a fully mitigated hit so
    /// CharacterAnimator's flinch does not play on it - that contract stays exactly as it is - but a
    /// damage-number popup still wants to say "this landed for nothing" instead of showing nothing at
    /// all, which is the whole reason this is a second event rather than a change to Damaged.
    /// </summary>
    public event Action<Character, int> DamageRegistered;

    /// <summary>
    /// Raised from TakeUnblockableDamage with the nominal amount - poison is not an attack and runs no
    /// OnTakeDamage hooks, so unlike Damaged there is no post-mitigation figure to report; the number
    /// dealt is the number that lands. Kept separate from Damaged rather than folded into it because
    /// CharacterAnimator's flinch is keyed off Damaged, and a poison tick should not flinch.
    /// </summary>
    public event Action<Character, int> DamagedUnblockable;

    /// <summary>
    /// Raised from Heal with the actual amount restored, not the amount requested - Heal clamps at
    /// maxHealth, so a full-health character healed for 20 gained nothing and should raise nothing.
    /// Not raised at all when the delta is zero, same reasoning as Damaged skipping a fully mitigated
    /// hit.
    /// </summary>
    public event Action<Character, int> Healed;

    /// <summary>
    /// Raised whenever this character's health, shield, energy or status list changed. Character
    /// knows nothing about who is listening - exactly the CardDrawn contract, and the reason
    /// CharacterOverheadViewer exists at all rather than this class writing straight at a Text field.
    ///
    /// Energy is in that list because playability is drawn from it: what a card highlight needs to
    /// know is exactly "did anything about this character change", and splitting energy out into an
    /// event of its own would only mean every listener subscribing to both.
    ///
    /// Carries only the subject, no numbers. A listener re-reads Health, MaxHealth and
    /// StatusStacks(Shield) itself, so a future fourth number on the bar needs no new event - the
    /// same reasoning as BattleManager.TurnAdvanced being parameterless.
    /// </summary>
    public event Action<Character> StatsChanged;

    private void RaiseStatsChanged() => StatsChanged?.Invoke(this);

    /// Raised when this character commits or clears an intent. Same contract as StatsChanged: an
    /// enemy announcing what it means to do is not the same thing as drawing an icon over its head.
    public event Action<Character> IntentChanged;

    /// True if these two affiliations are on opposing sides. Neutral is on nobody's side, so it is
    /// never an enemy - a card that wants an enemy target simply refuses it.
    public static bool AreEnemies(PlayableCharacter a, PlayableCharacter b)
    {
        int sideA = Side(a);
        int sideB = Side(b);

        return sideA != 0 && sideB != 0 && sideA != sideB;
    }

    /// True if these two affiliations are on the same side. Neutral is never allied with anyone,
    /// including another Neutral.
    public static bool AreAllies(PlayableCharacter a, PlayableCharacter b)
    {
        int sideA = Side(a);

        return sideA != 0 && sideA == Side(b);
    }

    /// AllyPlayable/Ally are +1, Enemy/EnemyAllied are -1, Neutral is 0 and matches nothing.
    private static int Side(PlayableCharacter affiliation) => affiliation switch
    {
        PlayableCharacter.AllyPlayable or PlayableCharacter.Ally => 1,
        PlayableCharacter.Enemy or PlayableCharacter.EnemyAllied => -1,
        _ => 0,
    };

    public bool IsEnemyOf(Character other) => other != null && AreEnemies(playableCharacter, other.playableCharacter);

    public bool CanAfford(int cost) => cost <= Energy;

    public void SpendEnergy(int cost)
    {
        Energy = Mathf.Max(0, Energy - cost);
        BattleManager.Instance.ChangeActiveMana(this.Energy);
        RaiseStatsChanged();
    }

    public void ResetEnergy()
    {
        Energy = maxEnergy;
        RaiseStatsChanged();
    }

    /// <summary>
    /// The single choke point all ordinary damage passes through - GridTile.DealDamage forwards here.
    ///
    /// Every defense is a status, so this runs the OnTakeDamage hooks and subtracts whatever survives
    /// them. It does not know that Parry, Block or Shield exist, and adding a fourth mitigation type
    /// needs no change here at all.
    ///
    /// attacker is who to reflect a parried hit back at - null (from sources with no attacker) just
    /// means a parried hit vanishes instead of reflecting.
    ///
    /// `bounces` is only ever non-zero on a reflected hit. See MaxParryBounces.
    /// </summary>
    public void TakeDamage(int amount, Character attacker = null, int bounces = 0)
    {
        if (amount <= 0) { return; }

        // `info` is the accumulator, not a per-status value: each hook takes the hit as the previous
        // one left it and returns the next, so mitigations compose. A hit of 10 against Block 3 then
        // Shield 4 goes 10 -> 7 -> 3. Rebuilding it inside the loop would hand every status the
        // original 10 and only the last one's result would survive.
        DamageInfo info = new(attacker, this, amount);

        foreach (Status status in ActiveStatuses())
        {
            info = status.OnTakeDamage(info);

            // Parry cancelled the hit outright - there is nothing left for anything else to reduce.
            if (info.negated) { break; }
        }

        PruneExpired();

        Reflect(info, attacker, bounces);

        Health = Mathf.Max(0, Health - info.amount);
        RaiseStatsChanged();

        // Only when something actually landed - a hit Shield or Block absorbed entirely raises
        // nothing, which is the point: CharacterAnimator's flinch is keyed off this, so a fully
        // blocked hit visibly does not flinch.
        if (info.amount > 0) { Damaged?.Invoke(this, info.amount); }

        // Health lost plus whatever a Shield status siphoned into its own pool - see the doc on
        // DamageRegistered for why that is the right number and why this fires even at zero.
        DamageRegistered?.Invoke(this, info.amount + info.shieldAbsorbed);

        CheckDeath();
    }

    /// <summary>
    /// How many times one hit may bounce between two characters who are both parrying before it is
    /// dropped on the floor.
    ///
    /// Ordinarily a bounce war ends on its own, because every parry spends a charge and charges are
    /// finite. An aura is the exception: Totem hands out a fresh Status object on every query, so an
    /// aura-granted Parry never depletes and two characters standing in one would bounce a single hit
    /// forever. This is the backstop for that.
    /// </summary>
    private const int MaxParryBounces = 8;

    /// <summary>
    /// Sends a parried hit back at whoever threw it - through the ordinary front door, not around it.
    ///
    /// That is the whole point: the attacker's own statuses all get their say, so their Shield absorbs
    /// it, their Block reduces it, and their Parry sends it straight back here again.
    /// </summary>
    private void Reflect(DamageInfo info, Character attacker, int bounces)
    {
        if (info.reflected <= 0 || attacker == null) { return; }

        if (bounces >= MaxParryBounces)
        {
            Debug.LogWarning($"{name}: parry bounce limit reached, dropping {info.reflected} reflected "
                             + "damage - is an aura granting Parry to both sides?");
            return;
        }

        attacker.TakeDamage(info.reflected, this, bounces + 1);
    }

    /// Damage that runs no OnTakeDamage hooks at all, so nothing can mitigate it. Poison is not an
    /// attack - defenses are things held up against something being thrown at you, and they do nothing
    /// about something already in your blood. Note a reflected parry does *not* come through here: it
    /// goes back through TakeDamage so it can be parried in turn.
    public void TakeUnblockableDamage(int amount)
    {
        if (amount <= 0) { return; }

        Health = Mathf.Max(0, Health - amount);
        RaiseStatsChanged();
        DamagedUnblockable?.Invoke(this, amount);

        CheckDeath();
    }

    /// <summary>
    /// Leaves the board on death. MoveTo(null) is the only path that clears GridTile.Occupant, so
    /// without this a corpse holds its tile for the rest of the battle - blocking movement, refusing
    /// Move cards aimed at it, and soaking attacks that should have hit somebody alive.
    ///
    /// Died fires before MoveTo(null) on purpose: BattleManager drops loot on this character's Tile
    /// when it reacts to Died, and that tile reads null the instant MoveTo runs.
    /// </summary>
    private void CheckDeath()
    {
        if (!IsDead || Tile == null) { return; }

        Debug.Log($"{name} is down");

        Died?.Invoke(this);
        MoveTo(null);
    }

    public void Heal(int amount)
    {
        int before = Health;
        Health = Mathf.Min(maxHealth, Health + amount);
        RaiseStatsChanged();

        // The delta, not `amount` - see the doc on Healed. A full-health character healed for 20
        // gained nothing and should pop no number.
        if (Health > before) { Healed?.Invoke(this, Health - before); }
    }

    /// <summary>
    /// Sets health outright, for a character arriving from somewhere that already knows how hurt it
    /// is - a party member carrying damage in from the previous level. Not Heal: that clamps upward
    /// only and so can never bring a character in at less than full.
    ///
    /// Floored at 1 rather than 0. A member who reached 0 is removed from the run entirely, so a
    /// record asking for a dead character to be spawned is a bug somewhere else, and spawning one
    /// that immediately dies would hide it.
    /// </summary>
    public void SetHealth(int value)
    {
        Health = Mathf.Clamp(value, 1, maxHealth);
        RaiseStatsChanged();
    }

    /// <summary>
    /// Overrides DisplayName for this instance - for a spawned party member sharing its class with
    /// another member of the same run, where "Knight" and "Knight" would otherwise be indistinguishable
    /// in every player-facing readout (SelectedCharacterPanel, reward titles). Never touches the
    /// prefab's own authored name; this is per-instance state set after Instantiate, same as SetDeck
    /// and SetHealth.
    /// </summary>
    public void SetDisplayName(string value)
    {
        displayName = value;
    }

    /// <summary>
    /// Every status affecting this character right now: the auras totems are projecting onto its tile
    /// first, then its own, each in the order it was gained.
    ///
    /// Auras come first so they always resolve ahead of anything the character is carrying itself, and
    /// building the list in that order is the whole enforcement - there is no sort. That also means
    /// hooks run FIFO, so mitigation order and the outgoing damage total both depend on which status
    /// landed first. See Status.
    ///
    /// A fresh list every call rather than a reused buffer: a Poison tick can kill the carrier, and a
    /// Died handler running mid-iteration could otherwise clobber the buffer being walked.
    /// </summary>
    public List<Status> ActiveStatuses()
    {
        List<Status> aurasThenOwn = new();

        // Auras first - projected by whichever totems reach this character's tile right now, and
        // resolved ahead of anything it carries itself.
        Totem.CollectAuras(this, aurasThenOwn);

        // Then the character's own, in the order they were applied.
        aurasThenOwn.AddRange(ownStatusEffects);

        return aurasThenOwn;
    }

    /// How much of `type` this character currently has, auras and own statuses combined - callers
    /// should not have to care which it came from.
    public int StatusStacks(StatusType type)
    {
        int total = 0;

        foreach (Status status in ActiveStatuses())
        {
            if (status.type == type) { total += status.stacks; }
        }

        return total;
    }

    /// The status object of this type, or null. For anything that needs more than a stack count - a UI
    /// asking Block for both of its numbers, say.
    public Status FindStatus(StatusType type)
    {
        foreach (Status status in ActiveStatuses())
        {
            if (status.type == type) { return status; }
        }

        return null;
    }

    /// Applies a status by type, stacking onto one already present. The convenience form for the
    /// statuses whose whole state is one number - GridTile.ApplyStatus and friends.
    public void AddStatus(StatusType type, int stacks, int turnsRemaining)
    {
        AddStatus(StatusEffect.Create(type, stacks, turnsRemaining));
    }

    /// <summary>
    /// Applies an already-built status, merging into one of the same type if it is already there.
    ///
    /// The object form exists for Block, whose two numbers do not fit the type/stacks/turns signature.
    /// How a top-up combines is the status's own business - see StatusEffect.Merge.
    ///
    /// Takes a StatusEffect, not a Status: an Aura is owned by its totem and rebuilt on every query,
    /// so there is nothing here for one to be added to. The type signature is what says so.
    /// </summary>
    public void AddStatus(StatusEffect incoming)
    {
        if (incoming == null || incoming.type == StatusType.None || incoming.stacks <= 0) { return; }

        foreach (StatusEffect existing in ownStatusEffects)
        {
            if (existing.type != incoming.type) { continue; }

            existing.Merge(incoming);
            RaiseStatsChanged();

            return;
        }

        ownStatusEffects.Add(incoming);
        RaiseStatsChanged();
    }

    /// <summary>
    /// The top of a round, before anybody acts. Shield wipes itself here; everything else ignores it.
    ///
    /// Replaces the old ResetShield, and is why this class no longer knows that shield decays: that is
    /// a fact about Shield, held in ShieldStatus.
    /// </summary>
    public void OnTurnStart()
    {
        foreach (Status status in ActiveStatuses()) { status.OnTurnStart(this); }

        PruneExpired();
        RaiseStatsChanged();
    }

    /// <summary>
    /// The end of this character's own phase: statuses that do something on a passing turn do it, then
    /// every timed status ages by one and the expired ones drop. Statuses with no duration (Strength,
    /// and charge-spent ones like Double Attack) are untouched.
    ///
    /// Called by BattleManager at the end of this character's own phase - PlayerActing for
    /// player-controlled characters, EnemyResolve for enemies - never at the shared TurnStart. See
    /// BattleManager.TickStatuses(bool) for why the timing matters.
    ///
    /// Hooks run before durations age, so a status with one turn left still gets its last tick. Only
    /// this character's own statuses age: an aura has no duration of its own, it lasts exactly as long
    /// as you stand in it.
    ///
    /// Iterated backwards because expiring a status removes it mid-loop.
    /// </summary>
    public void OnTurnEnd()
    {
        foreach (Status status in ActiveStatuses()) { status.OnTurnEnd(this); }

        // Only the carried ones age. An aura has no duration of its own - Aura.turnsRemaining is
        // permanently Indefinite - so it would be nothing but a no-op here anyway.
        for (int i = ownStatusEffects.Count - 1; i >= 0; i--)
        {
            if (ownStatusEffects[i].turnsRemaining > 0) { ownStatusEffects[i].turnsRemaining--; }

            if (ownStatusEffects[i].IsExpired) { ownStatusEffects.RemoveAt(i); }
        }
    }

    /// Drops carried statuses whose hooks just spent their last charge. Only walks this character's own
    /// list - the aura entries in ActiveStatuses are throwaways owned by a totem.
    private void PruneExpired()
    {
        for (int i = ownStatusEffects.Count - 1; i >= 0; i--)
        {
            if (ownStatusEffects[i].IsExpired) { ownStatusEffects.RemoveAt(i); }
        }
    }

    /// <summary>
    /// What this character's attack lands for, once every OnDealDamage hook has had a turn.
    ///
    /// shouldConsume is the whole difference between swinging and looking. True spends charges and
    /// belongs to the one place actually attacking - DamageAction. False leaves them untouched, for
    /// tooltips, damage previews and enemy AI scoring a move it has not made yet. Passing true to
    /// display a number would destroy the buff without an attack ever happening.
    /// </summary>
    public int ComputeOutgoingDamage(int amount, bool shouldConsume)
    {
        // Same accumulator shape as TakeDamage - each hook takes the total as the last one left it.
        DamageInfo info = new(this, null, amount, consumeCharges: shouldConsume);

        foreach (Status status in ActiveStatuses()) { info = status.OnDealDamage(info); }

        if (shouldConsume) { PruneExpired(); }

        return info.amount;
    }

    /// <summary>
    /// Grants this character shield, once every OnGainShield hook has had a turn - the shield-gain
    /// counterpart to ComputeOutgoingDamage, and the reason ShieldInfo exists: without a pipeline here
    /// a status like Double Shield has nothing to intercept, since GridTile.GainShield used to hand
    /// straight off to AddStatus.
    ///
    /// Only ShieldAction/GridTile.GainShield route through here today - a card authored directly as
    /// ApplyStatusEffect(status: Shield) reaches AddStatus without running this pipeline, so it will
    /// not be doubled. Author shield gains as a ShieldEffect if that matters.
    /// </summary>
    public void GainShield(int amount, bool shouldConsume = true)
    {
        // Guarding zero before the hooks matters: a 0-shield gain must not spend a Double Shield charge.
        if (amount <= 0) { return; }

        ShieldInfo info = new(this, amount, consumeCharges: shouldConsume);

        foreach (Status status in ActiveStatuses()) { info = status.OnGainShield(info); }

        if (shouldConsume) { PruneExpired(); }

        AddStatus(StatusType.Shield, info.amount, Status.Indefinite);
    }

    public void MoveTo(GridTile moveTo)
    {
        if (Tile != null && Tile.Occupant == this)
        {
            Tile.SetOccupant(null);
        }
        Tile = moveTo;
        if (moveTo != null)
        {
            moveTo.SetOccupant(this);

        }
    }

    public void DrawCards(int amount)
    {
        for (int i = 0; i < amount; i++) { DrawCard(); }
    }

    /// Moves one card from this character's draw pile into its own hand and returns it, or null if
    /// there is nothing left to draw even after reshuffling.
    public Card DrawCard()
    {
        if (drawPile.Count == 0)
        {
            ReshuffleDiscardIntoDrawPile();

            if (drawPile.Count == 0)
            {
                Debug.Log($"{name} has nothing left to draw");
                return null;
            }
        }

        int last = drawPile.Count - 1;
        Card card = drawPile[last];
        drawPile.RemoveAt(last);
        hand.Add(card);

        CardDrawn?.Invoke(this, card);

        return card;
    }

    public void Discard(Card card)
    {
        if (!hand.Remove(card)) { return; }

        // Innate cards never enter the discard pile - RestoreInnateCards puts them straight back into
        // hand next turn instead of them needing to be reshuffled back in.
        if (!card.HasKeyword(CardKeywordType.Innate)) { discardPile.Add(card); }

        CardDiscarded?.Invoke(this, card);
    }

    /// <summary>
    /// Puts back any innate card that DiscardHand or a play removed from hand. Called before the
    /// normal top-up draw each turn so innate cards occupy real hand slots rather than inflating hand
    /// size past HandSize.
    /// </summary>
    public void RestoreInnateCards()
    {
        foreach (Card card in innateCards)
        {
            if (hand.Contains(card)) { continue; }

            hand.Add(card);
            CardDrawn?.Invoke(this, card);
        }
    }

    /// <summary>
    /// Ticks every card's timed keywords (Cooldown, Dormant) down by one round. Safe to only walk
    /// these three piles rather than innateCards too, as long as this runs after RestoreInnateCards
    /// each turn - by then every innate card is already back in hand, and a rewarded innate card
    /// (added straight to hand by AddCardToHand/AddCard) is covered by the same walk.
    /// </summary>
    public void TickCardTimers()
    {
        foreach (Card card in drawPile) { card.TickTimers(); }
        foreach (Card card in hand) { card.TickTimers(); }
        foreach (Card card in discardPile) { card.TickTimers(); }
    }

    /// <summary>
    /// The deck as authored on this prefab, for anything seeding a mutable copy from it - a run's
    /// starting deck for this hero, before rewards have touched it.
    ///
    /// Read-only on purpose. Handing out the list itself would let a caller add a card straight into
    /// the prefab asset, which persists to disk after Play Mode exits.
    /// </summary>
    public IReadOnlyList<CardData> AuthoredDeck => deck;

    /// <summary>
    /// Replaces the authored deck and rebuilds the piles from it.
    ///
    /// For spawned enemies: BuildDeck has already run in Awake by the time anything can reach a
    /// freshly instantiated character, so handing it a new list means building the piles again
    /// rather than editing the field and hoping.
    /// </summary>
    public void SetDeck(IEnumerable<CardData> cards)
    {
        deck.Clear();

        if (cards != null) { deck.AddRange(cards); }

        BuildDeck();
    }

    /// <summary>
    /// Adds one card straight into this character's draw pile mid-battle - the general-purpose grant,
    /// for anything that hands over a card without the player needing to see it play out immediately
    /// (a relic, a card-that-adds-a-card). Not routed through `deck`: `deck` is the authored starting
    /// list BuildDeck rebuilds piles from wholesale, and rebuilding here would discard whatever this
    /// character already drew, played or discarded this battle.
    ///
    /// See AddCardToHand for the sibling that puts the card in hand instead - use that one wherever
    /// the point is that the card is immediately visible and playable, a loot pickup being the case
    /// that motivated it. The run-persistence half of a reward is separate either way - see
    /// BattleManager.RecordRunCard, which writes into the PartyMember record instead of here.
    /// </summary>
    public void AddCard(CardData data)
    {
        if (data == null) { return; }

        Card card = new Card(data);

        // An Innate card sitting in the draw pile is a contradiction, and quietly a bug: it would be
        // drawn like anything else and then, on Discard, skip the discard pile - Discard routes Innate
        // cards nowhere because it assumes RestoreInnateCards will put them back, which only happens
        // for cards BuildDeck already knows about. BuildDeck avoids this the same way, by routing
        // Innate straight to hand instead of the draw pile.
        if (card.HasKeyword(CardKeywordType.Innate)) { PutInHand(card); return; }

        drawPile.Add(card);
        Shuffle(drawPile);
    }

    /// <summary>
    /// Adds one card straight into this character's hand, playable this turn - a reward picked up off
    /// the ground is the caller. Distinct from AddCard on purpose: use this where the point is that
    /// the card is immediately visible and usable, and AddCard everywhere else.
    ///
    /// Deliberately allowed to overshoot HandSize when the hand is already full. The overflow lasts
    /// one turn at most - TurnStart discards the hand wholesale and re-deals to HandSize - and
    /// shunting the reward into the draw pile instead would make the one card the player just chose
    /// the one card they cannot see.
    /// </summary>
    public void AddCardToHand(CardData data)
    {
        if (data == null) { return; }

        PutInHand(new Card(data));
    }

    /// The bookkeeping both AddCard (for an Innate card) and AddCardToHand share: the innateCards
    /// entry is what RestoreInnateCards walks each turn to keep it in hand - the same two-list write
    /// BuildDeck does for an authored innate card - and CardDrawn is the event DrawCard already
    /// raises, so ActiveHandViewer builds a viewer through the one path it has. Nothing here knows
    /// whether this character's hand is the one currently on screen.
    private void PutInHand(Card card)
    {
        if (card.HasKeyword(CardKeywordType.Innate)) { innateCards.Add(card); }

        hand.Add(card);

        CardDrawn?.Invoke(this, card);
    }

    private void BuildDeck()
    {
        drawPile.Clear();
        hand.Clear();
        discardPile.Clear();
        innateCards.Clear();

        foreach (CardData data in deck)
        {
            if (data == null) { continue; }

            if (!data.CanBeUsedBy(this))
            {
                Debug.LogWarning($"{name}: {data.cardName} needs {data.requiredClass} but this "
                                 + $"character is {characterClass} - skipped");
                continue;
            }

            Card card = new Card(data);

            // Innate cards skip the draw pile entirely - they start in hand and RestoreInnateCards
            // keeps them there for the rest of the battle.
            if (card.HasKeyword(CardKeywordType.Innate))
            {
                innateCards.Add(card);
                hand.Add(card);
            }
            else
            {
                drawPile.Add(card);
            }
        }

        Shuffle(drawPile);
    }

    private void ReshuffleDiscardIntoDrawPile()
    {
        if (discardPile.Count == 0) { return; }

        drawPile.AddRange(discardPile);
        discardPile.Clear();
        Shuffle(drawPile);
    }

    private static void Shuffle(List<Card> cards)
    {
        for (int i = cards.Count - 1; i > 0; i--)
        {
            // Qualified: `using System` is in scope for Action, and System.Random would shadow this.
            int j = UnityEngine.Random.Range(0, i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
    }

    /// Keeps the Inspector's Health readout honest outside Play Mode. Without this it would show
    /// whatever was last serialized - 0 on a character that has never been played - which reads as a
    /// dead unit sitting in the scene. Guarded on isPlaying so it never fights Awake or combat.
    private void OnValidate()
    {
        if (!Application.isPlaying && Health != maxHealth) { Health = maxHealth; }
    }

    private void Awake()
    {
        Health = maxHealth;
        Energy = maxEnergy;
        BuildDeck();

        // Cached rather than looked up on every play - null on a body with no CharacterAnimator
        // component, e.g. Totem, which is a legal answer GameAction.Perform already handles.
        Animation = GetComponent<CharacterAnimator>();
    }

    private void Start()
    {
        // GridManager builds the grid in Awake, which always runs before any Start, so tiles exist here.
        PlaceOnStartTile();
    }

    /// <summary>
    /// Drops this character onto a cell immediately, rather than waiting for its own Start.
    ///
    /// Spawned enemies need this: an enemy instantiated during the battle's own Start would not run
    /// its Start until the end of the frame, so its Tile would still be null when the first turn
    /// asked it what it intended to do - and a character with no tile can only Wait.
    /// </summary>
    public void PlaceOnGrid(Vector2Int cell)
    {
        startCoordinates = cell;
        PlaceOnStartTile();
    }

    /// <summary>
    /// Hands the whole job to GridManager: claiming the tile and standing the body on it are one
    /// operation, and where a cell sits in world space is the board's business, not a character's.
    ///
    /// Public because a character sitting in the scene runs this from its own Start, and nothing
    /// orders that against BattleManager.Start, which is what builds the board. Whichever loses the
    /// race finds no tile and silently does nothing, so BattleManager calls this again once the grid
    /// exists. Safe to call twice: MoveTo onto the tile a character is already standing on clears and
    /// re-sets the same occupant.
    /// </summary>
    public void PlaceOnStartTile()
    {
        if (GridManager.Instance != null) { GridManager.Instance.PlaceCharacter(this, startCoordinates); }
    }

    public void DiscardHand()
    {
        while(hand.Count > 0)
        {
            Debug.Log(hand.Count);
            Discard(hand[0]);
        }
    }
}
