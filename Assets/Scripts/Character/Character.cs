using System;
using System.Collections.Generic;
using System.Linq;
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

    [Tooltip("Which spawn row(s) this character is eligible for - see BattleRole. None (the default) "
             + "means unconstrained, placed only after every role-constrained character has a cell.")]
    [SerializeField] private BattleRole battleRole = BattleRole.None;

    [Tooltip("Brandon's Power Level if set, else the sheet's Estimated Power Level - synced by "
             + "EnemySheetImporter, never hand-edited here. 0 means unscored: EncounterRoller can never "
             + "draw it, so a body left at 0 silently never appears in a random encounter.")]
    [SerializeField] private float powerLevel;

    [Tooltip("Drives EncounterRoller's boss budgets. Synced from the prefab's own folder "
             + "(Assets/Prefabs/Bosses) - never hand-edited here.")]
    [SerializeField] private bool isBoss;

    [Tooltip("Which cards this character may hold. Cards not matching are skipped when the deck is "
             + "built.")]
    [SingleClass]
    [SerializeField] private CharacterClass characterClass;

    [Tooltip("Shown in player-facing UI instead of this GameObject's raw name (which is either the "
             + "prefab's authoring name, e.g. \"PlayerKnight\", or has a runtime suffix like \"(Clone)\" "
             + "or a spawn coordinate appended). Leave blank to fall back to gameObject.name.")]
    [SerializeField] private string displayName;

    [Tooltip("Shown in the battle portrait row and the character-select screen. Identity there is a "
             + "single framed image, not the sprite rig standing on the board, so this is separate art.")]
    [SerializeField] private Sprite portrait;

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

    /// Equipment this character has picked up this run - permanent for the rest of it, never expiring,
    /// never pruned. Same reason ownStatusEffects is separate from auras: an EquipmentModifier's
    /// Project half is pulled fresh into ActiveStatuses every call exactly like a Totem's aura, and its
    /// Apply half is what RefreshCardModifiers re-runs over every held Card. See Character.Equip.
    private readonly List<EquipmentData> equipment = new();

    public IReadOnlyList<EquipmentData> Equipment => equipment;

    /// Current health. Serialized only so the live value is watchable in the Inspector during Play
    /// Mode; [ReadOnlyField] greys it out so nobody can type into it. Awake overwrites whatever was
    /// saved, so the stored value is a readout, never authoring data - edit Max Health instead.
    [field: SerializeField, ReadOnlyField]
    public int Health { get; private set; }

    public int MaxHealth => maxHealth;

    /// Each character has their own pool; playing a card spends the acting character's energy.
    public int Energy { get; private set; }

    public int MaxEnergy => maxEnergy;

    public PlayableCharacter Affiliation => playableCharacter;

    public BattleRole BattleRole => battleRole;

    public float PowerLevel => powerLevel;

    public bool IsBoss => isBoss;

    /// True only for the party the player clicks to control directly. Ally, EnemyAllied and Neutral
    /// are all AI-resolved, same as Enemy - see BattleManager.LivingEnemies.
    public bool IsPlayerControlled => playableCharacter == PlayableCharacter.AllyPlayable;

    public CharacterClass Class => characterClass;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    public Sprite Portrait => portrait;

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

    /// Whether this character rolled a totem hunt for the turn currently in progress. Per-copy runtime
    /// state alongside targetingCursor, and rolled once - see RollTotemHunt - rather than inside
    /// CurrentPriority itself: BattleManager.LateUpdate re-asks CurrentPriority every frame the board
    /// changes and EnemyResolve asks it again per action point, so rolling it there would flicker the
    /// intent icon and make TryFindMove's tile scan incoherent, exactly the failure TargetSelector.TryPick
    /// documents for Random.
    private bool huntingTotemThisTurn;

    /// Who this character is going after right now. Named by the pattern; the whole point is that it
    /// names the attack's victim *and* the direction a move heads in, so a Move in the sequence walks
    /// toward whoever the next attack means to hit. See TargetSelector.
    ///
    /// A totem hunt rolled for this turn overrides the sequence outright rather than being spliced
    /// into it - TargetPriority.Totem only ever reorders (see TargetSelector.Rank), so a hunt with no
    /// totem in reach still falls back to the nearest legal candidate instead of a wasted turn.
    public TargetPriority CurrentPriority =>
        huntingTotemThisTurn ? TargetPriority.Totem
        : targetingPattern != null ? targetingPattern.At(targetingCursor) : TargetPriority.Weakest;

    /// Spent by attacks only, and only once one has actually resolved - see BattleManager.Execute. A
    /// refused or fizzled swing leaves the sequence exactly where it was, so an owed "Attack: Closest"
    /// stays owed.
    public void AdvanceTargetingCursor() => targetingCursor++;

    /// Whether TargetSelector should drop totems from this character's candidate list outright,
    /// rather than merely ranking them - see TargetingPattern.IgnoreTotems.
    public bool IgnoresTotems => targetingPattern != null && targetingPattern.IgnoreTotems;

    /// Rolls whether this character hunts a totem for the round about to be resolved. Called once,
    /// from BattleManager.TurnStart, before any Decide for this round - see huntingTotemThisTurn.
    public void RollTotemHunt()
    {
        int chance = targetingPattern != null ? targetingPattern.TotemChancePercent : 0;

        huntingTotemThisTurn = chance > 0 && UnityEngine.Random.Range(0, 100) < chance;
    }

    private Intent committedIntent;

    /// <summary>
    /// What this enemy would do if its turn came right now - what the overhead icon shows.
    ///
    /// The card is a promise for the whole turn (see LockedCard); the tile and victim are not -
    /// BattleManager recomputes those whenever the board changes (see BattleManager.LateUpdate) by
    /// re-aiming the same locked card, and re-derives the whole thing outright the moment the enemy
    /// actually acts and the lock is consumed or lapses. Moving a hero out of an archer's reach turns
    /// its Attack back into a Move only once the locked card genuinely cannot be aimed at anyone -
    /// stepping back into range hands the same card back rather than rolling a new one.
    /// </summary>
    public Intent CommittedIntent
    {
        get => committedIntent;
        set { committedIntent = value; IntentChanged?.Invoke(this); }
    }

    /// <summary>
    /// The card this enemy committed to for the whole turn, and the kind it was committed as -
    /// BattleManager.Decide re-aims this same card every time the board changes rather than picking a
    /// new one, which is what makes the intent icon's damage number a promise instead of a forecast
    /// that can flicker as the player moves. Null between rounds and whenever nothing was committed
    /// (a Wait turn).
    ///
    /// Distinct from LockedAim: that one is Dodge freezing a *whole* Intent, tile included, for a
    /// single action point, and it still overrides this outright when set - see
    /// BattleManager.LockAimsOn and EnemyResolve. This one only pins the card; the aim stays live.
    /// </summary>
    public Card LockedCard { get; private set; }

    public IntentKind LockedKind { get; private set; }

    /// Commits to `intent`'s card and kind for the rest of the turn, or clears the lock if it is a
    /// Wait. Called once from BattleManager.TurnStart with a fresh Decide; never called with the
    /// result of Reaim, or a re-aim would silently become a new lock.
    public void LockIntent(Intent intent)
    {
        if (intent.IsWait)
        {
            ClearIntentLock();
            return;
        }

        LockedCard = intent.card;
        LockedKind = intent.kind;
    }

    public void ClearIntentLock()
    {
        LockedCard = null;
        LockedKind = IntentKind.Wait;
    }

    /// Whether `card` is still one of this character's hand copies - what BattleManager.Decide checks
    /// before trusting LockedCard, since the card may have been discarded (played, or a hand reshuffle)
    /// since it was locked. A plain loop rather than Hand.Contains-via-LINQ, matching the rest of this
    /// class's per-frame-safe style.
    public bool Holds(Card card)
    {
        if (card == null) { return false; }

        foreach (Card held in Hand)
        {
            if (held == card) { return true; }
        }

        return false;
    }

    /// <summary>
    /// An Attack this enemy is now locked onto, overriding the next Decide call in EnemyResolve -
    /// how Dodge makes an enemy keep swinging at the tile its target just left instead of re-aiming
    /// at wherever they moved to. Intent.Wait() (the default) means nothing is locked and EnemyResolve
    /// decides fresh, exactly as before Dodge could set this.
    ///
    /// Deliberately plain scratch state rather than a Status: it answers no question about this
    /// character, it is written by BattleManager.LockAimsOn and consumed by EnemyResolve, and it is
    /// cleared every round - see BattleManager.TurnStart and EnemyResolve.
    ///
    /// Not routed through the CommittedIntent setter - locking an aim must not raise IntentChanged,
    /// or the overhead icon over this enemy's own head would repaint from an intent decided about the
    /// dodger, not about itself.
    /// </summary>
    public Intent LockedAim { get; set; }

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

    /// This body's root SortingGroup, or null on a prefab without one. Written on every MoveTo so a
    /// character standing further back never draws over one standing in front of it - see
    /// GridManager.CellDepth.
    private UnityEngine.Rendering.SortingGroup sortingGroup;

    /// This character's own animation vocabulary, or null on a body with no CharacterAnimator - the
    /// Totem, or any prefab nobody has wired up yet. GameAction.Perform already no-ops on null, so
    /// nothing downstream needs its own guard for this.
    public CharacterAnimator Animation { get; private set; }

    /// The cards currently held. ActiveHandViewer builds the viewers for whichever character is active.
    public IReadOnlyList<Card> Hand => hand;

    /// What is left to draw, read-only for the same reason AuthoredDeck is - a caller must not be able
    /// to add a card by reaching into the list. CardPileHud reads this for the deck icon's count and
    /// browse grid; the order is the real shuffle order, so anything showing it to the player should
    /// sort it first rather than reveal the draw sequence.
    public IReadOnlyList<Card> DrawPile => drawPile;

    /// Everything discarded so far this battle. Same read-only bargain as DrawPile.
    public IReadOnlyList<Card> DiscardPile => discardPile;

    /// Raised when a card lands in this hand. ActiveHandViewer listens so the row on screen follows the
    /// active character - drawing itself is none of its business.
    public event Action<Character, Card> CardDrawn;

    /// Raised when a card leaves this hand for the discard pile, whether played or discarded wholesale
    /// at turn start. ActiveHandViewer listens and removes the matching viewer - discarding itself is
    /// none of its business, same as CardDrawn.
    public event Action<Character, Card> CardDiscarded;

    /// Raised when the discard pile has just been folded back into the draw pile, with how many cards
    /// moved. The piles are otherwise silent about themselves - this is the one moment worth seeing.
    public event Action<Character, int> PilesReshuffled;

    /// <summary>
    /// Raised after RefreshCardModifiers re-tunes every held card - equipping mid-battle can change a
    /// card already sitting in hand (a relic granting +1 range, say), and a viewer already showing that
    /// card has no other way to learn its cost or footprint just changed. Carries only the subject, same
    /// contract as StatsChanged: a listener re-reads whichever Card it already holds a reference to
    /// rather than this event describing what changed.
    /// </summary>
    public event Action<Character> CardsModified;

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
        RaiseStatsChanged();
    }

    /// <summary>
    /// Adds energy on top of whatever the character is currently holding - EnergyEffect's action half.
    ///
    /// Deliberately uncapped by maxEnergy: the whole point of a card that grants energy at a cost is
    /// spending past your normal pool for one turn, so clamping to maxEnergy here would make the card
    /// do nothing on a turn it has not already been partly spent.
    /// </summary>
    public void GainEnergy(int amount)
    {
        if (amount <= 0) { return; }

        Energy += amount;
        RaiseStatsChanged();
    }

    public void ResetEnergy()
    {
        Energy = maxEnergy + BonusEnergy;
        RaiseStatsChanged();
    }

    /// <summary>
    /// How much this character's active statuses and equipment add to its energy refill each turn -
    /// a ring's "+1 energy each turn". Summed exactly like AppliedPotency: two rings each granting a
    /// point should both count.
    /// </summary>
    public int BonusEnergy
    {
        get
        {
            int total = 0;

            foreach (Status status in ActiveStatuses()) { total += status.BonusEnergy; }

            return total;
        }
    }

    /// <summary>
    /// The hand-size counterpart to BonusEnergy - a ring's "+1 card drawn each turn". Read by
    /// BattleManager when it refills a hand up to HandSize; kept off Character's own hand-size field
    /// for the same reason HandSize itself lives on BattleManager/LevelData rather than here.
    /// </summary>
    public int BonusHandSize
    {
        get
        {
            int total = 0;

            foreach (Status status in ActiveStatuses()) { total += status.BonusHandSize; }

            return total;
        }
    }

    /// <summary>
    /// Runs a hit through every OnTakeDamage hook and returns what survives, without touching Health -
    /// the incoming counterpart to ComputeOutgoingDamage.
    ///
    /// shouldConsume is the whole difference between being hit and being looked at. True spends real
    /// charges and belongs to the one place actually landing a blow - TakeDamage below. False leaves
    /// Block, Shield, Parry and Dodge untouched, for a damage preview or anything else scoring a hit it
    /// has not thrown. Passing true to display a number would strip the target's defenses without an
    /// attack ever happening.
    ///
    /// `info` is the accumulator, not a per-status value: each hook takes the hit as the previous one
    /// left it and returns the next, so mitigations compose. A hit of 10 against Block 3 then Shield 4
    /// goes 10 -> 7 -> 3. Rebuilding it inside the loop would hand every status the original 10 and
    /// only the last one's result would survive.
    /// </summary>
    public DamageInfo ComputeIncomingDamage(int amount, Character attacker, bool shouldConsume)
    {
        DamageInfo info = new(attacker, this, amount, consumeCharges: shouldConsume);

        foreach (Status status in ActiveStatuses())
        {
            info = status.OnTakeDamage(info);

            // Parry cancelled the hit outright - there is nothing left for anything else to reduce.
            if (info.negated) { break; }
        }

        if (shouldConsume) { PruneExpired(); }

        return info;
    }

    /// <summary>
    /// The single choke point all ordinary damage passes through - GridTile.DealDamage forwards here.
    ///
    /// Every defense is a status, so this runs the OnTakeDamage hooks (via ComputeIncomingDamage) and
    /// subtracts whatever survives them. It does not know that Parry, Block or Shield exist, and adding
    /// a fourth mitigation type needs no change here at all.
    ///
    /// attacker is who to reflect a parried hit back at - null (from sources with no attacker) just
    /// means a parried hit vanishes instead of reflecting.
    ///
    /// `bounces` is only ever non-zero on a reflected hit. See MaxParryBounces.
    /// </summary>
    public void TakeDamage(int amount, Character attacker = null, int bounces = 0)
    {
        if (amount <= 0) { return; }

        DamageInfo info = ComputeIncomingDamage(amount, attacker, shouldConsume: true);

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

        // The attacker's on-hit riders - Poison Blade, and whatever shield-on-attack the Knight gets
        // next. Here, not in DamageAction, because this is the one place that knows the hit actually
        // connected and what survived the target's mitigation. Only !negated: Shield or Block eating
        // the hit down to zero still counts as connecting, Dodge and Parry sidestepping it does not.
        // Reflect above has already run by this point, so a parried hit fires the *parrier's* riders
        // on the counter-blow rather than the original attacker's - the right reading of "on hit".
        if (!info.negated && attacker != null) { attacker.NotifyDamageDealt(info); }

        CheckDeath();
    }

    /// <summary>
    /// Runs this character's on-hit riders for a blow it just landed. Called on the *attacker* from
    /// the victim's TakeDamage - a cross-instance private call, the same shape Reflect already uses to
    /// reach back into attacker.
    /// </summary>
    private void NotifyDamageDealt(DamageInfo info)
    {
        foreach (Status status in ActiveStatuses()) { status.OnDamageDealt(info); }

        PruneExpired();
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
    /// building the list in that order is most of the enforcement - the rest is Status.Order, applied
    /// below as a stable sort. Two statuses with the same Order (the default, and the overwhelming
    /// majority) keep resolving FIFO - auras first, then the character's own in the order they were
    /// gained - exactly as if Order did not exist. See Status.
    ///
    /// A fresh list every call rather than a reused buffer: a Poison tick can kill the carrier, and a
    /// Died handler running mid-iteration could otherwise clobber the buffer being walked.
    /// </summary>
    public List<Status> ActiveStatuses()
    {
        List<Status> aurasThenOwn = new();

        // Equipment first - permanent for the run, so it resolves ahead of both a totem's aura and the
        // character's own carried statuses. That ordering is what lets a flat equipment bonus land
        // before a multiplier: +2 then x2, not x2 then +2. Pulled fresh every call for the same reason
        // an aura is - see EquipmentModifier.Project.
        foreach (EquipmentData item in equipment)
        {
            if (item == null) { continue; }

            foreach (EquipmentModifier modifier in item.modifiers)
            {
                if (modifier != null) { modifier.Project(this, aurasThenOwn); }
            }
        }

        // Then auras - projected by whichever totems reach this character's tile right now.
        Totem.CollectAuras(this, aurasThenOwn);

        // Then the character's own, in the order they were applied.
        aurasThenOwn.AddRange(ownStatusEffects);

        // Stable, so ties (the default Order of 0) do not disturb the equip -> aura -> own sequence
        // built above - only a status that overrides Order moves out of that pack. OrderBy, not
        // List.Sort, because List.Sort is not guaranteed stable.
        return aurasThenOwn.OrderBy(status => status.Order).ToList();
    }

    /// <summary>
    /// The item currently held in `slot`, or null if it is empty - what a reward panel asks before
    /// offering a swap, and what an equipment UI reads to show a slot as filled or free.
    ///
    /// Always null for EquipmentSlot.Ring: unlimited slots have no single occupant to name. Use
    /// Equipment and filter by slot to list every ring instead.
    /// </summary>
    public EquipmentData EquippedIn(EquipmentSlot slot)
    {
        if (EquipmentSlots.IsUnlimited(slot)) { return null; }

        foreach (EquipmentData item in equipment)
        {
            if (item != null && item.slot == slot) { return item; }
        }

        return null;
    }

    /// <summary>
    /// Equips `item` for the rest of the run: its permanent statuses start showing up in
    /// ActiveStatuses immediately, and every card this character already holds is re-tuned to account
    /// for it - see RefreshCardModifiers. Duplicates stack in an unlimited slot (Ring): equipping a
    /// second copy runs its Project/Apply a second time, same as two totems both granting Strength give
    /// two separate auras rather than one doubled entry. Every other slot holds exactly one item -
    /// equipping into an occupied one unequips the current occupant first, same as Unequip, before the
    /// new item takes its place.
    ///
    /// Returns whatever this displaced, or null if the slot was empty (or unlimited). Callers are free
    /// to ignore it - BattleManager.RecordRunEquipment enforces the same one-occupant-per-slot rule
    /// independently when it writes a reward into the run record, so the live equip and the record stay
    /// in sync without LootManager having to thread this value through by hand.
    /// </summary>
    public EquipmentData Equip(EquipmentData item)
    {
        if (item == null) { return null; }

        EquipmentData displaced = null;

        if (!EquipmentSlots.IsUnlimited(item.slot))
        {
            displaced = EquippedIn(item.slot);

            if (displaced != null) { equipment.Remove(displaced); }
        }

        equipment.Add(item);
        RefreshCardModifiers();
        RaiseStatsChanged();

        return displaced;
    }

    /// <summary>
    /// Takes one copy of `item` back off, undoing exactly what Equip did.
    ///
    /// Removes a single copy rather than every match, mirroring Equip's rule that duplicates stack -
    /// two copies of the same item are two separate contributions, so taking one back should leave the
    /// other standing.
    ///
    /// Nothing else is needed, for two reasons worth stating because both look like omissions:
    /// ActiveStatuses rebuilds from `equipment` on every single call and EquipmentModifier.Project is
    /// pulled fresh rather than cached, so a projected status simply stops existing on the next query -
    /// it never entered ownStatusEffects and there is nothing to prune. And RefreshCardModifiers is
    /// reset-then-reapply (ResetToAuthored first), so the removed item's Apply half is undone by
    /// construction rather than by any inverse operation.
    ///
    /// Lives here rather than on a caller because RaiseStatsChanged is private: the HUD and the enemy
    /// intent previews both go stale without it.
    /// </summary>
    public void Unequip(EquipmentData item)
    {
        if (item == null) { return; }

        if (!equipment.Remove(item)) { return; }

        RefreshCardModifiers();
        RaiseStatsChanged();
    }

    /// <summary>
    /// Bulk form for SpawnParty: replaces this character's equipment wholesale from its PartyMember
    /// record. Called before SetDeck, so BuildDeck constructs every card already tuned rather than
    /// building once and re-tuning immediately after.
    ///
    /// Deliberately does NOT refresh cards or raise StatsChanged, unlike Equip/Unequip - it gets away
    /// with that only because SpawnParty calls it before SetDeck, so BuildDeck tunes every card on
    /// construction. Calling it mid-battle would leave stale card tuning and a stale HUD.
    /// </summary>
    public void SetEquipment(IEnumerable<EquipmentData> items)
    {
        equipment.Clear();

        if (items != null) { equipment.AddRange(items); }
    }

    /// <summary>
    /// Raises this character's health ceiling by a flat amount, and its current health along with it -
    /// SummonHealthStatus's write path for "summons enter play with +N max health". Unlike SetHealth,
    /// which only ever clamps *to* maxHealth, this is the one place maxHealth itself changes after
    /// Awake has already set Health from the old value - raising both together is what keeps a freshly
    /// summoned totem at full health under its new ceiling rather than clamped back down to what it had
    /// a moment ago.
    /// </summary>
    public void AddMaxHealth(int bonus)
    {
        if (bonus == 0) { return; }

        maxHealth = Mathf.Max(1, maxHealth + bonus);
        Health = Mathf.Clamp(Health + bonus, 1, maxHealth);
        RaiseStatsChanged();
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

    /// <summary>
    /// How many turns of `type` this character is carrying in total - every instance's counter added up,
    /// skipping anything projected onto it.
    ///
    /// What a status chip badges. For Weaken and Vulnerable that is a real sum over several statuses: a
    /// Vulnerable 4 for three turns under a Vulnerable 3 for three turns reads 6, because only the one
    /// in force ticks (see WeakenStatus.OnTurnEnd) and the other is waiting its turn rather than being
    /// spent. For every other type there is at most one carried instance, so this is just its counter -
    /// exactly what the badge showed before any of this existed.
    ///
    /// Auras are left out because they have no clock to contribute; a character whose only source is a
    /// totem badges nothing at all. StatusStacks, which does include them, stays what the rules and the
    /// tooltip sentence read.
    /// </summary>
    public int CarriedStatusStacks(StatusType type)
    {
        int total = 0;

        foreach (StatusEffect status in ownStatusEffects)
        {
            if (status.type == type) { total += status.stacks; }
        }

        return total;
    }

    /// <summary>
    /// The status of this type that is actually in force, or null. For anything that needs more than a
    /// stack count - a UI asking Block for both of its numbers, or asking a Weaken how deep it cuts.
    ///
    /// The *strongest*, not the first, because a character can carry several Weakens or Vulnerables at
    /// once (same-size ones merge, different-size ones do not - see StatusEffect.MergesWith) plus
    /// whatever a totem projects, and
    /// only the biggest applies. Ties go to a carried status over a projected one, so an enemy standing
    /// in a Sap Totem's aura who is also carrying an equally deep Weaken shows the carried one's clock
    /// rather than the aura's blank. Types with no size at all (Poison, Frozen, Shield) all tie at
    /// Amount 0, so that same tie-break hands back the carried one there too - which is exactly the
    /// first-match behaviour this replaced, since a merging type has at most one carried instance.
    /// </summary>
    public Status FindStatus(StatusType type)
    {
        Status best = null;

        foreach (Status status in ActiveStatuses())
        {
            if (status.type != type) { continue; }

            if (best == null
                || status.Amount > best.Amount
                || (status.Amount == best.Amount && best.IsProjected && !status.IsProjected))
            {
                best = status;
            }
        }

        return best;
    }

    /// <summary>
    /// How much this character's own statuses and equipment add to the size of a `type` it is about to
    /// apply to somebody else - what StatusAction hands to StatusEffect.Create as its amountBonus, and
    /// what Totem.Project asks its owner before building an aura.
    ///
    /// Summed rather than ranked, unlike FindStatus: two rings that each deepen Weaken should both
    /// count, the same way two damage relics both raise a swing.
    /// </summary>
    public int AppliedPotency(StatusType type)
    {
        int total = 0;

        foreach (Status status in ActiveStatuses()) { total += status.AppliedPotency(type); }

        return total;
    }

    /// <summary>
    /// The same question asked of this character's own carried statuses only, skipping equipment and
    /// auras - what Totem.Project uses.
    ///
    /// It cannot use AppliedPotency above, and the reason is a cycle rather than a preference:
    /// ActiveStatuses calls Totem.CollectAuras, so a totem asking its owner a question that walks
    /// ActiveStatuses re-enters Project and never comes back. A totem covering itself makes that
    /// immediate - Rampart affects allies at range 0, so it is inside its own aura. Reading the carried
    /// list directly is also exactly right rather than merely safe: a totem inherits its summoner's
    /// potency as a real carried status at summon time (see AppliedPotencyStatus.OnSummoned), so there
    /// is nothing projected onto a totem for this to miss.
    /// </summary>
    public int CarriedAppliedPotency(StatusType type)
    {
        int total = 0;

        foreach (StatusEffect status in ownStatusEffects) { total += status.AppliedPotency(type); }

        return total;
    }

    /// Applies a status by type, stacking onto one already present. The convenience form for every
    /// status the enum alone can describe - GridTile.ApplyStatus and friends.
    public void AddStatus(StatusType type, int stacks)
    {
        AddStatus(StatusEffect.Create(type, stacks));
    }

    /// <summary>
    /// Applies an already-built status, merging into one of the same type if it is already there.
    ///
    /// The object form exists for Taunt, which carries a reference to whoever applied it and so cannot
    /// come out of StatusEffect.Create. How a top-up combines is the status's own business - see
    /// StatusEffect.Merge.
    ///
    /// Takes a StatusEffect, not a Status: an Aura is owned by its totem and rebuilt on every query,
    /// so there is nothing here for one to be added to. The type signature is what says so.
    ///
    /// Runs the OnGainStatus pipeline before merging or appending, so a GainMultiplierStatus aura can
    /// scale what is about to land - the same shape GainShield uses for OnGainShield, run one call
    /// earlier so it covers every application path (StatusAction, GainBlock/GainParry/Taunt,
    /// PoisonBladeStatus, SummonAction and GainShield's own tail call into this method), not just
    /// shield. A shield gain now runs two pipelines in a row - OnGainShield first, then this one - so a
    /// Shield-subject GainMultiplier would compound with DoubleShieldStatus rather than replace it.
    /// </summary>
    public void AddStatus(StatusEffect incoming)
    {
        if (incoming == null || incoming.type == StatusType.None || incoming.stacks <= 0) { return; }

        StatusGainInfo info = new(this, incoming.type, incoming.stacks);
        foreach (Status status in ActiveStatuses()) { info = status.OnGainStatus(info); }
        if (info.stacks <= 0) { return; }
        incoming.stacks = info.stacks;

        // Same type is not enough on its own: Weaken and Vulnerable carry a size as well as a clock, and
        // only fold into a neighbour of the *same* size - so this keeps looking rather than stopping at
        // the first same-type entry, and appends when nothing matched. See StatusEffect.MergesWith.
        foreach (StatusEffect existing in ownStatusEffects)
        {
            if (existing.type != incoming.type || !existing.MergesWith(incoming)) { continue; }

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
    /// The end of this character's own phase: statuses that do something on a passing turn do it, and
    /// the ones that run out drop.
    ///
    /// Called by BattleManager at the end of this character's own phase - PlayerActing for
    /// player-controlled characters, EnemyResolve for enemies - never at the shared TurnStart. See
    /// BattleManager.TickStatuses(bool) for why the timing matters.
    ///
    /// There is deliberately no ageing loop here. A status that expires with time spends its own
    /// counter inside its OnTurnEnd - Poison bites and then decays, Frozen decrements, Weaken zeroes
    /// itself - so this method does not know that durations exist, which is the same reason TakeDamage
    /// does not know that Shield does. Adding a new timed status needs no change to this class.
    ///
    /// An aura ticking itself here is harmless: Totem rebuilds them on every query, so a counter it
    /// spends belongs to a throwaway.
    /// </summary>
    public void OnTurnEnd()
    {
        foreach (Status status in ActiveStatuses()) { status.OnTurnEnd(this); }

        PruneExpired();
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
    /// Strips every carried instance of `type` outright and returns how many stacks were removed -
    /// Purge and Cleansing Light both need the number to hand to whatever they do with it next
    /// (afflict an enemy, heal the target). Only carried statuses, the same reach PruneExpired has:
    /// an aura projected onto this character from a totem is not something removing it here could
    /// touch, and it would come back on the very next query anyway.
    /// </summary>
    public int RemoveStatus(StatusType type)
    {
        int removed = 0;

        for (int i = ownStatusEffects.Count - 1; i >= 0; i--)
        {
            if (ownStatusEffects[i].type != type) { continue; }

            removed += ownStatusEffects[i].stacks;
            ownStatusEffects.RemoveAt(i);
        }

        if (removed > 0) { RaiseStatsChanged(); }

        return removed;
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

        AddStatus(StatusType.Shield, info.amount);
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

            RefreshSortingDepth();
        }
    }


    /// <summary>
    /// Re-sorts this body against the board it is standing on.
    ///
    /// Here rather than in GridManager.MoveCharacter because MoveTo is the one place occupancy
    /// actually changes - PlaceCharacter, MoveCharacter and SwapCharacters all route through it, and
    /// a summon arriving mid-turn does too. Anywhere else and one of those four would be the path
    /// that forgot.
    ///
    /// Note this deliberately runs at the *start* of a move rather than when its tween lands: a
    /// character walking toward the camera should pass in front of what it is passing, and the tween
    /// is where that reads.
    /// </summary>
    private void RefreshSortingDepth()
    {
        if (sortingGroup == null || Tile == null || GridManager.Instance == null) { return; }

        sortingGroup.sortingOrder = GridManager.Instance.CellDepth(Tile.Coordinates);
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
    /// Discards a card that was just *played*, which is the only kind of discard Rebound reacts to.
    ///
    /// Rebound deliberately does not live in Discard itself. Discard is also how a hand is dumped at
    /// turn end and how DiscardAction spends a card, and a keyword meaning "you keep this after playing
    /// it" must not fire for either - a card you were forced to discard should stay discarded. It also
    /// hung the game: DiscardHand loops until the hand is empty, and a card that re-added itself inside
    /// Discard made that loop infinite.
    ///
    /// The return is raised as its own CardDrawn rather than folded into the CardDiscarded above,
    /// because the card genuinely does leave hand and arrive again - ActiveHandViewer removes the
    /// viewer on the first event and builds a fresh one on the second, which is the flight out and
    /// back. hand.Add directly rather than PutInHand: that helper also registers the card in
    /// innateCards, which would quietly make any Rebound card innate too.
    /// </summary>
    public void DiscardPlayed(Card card)
    {
        // Read before the discard, not after. Discard is a no-op for a card that was never in hand, so
        // asking afterwards cannot tell "just played" from "was never here" - and the second case would
        // conjure the card *into* hand rather than return it to it.
        bool played = card != null && hand.Contains(card);

        Discard(card);

        if (!played || !card.HasKeyword(CardKeywordType.Rebound)) { return; }

        discardPile.Remove(card);
        hand.Add(card);

        CardDrawn?.Invoke(this, card);
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

        Card card = NewCard(data);

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

        PutInHand(NewCard(data));
    }

    /// <summary>
    /// The one place a CardData turns into this character's own Card copy - every construction site
    /// (AddCard, AddCardToHand, BuildDeck) routes through here so equipped CardTuningModifiers are never
    /// something a caller has to remember to apply. Mirrors CardTuningModifier.Apply's own filter check
    /// per modifier, so a card matching nobody's filter is built exactly as new Card(data) always was.
    /// </summary>
    private Card NewCard(CardData data)
    {
        Card card = new(data);
        ApplyCardModifiers(card, data);
        return card;
    }

    /// <summary>
    /// Runs every equipped CardTuningModifier's Apply against one card - the half of NewCard that
    /// RefreshCardModifiers also needs, since re-tuning an existing card is "reset it, then apply" not
    /// "construct it, then apply".
    /// </summary>
    private void ApplyCardModifiers(Card card, CardData data)
    {
        foreach (EquipmentData item in equipment)
        {
            if (item == null) { continue; }

            foreach (EquipmentModifier modifier in item.modifiers)
            {
                if (modifier != null) { modifier.Apply(card, data); }
            }
        }
    }

    /// <summary>
    /// Re-tunes every card this character currently holds - hand, draw pile, discard pile and the
    /// innate subset - to account for its current equipment. Reset-then-reapply, not incremental: each
    /// card is first put back to exactly what its CardData authored (Card.ResetToAuthored), then every
    /// equipped CardTuningModifier runs again from scratch. That is what makes equipping mid-battle
    /// safe to call more than once - re-running this after a second item is equipped does not compound
    /// on top of whatever the first pass already wrote, because there is nothing left of the first pass
    /// to compound onto by the time modifiers run again.
    ///
    /// Called from Equip. BuildDeck needs no call of its own: every card it constructs already routes
    /// through NewCard, which applies the current equipment at construction time - see SetEquipment's
    /// ordering note on why SpawnParty calls it before SetDeck specifically so that holds true for a
    /// freshly spawned party member too.
    /// </summary>
    public void RefreshCardModifiers()
    {
        RefreshCardModifiers(hand);
        RefreshCardModifiers(drawPile);
        RefreshCardModifiers(discardPile);

        // innateCards is a subset of hand (see PutInHand/BuildDeck) sharing the same Card instances, so
        // walking it again would re-apply modifiers to cards RefreshCardModifiers(hand) already touched
        // - only worth a separate pass if an innate card ever lived outside hand, which it never does.

        CardsModified?.Invoke(this);
    }

    private void RefreshCardModifiers(List<Card> cards)
    {
        foreach (Card card in cards)
        {
            if (card == null) { continue; }

            card.ResetToAuthored();
            ApplyCardModifiers(card, card.Data);
        }
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

            Card card = NewCard(data);

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

        int moved = discardPile.Count;

        drawPile.AddRange(discardPile);
        discardPile.Clear();
        Shuffle(drawPile);

        PilesReshuffled?.Invoke(this, moved);
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

        // Cached for the same reason, and null-tolerant for the same reason: not every body has one.
        // Heroes and totems carry a SortingGroup so their several renderers sort as a unit; the
        // single-sprite enemies have one added by Tools/Board/2 - Wire Cameras so they can be depth
        // sorted the same way.
        sortingGroup = GetComponent<UnityEngine.Rendering.SortingGroup>();
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

    /// <summary>
    /// Dumps the whole hand.
    ///
    /// Iterates a snapshot rather than looping while the hand is non-empty. The old form assumed every
    /// Discard shrinks the hand by one, and the first keyword that put a card back - Rebound - turned
    /// that into an infinite loop that froze the Editor hard enough to need Task Manager. A snapshot
    /// cannot spin however Discard behaves, and a card that legitimately survives a dump simply stays.
    /// </summary>
    public void DiscardHand()
    {
        foreach (Card card in new List<Card>(hand))
        {
            Discard(card);
        }
    }
}
