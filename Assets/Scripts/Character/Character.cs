using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// A unit on the board. Everything a card can do to a character arrives through the tile it's standing
/// on, so these are the entry points GridTile forwards to. Incoming damage is mitigated by Parry, then
/// Block, then Shield, in that order - see TakeDamage.
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

    /// Buffs and curses alike. One list, because they are the same machinery - see StatusType.
    private readonly List<Status> statuses = new();

    /// Buffs currently maintained by an AuraSource this character is standing near. Separate from
    /// statuses so leaving range can withdraw exactly what one aura granted - see AppliedAura.
    private readonly List<AppliedAura> auraEffects = new();

    /// Current health. Serialized only so the live value is watchable in the Inspector during Play
    /// Mode; [ReadOnlyField] greys it out so nobody can type into it. Awake overwrites whatever was
    /// saved, so the stored value is a readout, never authoring data - edit Max Health instead.
    [field: SerializeField, ReadOnlyField]
    public int Health { get; private set; }

    public int MaxHealth => maxHealth;

    /// Each character has their own pool; playing a card spends the acting character's energy.
    public int Energy { get; private set; }

    /// A barrier of extra health sitting on top of Health. Absorbs damage ahead of it and is wiped at
    /// the start of every turn. Serialized purely as an Inspector readout, same as Health.
    [field: SerializeField, ReadOnlyField]
    public int Shield { get; private set; }

    /// A flat reduction applied to each hit while charges remain, unlike Shield which is a pool of
    /// extra health - Block 5 against three hits of 8 leaves three hits of 3, not one hit absorbed.
    /// BlockCharges is how many more hits it still applies to; BlockAmount is the reduction each of
    /// those hits gets.
    [field: SerializeField, ReadOnlyField]
    public int BlockAmount { get; private set; }

    [field: SerializeField, ReadOnlyField]
    public int BlockCharges { get; private set; }

    /// While charges remain, each hit is fully negated and dealt back to whoever threw it instead of
    /// landing here at all.
    [field: SerializeField, ReadOnlyField]
    public int ParryCharges { get; private set; }

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

    /// Frozen stops a character dead. Checked before a card is paid for, and by the turn loop when it
    /// asks whether anybody can still act - a fully frozen party would otherwise stall the turn.
    public bool CanAct => !IsDead && StatusStacks(StatusType.Frozen) <= 0;

    public IReadOnlyList<Status> Statuses => statuses;

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

    /// Shield is spent before health and does not carry between turns. Persistent shield would not
    /// survive this card set - Steely Attack grants 5 for 1 energy while also dealing damage, so at
    /// three plays a turn the Knight would bank 15 a turn and stop being killable.
    public void ResetShield()
    {
        Shield = 0;
        UpdateHealthBar();
    }

    /// <summary>
    /// The single choke point all ordinary damage passes through - GridTile.DealDamage forwards here.
    /// Mitigation applies in order: Parry negates the hit outright and reflects it at attacker, Block
    /// takes a flat amount off, then whatever remains is absorbed by Shield before it reaches Health.
    ///
    /// attacker is who to reflect a parried hit back at - null (from Poison-style sources with no
    /// attacker) just means a parried hit vanishes instead of reflecting.
    /// </summary>
    public void TakeDamage(int amount, Character attacker = null)
    {
        if (amount <= 0) { return; }

        if (ParryCharges > 0)
        {
            ParryCharges--;
            if (attacker != null) { attacker.TakeUnblockableDamage(amount); }
            return;
        }

        if (BlockCharges > 0)
        {
            amount = Mathf.Max(0, amount - BlockAmount);
            BlockCharges--;
        }

        int absorbed = Mathf.Min(Shield, amount);
        Shield -= absorbed;
        Health = Mathf.Max(0, Health - (amount - absorbed));
        UpdateHealthBar();
        CheckDeath();
    }

    /// Damage that ignores Shield, Block and Parry. Poison is not an attack - those are defenses held
    /// up against something being thrown at you, and they do nothing about something already in your
    /// blood. Also what a parried hit reflects with, so a parry can never itself be parried.
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

    public void AddShield(int amount) { Shield += Mathf.Max(0, amount); UpdateHealthBar(); }

    /// <summary>
    /// Grants Block: the next `count` hits are each reduced by `amount`. Re-applying while charges
    /// remain keeps the higher of the two amounts and adds to the charge count, so a top-up can never
    /// downgrade what is already there.
    /// </summary>
    public void GainBlock(int amount, int count)
    {
        if (amount <= 0 || count <= 0) { return; }

        BlockAmount = BlockCharges > 0 ? Mathf.Max(BlockAmount, amount) : amount;
        BlockCharges += count;
    }

    /// Grants Parry: the next `count` hits are each negated and reflected back at the attacker instead
    /// of landing here. See TakeDamage.
    public void GainParry(int count)
    {
        if (count <= 0) { return; }

        ParryCharges += count;
    }

    private void UpdateHealthBar()
    {
        healthBar.text = Health.ToString() + "/" + maxHealth.ToString() + " (" + Shield.ToString() + ")";
    }

    /// How much of `type` this character currently has, from statuses and any aura buffs combined -
    /// callers like ComputeOutgoingDamage should not have to care which source it came from.
    public int StatusStacks(StatusType type)
    {
        int total = 0;

        foreach (Status status in statuses)
        {
            if (status.type == type) { total += status.stacks; }
        }

        foreach (AppliedAura aura in auraEffects)
        {
            if (aura.type == type) { total += aura.stacks; }
        }

        return total;
    }

    /// <summary>
    /// Grants a passive buff on behalf of an AuraSource, for as long as this character stands in its
    /// range. Tracked apart from AddStatus/statuses precisely so RemoveAura can later take back exactly
    /// this grant - see AppliedAura.
    /// </summary>
    public void ApplyAura(AuraSource source, StatusType type, int stacks)
    {
        if (type == StatusType.None || stacks <= 0) { return; }

        auraEffects.Add(new AppliedAura(source, type, stacks));
    }

    /// Withdraws every buff the given aura granted - called once a character leaves its range, or the
    /// aura's own source dies.
    public void RemoveAura(AuraSource source)
    {
        auraEffects.RemoveAll(a => a.source == source);
    }

    /// <summary>
    /// Applies a status, stacking onto one already present. Re-applying takes the *longer* of the two
    /// durations so a top-up can never shorten what is already there - and Indefinite, being -1, has
    /// to be special-cased or Mathf.Max would treat it as the shortest.
    /// </summary>
    public void AddStatus(StatusType type, int stacks, int turnsRemaining)
    {
        if (type == StatusType.None || stacks <= 0) { return; }

        foreach (Status existing in statuses)
        {
            if (existing.type != type) { continue; }

            existing.stacks += stacks;

            if (existing.turnsRemaining != Status.Indefinite)
            {
                existing.turnsRemaining = turnsRemaining == Status.Indefinite
                    ? Status.Indefinite
                    : Mathf.Max(existing.turnsRemaining, turnsRemaining);
            }

            return;
        }

        statuses.Add(new Status(type, stacks, turnsRemaining));
    }

    /// Spends one charge of a status, for the ones an event uses up rather than time. True if there
    /// was one to spend.
    public bool ConsumeStatus(StatusType type)
    {
        for (int i = 0; i < statuses.Count; i++)
        {
            if (statuses[i].type != type || statuses[i].stacks <= 0) { continue; }

            statuses[i].stacks--;
            if (statuses[i].IsExpired) { statuses.RemoveAt(i); }

            return true;
        }

        return false;
    }

    /// <summary>
    /// One of this character's own turns passing: curses that deal damage do it, then every timed
    /// status ages by one and the expired ones drop. Statuses with no duration (Strength, and
    /// charge-spent ones like DoubleNextAttack) are untouched.
    ///
    /// Called by BattleManager at the end of this character's own phase - PlayerActing for
    /// player-controlled characters, EnemyResolve for enemies - never at the shared TurnStart. See
    /// BattleManager.TickStatuses(bool) for why the timing matters.
    ///
    /// Iterated backwards because expiring a status removes it mid-loop.
    /// </summary>
    public void TickStatuses()
    {
        for (int i = statuses.Count - 1; i >= 0; i--)
        {
            Status status = statuses[i];

            if (status.type == StatusType.Poison) { TakeUnblockableDamage(status.stacks); }

            if (status.turnsRemaining > 0) { status.turnsRemaining--; }

            if (status.IsExpired) { statuses.RemoveAt(i); }
        }
    }

    /// <summary>
    /// What this character's attack lands for, once Strength and Double Attack are applied.
    ///
    /// shouldConsume is the whole difference between swinging and looking. True spends the Double
    /// Attack charge and belongs to the one place actually attacking - DamageAction. False leaves it
    /// untouched, for tooltips, damage previews and enemy AI scoring a move it has not made yet.
    /// Passing true to display a number would destroy the buff without an attack ever happening.
    ///
    /// Double Attack multiplies the card's own number and Strength is added after, so Quick Attack
    /// at 9 with +3 Strength and a Double Attack lands (9 x 2) + 3 = 21.
    /// </summary>
    public int ComputeOutgoingDamage(int amount, bool shouldConsume)
    {
        bool doubled = shouldConsume
            ? ConsumeStatus(StatusType.DoubleNextAttack)
            : StatusStacks(StatusType.DoubleNextAttack) > 0;

        if (doubled) { amount *= 2; }

        return amount + StatusStacks(StatusType.Strength);
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

    private void PlaceOnStartTile()
    {
        GridTile tile = GridManager.Instance != null ? GridManager.Instance.GetTile(startCoordinates) : null;

        if (tile != null)
        {
            MoveTo(tile);
            transform.position = tile.transform.position;
        }
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
