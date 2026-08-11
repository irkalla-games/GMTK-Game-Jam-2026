using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The deck-thinning screen RemoveCardSkipReward opens: every card in one hero's run deck, laid out in
/// a grid, click one to delete it. RewardPanel's near-sibling - same "real CardViewers, purely a view"
/// shape - but a grid instead of a row, because a deck can run to a dozen-plus cards where an offer is
/// only ever a handful.
///
/// Resolves by index, not by CardData: a deck routinely holds several identical copies (four Move
/// cards is typical), and the player clicked one specific tile in the grid, not "a Move" in the
/// abstract. RemoveCardSkipReward is the only reader, and removes with List.RemoveAt for exactly that
/// reason.
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

    public void Show(IReadOnlyList<CardData> deck, string heroName)
    {
        Resolved = false;
        ChosenIndex = -1;

        Clear();

        if (root != null) { root.SetActive(true); }

        if (titleLabel != null)
        {
            titleLabel.text = heroName != null ? $"{heroName}'s deck — choose a card to remove" : string.Empty;
        }

        SpawnGrid(deck);
    }

    private void SpawnGrid(IReadOnlyList<CardData> deck)
    {
        if (cardPrefab == null || gridAnchor == null || deck == null)
        {
            Debug.LogError($"{name}: cardPrefab, gridAnchor or deck not set - removal panel cannot show cards");
            return;
        }

        int count = deck.Count;
        int cols = Mathf.Max(1, columns);
        int rows = Mathf.CeilToInt(count / (float)cols);

        float startX = -(cols - 1) * cellWidth / 2f;
        float startY = (rows - 1) * cellHeight / 2f;

        for (int i = 0; i < count; i++)
        {
            CardData data = deck[i];

            if (data == null) { continue; }

            int col = i % cols;
            int row = i / cols;

            Vector3 position = gridAnchor.position
                + new Vector3(startX + col * cellWidth, startY - row * cellHeight, 0f);

            // Captured per-iteration on purpose - `i` is reassigned every loop, `chosenIndex` is not.
            int chosenIndex = i;

            CardViewer viewer = OfferedCard.Spawn(
                cardPrefab, data, position, i, cardScale, cardHoverScale, _ => Choose(chosenIndex));

            spawnedCards.Add(viewer);
        }
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
