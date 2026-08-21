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

    [Tooltip("Shows who the offer is for, e.g. \"Knight's reward\". Optional - a mid-battle pickup "
             + "passes no title since only one character can be standing on the tile.")]
    [SerializeField] private TMP_Text titleLabel;

    [Tooltip("Same prefab hands are built from, so a reward card looks exactly like a card in hand.")]
    [SerializeField] private CardViewer cardPrefab;

    [Tooltip("Where offered cards are centred. A world position, not a Transform parent - CardViewer "
             + "is a plain world-space sprite object like every card in hand, never parented under UI.")]
    [SerializeField] private Transform cardAnchor;

    [Tooltip("Gap between offered cards. Layout is width-driven off however many are offered, not a "
             + "fixed count - LootTable.ChoiceCount can differ per table.")]
    [SerializeField] private float cardSpacing = 2.2f;

    [Tooltip("Total width the offered cards may span before spacing is squeezed - a level-clear offer "
             + "of 5 would otherwise run off CameraFrame's fixed 19.2-unit frame at the pickup offer's "
             + "3-card spacing. 3 cards well under this stay at cardSpacing; more get compressed. "
             + "0 disables the clamp entirely.")]
    [SerializeField] private float maxWidth = 15f;

    [Tooltip("How large an offered card sits on the choice screen. 1 is the size of a card in hand.")]
    [SerializeField] private float cardScale = 1.6f;

    [Tooltip("Multiplier on top of Card Scale while hovered. Small on purpose - an offered card is "
             + "already presented large, so the hand's 1.5x lift would be far too much here.")]
    [SerializeField] private float cardHoverScale = 1.08f;

    [SerializeField] private Button skipButtonPrefab;

    [SerializeField] private Transform skipButtonParent;

    [Header("Equipment offer")]
    [Tooltip("Spawned for each equipment choice - the equipment counterpart to cardPrefab. Plain UGUI, "
             + "unlike cardPrefab's world-space CardViewer, since equipment has no board presence.")]
    [SerializeField] private EquipmentViewer equipmentPrefab;

    [Tooltip("Where equipment tiles are laid out - a RectTransform, since EquipmentViewer is UGUI and "
             + "positions with anchoredPosition rather than the world position cardAnchor supplies.")]
    [SerializeField] private RectTransform equipmentAnchor;

    [SerializeField] private float equipmentSpacing = 340f;

    private readonly List<CardViewer> spawnedCards = new();

    private readonly List<EquipmentViewer> spawnedEquipment = new();

    private readonly List<Button> spawnedButtons = new();

    /// True once the player has chosen a card, an item, or a skip reward. LootManager's WaitUntil polls
    /// this.
    public bool Resolved { get; private set; }

    /// The card chosen, or null if something else was chosen (or nothing has resolved yet).
    public CardData ChosenCard { get; private set; }

    /// The equipment chosen, or null if something else was chosen. Show and ShowEquipment are mutually
    /// exclusive per call - only one of ChosenCard/ChosenEquipment is ever set for a given resolution.
    public EquipmentData ChosenEquipment { get; private set; }

    /// The skip reward chosen, or null if a card or item was chosen instead.
    public SkipReward ChosenSkip { get; private set; }

    /// <summary>
    /// Whether the panel is actually on screen right now - not the same question LootManager.IsIdle
    /// answers. IsIdle flips false as soon as a pickup is queued (GridTile.TryPickUpItem, called
    /// synchronously from GridManager.MoveCharacter), well before LootManager.Drain finishes waiting on
    /// ActionManager.IsIdle and actually calls Show below. A caller that needs to know the panel itself
    /// is up - the tutorial's own explanation of it - has to ask this, not IsIdle.
    /// </summary>
    public bool IsShowing => root != null && root.activeSelf;

    /// <summary>
    /// Exposed for the tutorial, which anchors its own explanation above the whole panel rather than
    /// above the title specifically - see TutorialDirector. `root` stretches the full screen, so
    /// AnchoredPlacement can never fit a box strictly "above" or "below" it; the clamp it falls back to
    /// then pins the box against the top edge, which is what actually keeps it clear of the offered
    /// cards. Anchoring to titleLabel instead would only line the box up beside a fixed-size rect the
    /// cards sit close beneath, and a small change to either one could push it back over them.
    /// </summary>
    public RectTransform Rect => root != null ? (RectTransform)root.transform : null;

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

    public void Show(List<CardData> candidates, List<SkipReward> skipRewards, string title = null)
    {
        Resolved = false;
        ChosenCard = null;
        ChosenEquipment = null;
        ChosenSkip = null;

        Clear();

        if (root != null) { root.SetActive(true); }

        if (titleLabel != null) { titleLabel.text = title ?? string.Empty; }

        SpawnCards(candidates);
        SpawnSkipButtons(skipRewards);
    }

    /// <summary>
    /// The equipment counterpart to Show - same lifecycle (Resolved/Hide/skip buttons), a row of
    /// EquipmentViewers instead of CardViewers. LootManager calls this instead of Show when a loot
    /// roll comes up equipment, replacing the whole panel's offer rather than mixing the two in one row
    /// - see LootManager.Drain/OfferLevelClear.
    /// </summary>
    public void ShowEquipment(List<EquipmentData> candidates, List<SkipReward> skipRewards, string title = null)
    {
        Resolved = false;
        ChosenCard = null;
        ChosenEquipment = null;
        ChosenSkip = null;

        Clear();

        if (root != null) { root.SetActive(true); }

        if (titleLabel != null) { titleLabel.text = title ?? string.Empty; }

        SpawnEquipment(candidates);
        SpawnSkipButtons(skipRewards);
    }

    private void SpawnCards(List<CardData> candidates)
    {
        if (cardPrefab == null || cardAnchor == null)
        {
            Debug.LogError($"{name}: cardPrefab or cardAnchor not set - reward panel cannot show cards");
            return;
        }

        // 3 cards at the authored spacing is unaffected; a level-clear offer of 5 would otherwise run
        // past CameraFrame's fixed frame - see maxWidth's tooltip.
        //
        // maxWidth <= 0 means no clamp, and has to: this panel was authored in Game.unity before the
        // field existed, so it deserializes to 0 there, and treating that as a real width would divide
        // the spacing down to nothing and stack every card on the same spot. Same hazard as
        // RangeShape.Anywhere being 0 - the zero value must be the old behaviour.
        float spacing = maxWidth > 0f && candidates.Count > 1
            ? Mathf.Min(cardSpacing, maxWidth / (candidates.Count - 1))
            : cardSpacing;

        float startX = -(candidates.Count - 1) * spacing / 2f;

        for (int i = 0; i < candidates.Count; i++)
        {
            CardData data = candidates[i];

            if (data == null) { continue; }

            Vector3 position = cardAnchor.position + new Vector3(startX + i * spacing, 0f, 0f);

            // Captured per-iteration on purpose - `data` is reassigned every loop, `chosen` is not.
            CardData chosen = data;

            // Overlay is above the notification modal's own layer, since a reward can in principle be
            // offered while one is still up (an EnemyResolve hero-shove is not gated on
            // NotificationManager).
            CardViewer viewer = OfferedCard.Spawn(
                cardPrefab, data, position, i, cardScale, cardHoverScale, _ => Choose(chosen));

            spawnedCards.Add(viewer);
        }
    }

    private void SpawnEquipment(List<EquipmentData> candidates)
    {
        if (equipmentPrefab == null || equipmentAnchor == null)
        {
            Debug.LogError($"{name}: equipmentPrefab or equipmentAnchor not set - reward panel cannot show equipment");
            return;
        }

        float startX = -(candidates.Count - 1) * equipmentSpacing / 2f;

        for (int i = 0; i < candidates.Count; i++)
        {
            EquipmentData item = candidates[i];

            if (item == null) { continue; }

            EquipmentViewer viewer = Instantiate(equipmentPrefab, equipmentAnchor);
            RectTransform rect = viewer.GetComponent<RectTransform>();

            if (rect != null) { rect.anchoredPosition = new Vector2(startX + i * equipmentSpacing, 0f); }

            viewer.Setup(item, ChooseEquipment);

            spawnedEquipment.Add(viewer);
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

    private void ChooseEquipment(EquipmentData item)
    {
        if (Resolved) { return; }

        ChosenEquipment = item;
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

        foreach (EquipmentViewer viewer in spawnedEquipment)
        {
            if (viewer != null) { Destroy(viewer.gameObject); }
        }

        spawnedEquipment.Clear();

        foreach (Button button in spawnedButtons)
        {
            if (button != null) { Destroy(button.gameObject); }
        }

        spawnedButtons.Clear();
    }
}
