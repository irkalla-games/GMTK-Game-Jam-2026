using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The choice screen LootManager shows: real CardViewers rendering the offered CardData, plus one
/// button per SkipReward. Purely a view - it records the outcome for LootManager to read and apply; it
/// decides nothing about what a card or a skip actually does.
/// </summary>
public class RewardPanel : MonoBehaviour
{
    [Tooltip("Backdrop root, toggled on Show/Hide. Optional - a panel with no backdrop just has no dim.")]
    [SerializeField] private GameObject root;

    [Tooltip("Same prefab hands are built from, so a reward card looks exactly like a card in hand.")]
    [SerializeField] private CardViewer cardPrefab;

    [Tooltip("Where offered cards are centred. A world position, not a Transform parent - CardViewer "
             + "is a plain world-space sprite object like every card in hand, never parented under UI.")]
    [SerializeField] private Transform cardAnchor;

    [Tooltip("Gap between offered cards. Layout is width-driven off however many are offered, not a "
             + "fixed count - LootTable.ChoiceCount can differ per table.")]
    [SerializeField] private float cardSpacing = 2.2f;

    [Tooltip("How large an offered card sits on the choice screen. 1 is the size of a card in hand.")]
    [SerializeField] private float cardScale = 1.6f;

    [Tooltip("Multiplier on top of Card Scale while hovered. Small on purpose - an offered card is "
             + "already presented large, so the hand's 1.5x lift would be far too much here.")]
    [SerializeField] private float cardHoverScale = 1.08f;

    [SerializeField] private Button skipButtonPrefab;

    [SerializeField] private Transform skipButtonParent;

    private readonly List<CardViewer> spawnedCards = new();

    private readonly List<Button> spawnedButtons = new();

    /// True once the player has chosen a card or a skip reward. LootManager's WaitUntil polls this.
    public bool Resolved { get; private set; }

    /// The card chosen, or null if a skip was chosen (or nothing has resolved yet).
    public CardData ChosenCard { get; private set; }

    /// The skip reward chosen, or null if a card was chosen instead.
    public SkipReward ChosenSkip { get; private set; }

    /// <summary>
    /// Starts hidden regardless of how the scene left `root`'s active checkbox - same reasoning as
    /// NotificationManager.Awake forcing its panel inactive. Authoring `root` as active by default (the
    /// natural state while building the hierarchy in the Editor) would otherwise dim the whole battle
    /// from the first frame, since nothing else calls Hide until a reward has actually resolved once.
    /// </summary>
    private void Awake()
    {
        if (root != null) { root.SetActive(false); }
    }

    public void Show(List<CardData> candidates, List<SkipReward> skipRewards)
    {
        Resolved = false;
        ChosenCard = null;
        ChosenSkip = null;

        Clear();

        if (root != null) { root.SetActive(true); }

        SpawnCards(candidates);
        SpawnSkipButtons(skipRewards);
    }

    private void SpawnCards(List<CardData> candidates)
    {
        if (cardPrefab == null || cardAnchor == null)
        {
            Debug.LogError($"{name}: cardPrefab or cardAnchor not set - reward panel cannot show cards");
            return;
        }

        float startX = -(candidates.Count - 1) * cardSpacing / 2f;

        for (int i = 0; i < candidates.Count; i++)
        {
            CardData data = candidates[i];

            if (data == null) { continue; }

            Vector3 position = cardAnchor.position + new Vector3(startX + i * cardSpacing, 0f, 0f);
            CardViewer viewer = Instantiate(cardPrefab, position, Quaternion.identity);

            viewer.Setup(new Card(data));

            // Overlay is above the notification modal's own layer, since a reward can in principle be
            // offered while one is still up (an EnemyResolve hero-shove is not gated on
            // NotificationManager). PresentAt rather than a bare sorting write: it also pins the rest
            // scale and hover lift, so leaving a card does not snap it back to hand size and the
            // Cards layer.
            viewer.PresentAt(SortingLayers.Overlay, i, cardScale, cardHoverScale);

            // Captured per-iteration on purpose - `data` is reassigned every loop, `chosen` is not.
            CardData chosen = data;
            viewer.clickOverride = _ => Choose(chosen);

            spawnedCards.Add(viewer);
        }
    }

    private void SpawnSkipButtons(List<SkipReward> skipRewards)
    {
        if (skipButtonPrefab == null || skipButtonParent == null || skipRewards == null) { return; }

        foreach (SkipReward reward in skipRewards)
        {
            if (reward == null) { continue; }

            Button button = Instantiate(skipButtonPrefab, skipButtonParent);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();

            if (label != null) { label.text = reward.Label; }

            SkipReward chosen = reward;
            button.onClick.AddListener(() => ChooseSkip(chosen));

            spawnedButtons.Add(button);
        }
    }

    private void Choose(CardData data)
    {
        if (Resolved) { return; }

        ChosenCard = data;
        Resolved = true;
        Hide();
    }

    private void ChooseSkip(SkipReward reward)
    {
        if (Resolved) { return; }

        ChosenSkip = reward;
        Resolved = true;
        Hide();
    }

    private void Hide()
    {
        if (root != null) { root.SetActive(false); }

        // Safe even though this runs from inside a just-clicked card's own OnMouseDown: Destroy is
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

        foreach (Button button in spawnedButtons)
        {
            if (button != null) { Destroy(button.gameObject); }
        }

        spawnedButtons.Clear();
    }
}
