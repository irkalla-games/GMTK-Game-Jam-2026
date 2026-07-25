using System;
using System.Collections.Generic;
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

    [SerializeField] private int maxEnergy = 3;

    [SerializeField] private bool isPlayerControlled;

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

    /// Current health. Serialized only so the live value is watchable in the Inspector during Play
    /// Mode; [ReadOnlyField] greys it out so nobody can type into it. Awake overwrites whatever was
    /// saved, so the stored value is a readout, never authoring data - edit Max Health instead.
    [field: SerializeField, ReadOnlyField]
    public int Health { get; private set; }

    /// Each character has their own pool; playing a card spends the acting character's energy.
    public int Energy { get; private set; }

    public bool IsPlayerControlled => isPlayerControlled;

    public bool IsDead => Health <= 0;

    /// The tile this character is standing on.
    public GridTile Tile { get; private set; }

    /// The cards currently held. GameManager builds the viewers for whichever character is active.
    public IReadOnlyList<Card> Hand => hand;

    /// Raised when a card lands in this hand. GameManager listens so the row on screen can follow the
    /// active character - drawing itself is none of its business.
    public event Action<Character, Card> CardDrawn;

    public bool CanAfford(int cost) => cost <= Energy;

    public void SpendEnergy(int cost)
    {
        Energy = Mathf.Max(0, Energy - cost);
    }

    public void ResetEnergy()
    {
        Energy = maxEnergy;
    }

    //TODO: block/shield/parry mitigation. Right now damage goes straight to health.
    public void TakeDamage(int amount) { Health = Mathf.Max(0, Health - amount); }

    public void Heal(int amount) { Health = Mathf.Min(maxHealth, Health + amount); }

    public void AddShield(int amount) { }

    public void GainBlock(int amount, int count) { }

    public void GainParry(int reflectTotal, int count) { }

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
        GridTile tile = GridManager.Instance != null ? GridManager.Instance.GetTile(startCoordinates) : null;
        if (tile != null)
        {
            MoveTo(tile);
            transform.position = tile.transform.position;
        }
    }
}
