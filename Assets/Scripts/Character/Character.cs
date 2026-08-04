using System;
using System.Collections.Generic;
using TMPro;
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

    [SerializeField] private TextMeshProUGUI healthBar;

    [SerializeField] private int maxEnergy = 3;

    [SerializeField] private PlayableCharacter playableCharacter = PlayableCharacter.Enemy;

    [Tooltip("Which cards this character may hold. Cards not matching are skipped when the deck is "
             + "built.")]
    [SerializeField] private CharacterClass characterClass;

    [Tooltip("Actions this character takes per turn. Enemies only - players spend energy instead.")]
    [SerializeField] private int actionPoints = 2;

    [Tooltip("Which rule list this enemy runs. None means it stands there - correct for players.")]
    [SerializeField] private BrainType brain;

    // No attack damage, reach or move speed here. Those are properties of the cards a character
    // holds - a goblin hits for 6 because it is holding a card that deals 6, and reaches two tiles
    // because that card's TargetRange says two. Duplicating them onto the character would be a
    // second answer to a question the card already answers, and the two would drift.

    [Tooltip("Grid cell this character starts on. Placed onto that tile at battle start.")]
    [SerializeField] private Vector2Int startCoordinates;

    [Tooltip("Spawned on this character's tile when it dies. Leave empty for characters that drop "
             + "nothing - party members typically do.")]
    [SerializeField] private GameObject itemDropPrefab;

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

    public int ActionPoints => actionPoints;

    public BrainType Brain => brain;

    public GameObject ItemDropPrefab => itemDropPrefab;


    /// <summary>
    /// What this enemy told the player it was going to do, decided at the start of the turn.
    ///
    /// Held rather than recomputed because the whole point is that it can go stale: the player spends
    /// the turn making it wrong, and it executes anyway. Recomputing at execution time would quietly
    /// undo every block and every kill the player set up.
    /// </summary>
    public Intent CommittedIntent { get; set; }

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
    }

    public void ResetEnergy()
    {
        Energy = maxEnergy;
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
        UpdateHealthBar();
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
        UpdateHealthBar();

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

    public void Heal(int amount) { Health = Mathf.Min(maxHealth, Health + amount); UpdateHealthBar(); }

    private void UpdateHealthBar()
    {
        // Shield is not a field on this class - it is whatever a ShieldStatus in the list says it is.
        healthBar.text = $"{Health}/{maxHealth} ({StatusStacks(StatusType.Shield)})";
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
            UpdateHealthBar();

            return;
        }

        ownStatusEffects.Add(incoming);
        UpdateHealthBar();
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
        UpdateHealthBar();
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
    /// Ticks every card's Cooldown down by one round. Safe to only walk these three piles rather than
    /// innateCards too, as long as this runs after RestoreInnateCards each turn - by then every innate
    /// card is already back in hand.
    /// </summary>
    public void TickCardCooldowns()
    {
        foreach (Card card in drawPile) { card.TickCooldown(); }
        foreach (Card card in hand) { card.TickCooldown(); }
        foreach (Card card in discardPile) { card.TickCooldown(); }
    }

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

    private void BuildDeck()
    {
        drawPile.Clear();
        hand.Clear();
        discardPile.Clear();
        innateCards.Clear();

        foreach (CardData data in deck)
        {
            if (data == null) { continue; }

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

    /// Hands the whole job to GridManager: claiming the tile and standing the body on it are one
    /// operation, and where a cell sits in world space is the board's business, not a character's.
    private void PlaceOnStartTile()
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
