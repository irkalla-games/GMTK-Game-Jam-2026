using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// CardRemovalPanel's read-only sibling: shows every card in one pile (the draw pile or the discard
/// pile) laid out in the same CardGridView grid, with nothing to choose - CardPileHud opens it, the
/// player looks, and closes it again. There is no Resolved/ChosenIndex to poll because nothing is
/// waiting on an answer; unlike the removal screen this never gates a coroutine.
///
/// Reuses CardRemovalPanel's whole shape (same field set, same Awake-hides-itself bargain, same
/// world-space CardViewer grid) rather than adding a read-only mode to that class - browsing a pile
/// and thinning a deck are different concepts wearing the same grid, and RemoveCardSkipReward's
/// "index into a list I RemoveAt from" contract has no business growing a branch for a screen that
/// never resolves anything.
/// </summary>
public class CardPilePanel : Singleton<CardPilePanel>
{
    [Tooltip("Backdrop root, toggled on Show/Hide.")]
    [SerializeField] private GameObject root;

    [Tooltip("\"Draw pile\" / \"Discard pile\" - CardPileHud passes the title per call.")]
    [SerializeField] private TMP_Text titleLabel;

    [Tooltip("Same prefab every other card grid is built from.")]
    [SerializeField] private CardViewer cardPrefab;

    [Tooltip("Where the grid is centred. A world position, like CardRemovalPanel.gridAnchor - "
             + "CardViewer is a plain world-space sprite object, never parented under UI.")]
    [SerializeField] private Transform gridAnchor;

    [SerializeField] private int columns = 6;

    [SerializeField] private float cellWidth = 2.2f;

    [SerializeField] private float cellHeight = 3.2f;

    [Tooltip("Tallest the grid may run before CardGridView scales cells down to fit. 0 disables the "
             + "clamp. See CardRemovalPanel.maxGridHeight for the same reasoning.")]
    [SerializeField] private float maxGridHeight = 9f;

    [SerializeField] private float cardScale = 0.7f;

    [Tooltip("Multiplier on top of Card Scale while hovered - has to be above 1 or hovering shrinks "
             + "the card instead of popping it up.")]
    [SerializeField] private float cardHoverScale = 1.08f;

    [SerializeField] private Button closeButton;

    private readonly List<CardViewer> spawnedCards = new();

    public bool IsOpen { get; private set; }

    /// Starts hidden regardless of the scene's authored state - same reasoning as RewardPanel.Awake.
    protected override void Awake()
    {
        base.Awake();

        if (root != null) { root.SetActive(false); }

        if (closeButton != null) { closeButton.onClick.AddListener(Close); }
    }

    public void Show(IReadOnlyList<Card> cards, string title)
    {
        if (cardPrefab == null || gridAnchor == null || cards == null)
        {
            Debug.LogError($"{name}: cardPrefab, gridAnchor or cards not set - pile panel cannot show cards");
            return;
        }

        IsOpen = true;

        Clear();

        if (root != null) { root.SetActive(true); }

        if (titleLabel != null) { titleLabel.text = title ?? string.Empty; }

        // Purely informational - no onClick, so CardGridView leaves every viewer's clickOverride null.
        // CardViewer.OnMouseDown then falls through to CardPlayManager, which already ignores any card
        // ActiveHandViewer does not know about (see CardPlayManager.OnCardClicked).
        CardGridView.Build(
            cards, cardPrefab, gridAnchor, columns, cellWidth, cellHeight, cardScale, cardHoverScale,
            maxGridHeight, null, spawnedCards);

        // Same lock LootManager takes while a reward panel is up - stops the board, the hand and any
        // tooltip from responding underneath this modal.
        if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(true); }
    }

    public void Close()
    {
        if (!IsOpen) { return; }

        IsOpen = false;

        if (root != null) { root.SetActive(false); }

        Clear();

        if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(false); }
    }

    /// <summary>
    /// The frame on which Tab was used to close this panel, or -1.
    ///
    /// PartySheetPanel toggles on Tab, and Unity does not define which component's Update runs
    /// first, so without this one Tab press could close the pile AND open the party sheet. The two
    /// orderings are covered differently: if the sheet runs first its Show refuses outright, because
    /// this panel still holds InputLocked; if this one runs first the lock is already released by
    /// then, and this marker is what tells the sheet the press was spoken for.
    /// </summary>
    private static int tabConsumedFrame = -1;

    public static bool TabConsumedThisFrame => tabConsumedFrame == Time.frameCount;

    /// Tab closes the panel same as the button - a purely informational screen has nothing worth
    /// making the player hunt for a button to leave, unlike CardRemovalPanel's choice, which is
    /// deliberately Cancel-button-only.
    private void Update()
    {
        if (!IsOpen) { return; }

        if (Keyboard.current == null || !Keyboard.current.tabKey.wasPressedThisFrame) { return; }

        tabConsumedFrame = Time.frameCount;

        Close();
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
