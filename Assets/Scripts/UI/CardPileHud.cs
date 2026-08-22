using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Deck and Discard icons: a live count of each pile, a click that opens CardPilePanel on it, and
/// the chip flight that plays when a reshuffle folds the discard pile back into the draw pile.
///
/// Follows the active character the same way ActiveHandViewer.ShowHandFor does - the count and the
/// grid a click opens must always belong to whoever's hand is currently on screen, not whoever last
/// drew a card. See ShowFor below, which is that method's shape.
/// </summary>
public class CardPileHud : Singleton<CardPileHud>
{
    [Header("Deck")]
    [SerializeField] private Button deckButton;

    [SerializeField] private TMP_Text deckCountLabel;

    [Header("Discard")]
    [SerializeField] private Button discardButton;

    [SerializeField] private TMP_Text discardCountLabel;

    [Header("Reshuffle flight")]
    [Tooltip("Placeholder card-back art for both icons and the flight chips, until real art exists - "
             + "see CardPileWiring, which points this at CardPrefab's own CardBackground sprite.")]
    [SerializeField] private Sprite chipSprite;

    [SerializeField] private float chipScale = 0.4f;

    [SerializeField] private float flightDuration = 0.35f;

    [SerializeField] private float flightStagger = 0.04f;

    [Tooltip("Caps how many chips a big reshuffle spawns - a 20-card discard pile flying back in one "
             + "at a time would take far longer than the pause is worth.")]
    [SerializeField] private int maxChips = 8;

    /// Whose piles are on screen. Held so the subscriptions can be moved off it on a switch, same as
    /// ActiveHandViewer.shown.
    private Character shown;

    private void Start()
    {
        if (deckButton != null) { deckButton.onClick.AddListener(OnDeckClicked); }
        if (discardButton != null) { discardButton.onClick.AddListener(OnDiscardClicked); }

        BattleManager battle = BattleManager.Instance;

        if (battle == null)
        {
            Debug.LogError($"{name}: no BattleManager in the scene - the pile counts cannot follow anybody");
            return;
        }

        battle.ActiveCharacterChanged += ShowFor;

        if (battle.ActiveCharacter != null) { ShowFor(battle.ActiveCharacter); }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (shown != null)
        {
            shown.CardDrawn -= OnPilesChanged;
            shown.CardDiscarded -= OnPilesChanged;
            shown.PilesReshuffled -= OnPilesReshuffled;
        }

        if (BattleManager.Instance != null) { BattleManager.Instance.ActiveCharacterChanged -= ShowFor; }
    }

    /// Same per-frame interactable sync EndTurnButton already uses - the discard pile has to be inert
    /// for the whole tutorial run, not just refused post-click, so free play in turn 3 cannot walk the
    /// player into an unexpected state. Self-restores the instant TutorialDirector.End() flips
    /// TutorialRunning false, so no separate skip/teardown handling is needed.
    private void Update()
    {
        if (discardButton != null) { discardButton.interactable = !TutorialDirector.TutorialRunning; }
    }

    private void ShowFor(Character character)
    {
        if (shown != null)
        {
            shown.CardDrawn -= OnPilesChanged;
            shown.CardDiscarded -= OnPilesChanged;
            shown.PilesReshuffled -= OnPilesReshuffled;
        }

        shown = character;

        if (shown != null)
        {
            shown.CardDrawn += OnPilesChanged;
            shown.CardDiscarded += OnPilesChanged;
            shown.PilesReshuffled += OnPilesReshuffled;
        }

        RefreshCounts();
    }

    private void OnPilesChanged(Character character, Card card) => RefreshCounts();

    private void OnPilesReshuffled(Character character, int count) => RefreshCounts();

    private void RefreshCounts()
    {
        if (deckCountLabel != null) { deckCountLabel.text = shown != null ? shown.DrawPile.Count.ToString() : "0"; }
        if (discardCountLabel != null) { discardCountLabel.text = shown != null ? shown.DiscardPile.Count.ToString() : "0"; }
    }

    private void OnDeckClicked()
    {
        if (shown == null) { return; }
        if (BattleManager.Instance != null && BattleManager.Instance.InputLocked) { return; }
        if (CardPilePanel.Instance == null) { return; }

        // Alphabetical, not the real draw order - the draw pile is shuffled, and showing the true
        // order would tell the player exactly what they draw next.
        List<Card> sorted = shown.DrawPile.OrderBy(c => c.cardName).ToList();
        CardPilePanel.Instance.Show(sorted, "Draw pile");
    }

    private void OnDiscardClicked()
    {
        if (shown == null) { return; }
        if (BattleManager.Instance != null && BattleManager.Instance.InputLocked) { return; }
        if (CardPilePanel.Instance == null) { return; }

        // Newest-first: nothing hides here, so showing the real order is just showing what happened.
        List<Card> reversed = shown.DiscardPile.Reverse().ToList();
        CardPilePanel.Instance.Show(reversed, "Discard pile");
    }

    /// Plays the discard-pile-flies-into-the-deck flourish. Owned here rather than by ActiveHandViewer
    /// because this is the class that owns both anchors and the chip art; ActiveHandViewer only owns
    /// the deal queue this has to be sequenced inside of - see its DrainDealQueue.
    public IEnumerator PlayReshuffle(int count)
    {
        if (deckButton == null || discardButton == null) { yield break; }

        int chips = Mathf.Min(count, maxChips);

        yield return CardChipFlight.Play(
            chipSprite, discardButton.transform.position, deckButton.transform.position,
            chips, chipScale, flightDuration, flightStagger);
    }
}
