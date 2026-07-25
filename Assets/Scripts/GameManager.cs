using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owns which character you are currently playing as, and the hand on screen.
///
/// There is no turn order: every character holds its own deck, and clicking one makes it active. The
/// hand you see always belongs to the active character, so switching swaps the whole hand over.
/// </summary>
public class GameManager : Singleton<GameManager>
{
    [Tooltip("Every character on the board. Each one owns its own deck; this is only used to deal the "
             + "opening hands.")]
    [SerializeField] private List<Character> characters = new();

    [SerializeField] private int openingHandSize = 5;

    /// Whose hand is on screen. Cards are played by this character and spend its energy.
    public Character ActiveCharacter { get; private set; }

    private void Start()
    {
        // Characters build their decks in Awake, so every draw pile is ready by now. Nothing is on
        // screen yet either - ActiveCharacter is still null, so these opening draws build no viewers
        // and SetActiveCharacter lays out the whole hand in one go below.
        foreach (Character character in characters)
        {
            if (character == null) { continue; }

            character.CardDrawn += OnCardDrawn;
            character.DrawCards(openingHandSize);
        }

        SetActiveCharacter(FirstPlayableCharacter());
    }

    /// Override rather than a plain OnDestroy: Unity dispatches the message to the most-derived
    /// method only, so hiding the base would silently stop Singleton from clearing Instance.
    protected override void OnDestroy()
    {
        base.OnDestroy();

        foreach (Character character in characters)
        {
            if (character != null) { character.CardDrawn -= OnCardDrawn; }
        }
    }

    /// Keeps the row on screen honest: a card drawn by the active character shows up straight away,
    /// one drawn by anybody else stays in their hand until you switch to them.
    private void OnCardDrawn(Character character, Card card)
    {
        if (character == ActiveCharacter) { AddToVisibleHand(card); }
    }

    /// Every tile click lands here. With a card selected it is a play; with nothing selected it means
    /// "play as whoever is standing here" - characters have no colliders of their own, so the tile
    /// under one is what you click to pick it up.
    public void OnTileClicked(GridTile tile)
    {
        if (tile == null) { return; }

        CardPlayManager cardPlayManager = CardPlayManager.Instance;

        if (cardPlayManager != null && cardPlayManager.HasSelection)
        {
            cardPlayManager.PlaySelectedOn(tile);
            return;
        }

        Character occupant = tile.Occupant;

        if (occupant == null || !occupant.IsPlayerControlled)
        {
            string who = occupant != null ? $"{occupant.name} is not player controlled" : "nobody here";
            Debug.Log($"tile clicked: {tile.Coordinates} - no card selected, {who}");
            return;
        }

        Debug.Log($"tile clicked: {tile.Coordinates} - activating {occupant.name}");
        SetActiveCharacter(occupant);
    }

    /// Makes this character the one you are playing as and swaps the hand on screen over to its cards.
    public void SetActiveCharacter(Character character)
    {
        if (character == null || character == ActiveCharacter) { return; }

        ActiveCharacter = character;
        Debug.Log($"active character: {character.name} (energy {character.Energy}, {character.Hand.Count} in hand)");

        ShowHandFor(character);
    }

    private void ShowHandFor(Character character)
    {
        HandViewer.Instance.ClearHand();

        foreach (Card card in character.Hand) { AddToVisibleHand(card); }
    }

    private void AddToVisibleHand(Card card)
    {
        CardViewer cardViewer = CreateCardViewer.Instance.CreateCard(card, transform.position, Quaternion.identity);
        StartCoroutine(HandViewer.Instance.AddCard(cardViewer));
    }

    private Character FirstPlayableCharacter()
    {
        foreach (Character character in characters)
        {
            if (character != null && character.IsPlayerControlled && !character.IsDead) { return character; }
        }

        return null;
    }

    private void Update()
    {
        if (Keyboard.current == null || ActiveCharacter == null) { return; }

        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            ActiveCharacter.DrawCard();
        }
    }
}
