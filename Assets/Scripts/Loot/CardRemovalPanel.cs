using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The deck-browsing choice screen a level-clear skip reward opens: every card in one hero's run deck,
/// laid out in a grid, click one to commit to it. RewardPanel's near-sibling - same "real CardViewers,
/// purely a view" shape - but a grid instead of a row, because a deck can run to a dozen-plus cards
/// where an offer is only ever a handful.
///
/// Two skip rewards share this one screen rather than each getting its own: RemoveCardSkipReward deletes
/// the chosen card, UpgradeCardSkipReward replaces it with CardData.upgradedForm. Both are "browse the
/// deck, commit to one card" - the same shape CardGridView already generalizes over CardPilePanel's
/// look-then-close - so the difference between them is entirely in what Grant does with ChosenIndex, not
/// in this class. `eligible`, when given, greys out (and disables the click on) any card the calling
/// skip reward would refuse - a card with no upgradedForm, on the upgrade screen.
///
/// Resolves by index, not by CardData: a deck routinely holds several identical copies (four Move
/// cards is typical), and the player clicked one specific tile in the grid, not "a Move" in the
/// abstract. Both skip rewards read ChosenIndex for exactly that reason.
/// </summary>
public class CardRemovalPanel : Singleton<CardRemovalPanel>
{
    [Tooltip("Backdrop root, toggled on Show/Hide.")]
    [SerializeField] private GameObject root;

    [Tooltip("\"Knight's deck\", so it reads as removal from a specific hero rather than the whole run.")]
    [SerializeField] private TMP_Text titleLabel;

    [Tooltip("Same prefab hands and RewardPanel are built from.")]
    [SerializeField] private CardViewer cardPrefab;

    [Tooltip("Where the grid is centred. A world position, like RewardPanel.cardAnchor - CardViewer is "
             + "a plain world-space sprite object, never parented under UI.")]
    [SerializeField] private Transform gridAnchor;

    [SerializeField] private int columns = 6;

    [SerializeField] private float cellWidth = 2.2f;

    [SerializeField] private float cellHeight = 3.2f;

    [Tooltip("Tallest the grid may run before CardGridView scales cells down to fit - keeps a big deck "
             + "on screen instead of running off the top of CameraFrame's fixed 10.8-unit height. 0 "
             + "disables the clamp.")]
    [SerializeField] private float maxGridHeight = 9f;

    [Tooltip("How large a card sits in the grid. Smaller than RewardPanel's default - this shows a "
             + "whole deck at once, not a handful of choices.")]
    [SerializeField] private float cardScale = 0.7f;

    [Tooltip("Multiplier on top of Card Scale while hovered - CardViewer computes rest * hover, so "
             + "this has to be above 1 or hovering shrinks the card instead of popping it up.")]
    [SerializeField] private float cardHoverScale = 1.08f;

    [SerializeField] private Button cancelButton;

    private readonly List<CardViewer> spawnedCards = new();

    /// True once the player has removed a card or cancelled. RemoveCardSkipReward's WaitUntil polls this.
    public bool Resolved { get; private set; }

    /// Index into the deck passed to Show, or -1 if the player cancelled.
    public int ChosenIndex { get; private set; } = -1;

    /// Starts hidden regardless of the scene's authored state - same reasoning as RewardPanel.Awake.
    protected override void Awake()
    {
        base.Awake();

        if (root != null) { root.SetActive(false); }

        if (cancelButton != null) { cancelButton.onClick.AddListener(Cancel); }
    }

    /// <summary>
    /// `prompt` fills the back half of the title - "choose a card to remove" vs "choose a card to
    /// upgrade" - so the same panel reads correctly for either skip reward. `eligible`, when given,
    /// greys out and disables the click on any deck entry it returns false for; null (the default)
    /// leaves every card clickable, which is RemoveCardSkipReward's original behaviour unchanged.
    /// </summary>
    public void Show(IReadOnlyList<CardData> deck, string heroName, string prompt = "choose a card to remove",
                      Predicate<CardData> eligible = null)
    {
        Resolved = false;
        ChosenIndex = -1;

        Clear();

        if (root != null) { root.SetActive(true); }

        if (titleLabel != null)
        {
            titleLabel.text = heroName != null ? $"{heroName}'s deck — {prompt}" : string.Empty;
        }

        SpawnGrid(deck, eligible);
    }

    private void SpawnGrid(IReadOnlyList<CardData> deck, Predicate<CardData> eligible)
    {
        if (cardPrefab == null || gridAnchor == null || deck == null)
        {
            Debug.LogError($"{name}: cardPrefab, gridAnchor or deck not set - removal panel cannot show cards");
            return;
        }

        // The grid works from the authored deck list (CardData), not runtime copies - a card chosen
        // here has never been drawn this battle (the deck record, not a live hand), so there is no
        // existing Card to show. A fresh one per entry is exactly what the offer-screen path already
        // builds.
        //
        // Built with one slot per deck entry, nulls included, so a null CardData still leaves a gap
        // rather than shifting every later index - Choose(chosenIndex) has to land on the same index
        // the calling skip reward then RemoveAt()s or replaces out of the same deck list.
        List<Card> cards = new(deck.Count);
        foreach (CardData data in deck)
        {
            cards.Add(data != null ? new Card(data) : null);
        }

        CardGridView.Build(
            cards, cardPrefab, gridAnchor, columns, cellWidth, cellHeight, cardScale, cardHoverScale,
            maxGridHeight, Choose, spawnedCards,
            eligible != null ? i => eligible(deck[i]) : null);
    }

    private void Choose(int index)
    {
        if (Resolved) { return; }

        ChosenIndex = index;
        Resolved = true;
        Hide();
    }

    private void Cancel()
    {
        if (Resolved) { return; }

        ChosenIndex = -1;
        Resolved = true;
        Hide();
    }

    private void Hide()
    {
        if (root != null) { root.SetActive(false); }

        // Safe even though this may run from inside a just-clicked card's own OnMouseDown: Destroy is
        // deferred to end of frame, so the callback finishes on a still-live object.
        Clear();
    }

    private void Clear()
    {
        foreach (CardViewer viewer in spawnedCards)
        {
            if (viewer != null) { Destroy(viewer.gameObject); }
        }

        spawnedCards.Clear();
    }
}
