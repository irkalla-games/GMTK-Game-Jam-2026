using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The screen DiscardAction opens: every card currently in the acting character's hand, laid out in a
/// grid, click one to discard it. Built on the same CardGridView.Build + Resolved/ChosenIndex shape
/// CardRemovalPanel and CardPilePanel already share, but neither of those fits this job -
/// CardRemovalPanel resolves against the authored deck (CardData, not live Card instances) and offers a
/// Cancel out; CardPilePanel never resolves anything at all. A screen that commits to a choice among
/// live runtime cards is its own concept even though it borrows the grid - see CardRemovalPanel's own
/// doc comment for why that split is deliberate.
///
/// No cancel button: DiscardAction is the cost half of a card the player already chose to play, so
/// there is nothing to back out of once it is queued - mirrors DrawAction/SelfDamageAction having no
/// refusal of their own.
///
/// Takes real Card instances (Character.Hand), not CardData - the same reason CardPilePanel does: the
/// player is choosing among cards actually in hand, cooldown badges and all, not fresh copies built for
/// an offer screen.
/// </summary>
public class CardChoicePanel : Singleton<CardChoicePanel>
{
    [Tooltip("Backdrop root, toggled on Show/Hide.")]
    [SerializeField] private GameObject root;

    [Tooltip("\"Choose a card to discard\" - the caller passes the prompt per call.")]
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

    private readonly List<CardViewer> spawnedCards = new();

    /// True once the player has chosen a card. DiscardAction's WaitUntil polls this.
    public bool Resolved { get; private set; }

    /// Index into the hand passed to Show, valid only once Resolved is true.
    public int ChosenIndex { get; private set; } = -1;

    /// Starts hidden regardless of the scene's authored state - same reasoning as RewardPanel.Awake.
    protected override void Awake()
    {
        base.Awake();

        if (root != null) { root.SetActive(false); }
    }

    public void Show(IReadOnlyList<Card> hand, string prompt)
    {
        if (cardPrefab == null || gridAnchor == null || hand == null)
        {
            Debug.LogError($"{name}: cardPrefab, gridAnchor or hand not set - choice panel cannot show cards");
            return;
        }

        Resolved = false;
        ChosenIndex = -1;

        Clear();

        if (root != null) { root.SetActive(true); }

        if (titleLabel != null) { titleLabel.text = prompt ?? string.Empty; }

        CardGridView.Build(
            hand, cardPrefab, gridAnchor, columns, cellWidth, cellHeight, cardScale, cardHoverScale,
            maxGridHeight, Choose, spawnedCards);

        // Same lock every other modal card screen takes - stops the board, the hand and any tooltip
        // from responding underneath this one while DiscardAction's coroutine waits on Resolved.
        if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(true); }
    }

    private void Choose(int index)
    {
        if (Resolved) { return; }

        ChosenIndex = index;
        Resolved = true;
        Hide();
    }

    private void Hide()
    {
        if (root != null) { root.SetActive(false); }

        if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(false); }

        // Safe even though this may run from inside a just-clicked card's own OnMouseDown: Destroy is
        // deferred to end of frame, so the callback finishes on a still-live object. Same reasoning as
        // CardRemovalPanel.Hide.
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
