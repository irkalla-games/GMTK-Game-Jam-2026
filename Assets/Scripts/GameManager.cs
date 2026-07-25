using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// Owns the main game loop: whose turn it is, when energy refreshes, when the battle ends.
public class GameManager : Singleton<GameManager>
{
    [Tooltip("The deck as authored. Card objects are built from this once, at the start of a battle.")]
    public List<CardData> deck;

    [SerializeField] private HandViewer handViewer;

    [SerializeField] private List<Character> turnOrder = new();

    [SerializeField] private int cardsPerTurn = 5;

    /// Whose turn it is. Cards are played by this character and spend its energy.
    public Character ActiveCharacter { get; private set; }

    public int TurnNumber { get; private set; }

    // Card objects live here for the whole battle and move between piles. They are *not* rebuilt on
    // each draw - a Card carries per-copy state (temporary cost, ethereal) that has to survive being
    // played and redrawn.
    private readonly List<Card> drawPile = new();

    public readonly List<Card> discardPile = new();

    private bool turnEnded;

    private IEnumerator Start()
    {
        yield return RunGame();
    }

    private IEnumerator RunGame()
    {
        BuildDrawPile();

        while (!IsBattleOver())
        {
            TurnNumber++;

            foreach (Character character in turnOrder)
            {
                if (character == null || character.IsDead) { continue; }

                yield return RunTurn(character);

                if (IsBattleOver()) { break; }
            }
        }
    }

    private IEnumerator RunTurn(Character character)
    {
        ActiveCharacter = character;
        character.ResetEnergy();
        turnEnded = false;

        if (character.IsPlayerControlled)
        {
            DrawCards(cardsPerTurn);

            while (!turnEnded) { yield return null; }
        }
        else
        {
            //TODO: AI turn. Non-player characters pass immediately for now.
            yield return null;
        }

        ActiveCharacter = null;
    }

    /// Ends the active player's turn. Hook to an end-turn button; also bound to Enter below.
    public void EndTurn()
    {
        turnEnded = true;
    }

    private bool IsBattleOver()
    {
        //TODO: real win/loss conditions. The battle runs while both sides still have someone alive.
        bool anyPlayer = false;
        bool anyEnemy = false;

        foreach (Character character in turnOrder)
        {
            if (character == null || character.IsDead) { continue; }

            if (character.IsPlayerControlled) { anyPlayer = true; }
            else { anyEnemy = true; }
        }

        return !anyPlayer || !anyEnemy;
    }

    private void BuildDrawPile()
    {
        drawPile.Clear();
        discardPile.Clear();

        foreach (CardData data in deck)
        {
            if (data != null) { drawPile.Add(new Card(data)); }
        }

        Shuffle(drawPile);
    }

    public void Discard(Card card)
    {
        discardPile.Add(card);
    }

    public void DrawCards(int amount)
    {
        for (int i = 0; i < amount; i++)
        {
            DrawCard();
        }
    }

    public void DrawCard()
    {
        if (drawPile.Count == 0)
        {
            Debug.Log("drawPile is empty, reshuffling");
            ReshuffleDiscardIntoDrawPile();
            return;
        }
        
        Debug.Log($"drawPile is {drawPile.Count}");
        int last = drawPile.Count - 1;
        Card card = drawPile[last];
        drawPile.RemoveAt(last);
        Debug.Log($"drew {card}, removed from drawPile");

        CardViewer cardViewer = CreateCardViewer.Instance.CreateCard(card, transform.position, Quaternion.identity);
        StartCoroutine(handViewer.AddCard(cardViewer));
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
            int j = Random.Range(0, i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
    }

    void Update()
    {
        if (Keyboard.current == null) { return; }

        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            DrawCard();
        }

        if (Keyboard.current.enterKey.wasPressedThisFrame)
        {
            EndTurn();
        }
    }
}
