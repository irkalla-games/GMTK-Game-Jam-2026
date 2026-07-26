using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// A unit on the board. Everything a card can do to a character arrives through the tile it's standing
/// on, so these are the entry points GridTile forwards to. Combat rules (how block/shield/parry
/// actually reduce incoming damage, death, statuses) are not implemented here.
///
/// Each character owns its own deck and piles. Clicking a character makes it the active one, and its
/// hand is what you see; there is no turn order.
/// </summary>
public class Character : MonoBehaviour
{
    [SerializeField] private int maxHealth = 10;

    [SerializeField] private TextMeshProUGUI healthBar;

    [SerializeField] private int maxEnergy = 3;

    [SerializeField] private bool isPlayerControlled;

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

    [Tooltip("This character's deck as authored. Card objects are built from it once, in Awake.")]
    [SerializeField] private List<CardData> deck = new();

    // Card objects live here for the whole battle and move between piles. They are *not* rebuilt on
    // each draw - a Card carries per-copy state (temporary cost, ethereal) that has to survive being
    // played and redrawn.
    private readonly List<Card> drawPile = new();

    private readonly List<Card> hand = new();

    private readonly List<Card> discardPile = new();

    /// Buffs and curses alike. One list, because they are the same machinery - see StatusType.
    private readonly List<Status> statuses = new();

    /// Current health. Serialized only so the live value is watchable in the Inspector during Play
    /// Mode; [ReadOnlyField] greys it out so nobody can type into it. Awake overwrites whatever was
    /// saved, so the stored value is a readout, never authoring data - edit Max Health instead.
    [field: SerializeField, ReadOnlyField]
    public int Health { get; private set; }

    /// Each character has their own pool; playing a card spends the acting character's energy.
    public int Energy { get; private set; }

    /// Absorbs damage ahead of health and is wiped at the start of every turn. Serialized purely as
    /// an Inspector readout, same as Health.
    [field: SerializeField, ReadOnlyField]
    public int Armor { get; private set; }

    public bool IsPlayerControlled => isPlayerControlled;

    public CharacterClass Class => characterClass;

    public int ActionPoints => actionPoints;

    public BrainType Brain => brain;


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

    /// Raised once, when this character drops to 0 and leaves the board.
    public event Action<Character> Died;

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

    /// Armor is spent before health and does not carry between turns. Persistent armor would not
    /// survive this card set - Steely Attack grants 5 for 1 energy while also dealing damage, so at
    /// three plays a turn the Knight would bank 15 a turn and stop being killable.
    public void ResetArmor()
    {
        Armor = 0;
        healthBar.text = Health.ToString() + "/" + maxHealth.ToString() + " (" + Armor.ToString() + ")";
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0) { return; }

        int absorbed = Mathf.Min(Armor, amount);
        Armor -= absorbed;
        Health = Mathf.Max(0, Health - (amount - absorbed));
        healthBar.text = Health.ToString() + "/" + maxHealth.ToString() + " (" + Armor.ToString() + ")";
        CheckDeath();
    }

    /// Damage that ignores armor. Poison is not an attack - armor is a shield you hold up, and it
    /// does nothing about something already in your blood.
    public void TakeUnblockableDamage(int amount)
    {
        if (amount <= 0) { return; }

        Health = Mathf.Max(0, Health - amount);

        CheckDeath();
    }

    /// <summary>
    /// Leaves the board on death. MoveTo(null) is the only path that clears GridTile.Occupant, so
    /// without this a corpse holds its tile for the rest of the battle - blocking movement, refusing
    /// Move cards aimed at it, and soaking attacks that should have hit somebody alive.
    /// </summary>
    private void CheckDeath()
    {
        if (!IsDead || Tile == null) { return; }

        Debug.Log($"{name} is down");

        MoveTo(null);
        Died?.Invoke(this);
    }

    public void Heal(int amount) { Health = Mathf.Min(maxHealth, Health + amount); healthBar.text = Health.ToString() + "/" + maxHealth.ToString() + " (" + Armor.ToString() + ")"; }

    public void AddShield(int amount) { Armor += Mathf.Max(0, amount); healthBar.text = Health.ToString() + "/" + maxHealth.ToString() + " (" + Armor.ToString() + ")"; }

    //TODO: block is per-instance damage reduction, unlike armor which is a pool. Not needed by any
    //card in either kit yet - if it stays unwanted, delete BlockAction rather than leave a second
    //half-built mitigation system next to a working one.
    public void GainBlock(int amount, int count) { }

    //TODO: parry reflects damage back at the attacker. Same story as block.
    public void GainParry(int reflectTotal, int count) { }

    public int StatusStacks(StatusType type)
    {
        foreach (Status status in statuses)
        {
            if (status.type == type) { return status.stacks; }
        }

        return 0;
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
    /// One turn passing: curses that deal damage do it, then every timed status ages by one and the
    /// expired ones drop. Statuses with no duration (Strength, and charge-spent ones like
    /// DoubleNextAttack) are untouched.
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
        hand.Remove(card);
        discardPile.Add(card);
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

        foreach (CardData data in deck)
        {
            if (data != null) { drawPile.Add(new Card(data)); }
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
}
