using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using DG.Tweening;

public class CardViewer : MonoBehaviour
{
    [Tooltip("On the card's root. Makes every renderer under it sort as one unit, so two cards can " +
        "never interleave - without it the name and description (which are separate renderers) draw " +
        "over whatever card happens to be next to this one.")]
    [SerializeField] private SortingGroup sortingGroup;

    [SerializeField] private TMP_Text cardName;

    [SerializeField] private TMP_Text description;

    [SerializeField] private TMP_Text cost;

    [SerializeField] private SpriteRenderer image;

    [SerializeField] private SpriteRenderer rangeIndicator;

    [SerializeField] private Sprite bowIcon;

    [SerializeField] private Sprite swordIcon;

    [SerializeField] private float hoverScale = 1.5f;

    [SerializeField] private float hoverDuration = 0.1f;

    [Header("Tooltips")]
    [Tooltip("Explains this card's keywords on hover, and marks up the status names in its description " +
        "so they can be hovered individually. Optional - without it the card just has no tooltips.")]
    [SerializeField] private Glossary glossary;

    [Tooltip("On the description text. Watches for the cursor crossing one of the terms Glossary.Tag " +
        "marked up.")]
    [SerializeField] private TooltipLinkText descriptionLinks;

    [Tooltip("What the tooltip is positioned against. The root collider, so the box lines up with the " +
        "part of the card you can actually hit.")]
    [SerializeField] private Collider2D hitbox;



    /// <summary>
    /// Set by RewardPanel on a card it built for the choice screen. When set, a click goes here instead
    /// of CardPlayManager - a reward card is by definition not in ActiveHandViewer's hand, and
    /// CardPlayManager.OnCardClicked rejects any viewer that fails ActiveHandViewer.Contains.
    /// </summary>
    public System.Action<CardViewer> clickOverride;

    public Card card { get; private set; }

    public bool isSelected { get; private set; }

    public bool isHovered { get; private set; }

    /// True once the card has been played and is tweening away - hover must stop touching it.
    private bool isPlaying;

    /// This card's place in the hand, kept so hover can lift it into CardHover and put it back
    /// afterwards without having to ask the hand where it was.
    private int handOrder;

    // Where this card returns to when it is not hovered. A card in hand rests at scale 1 on the Cards
    // layer; a reward card rests larger, on Overlay, and stays there. Hover and hover-exit used to
    // hardcode the hand's answers to both, which is why a card presented anywhere but a hand snapped
    // back to hand size *and* off its own sorting layer the first time the cursor left it.
    private float restScale = 1f;

    private string restLayer = SortingLayers.Cards;

    private string hoverLayer = SortingLayers.CardHover;

    private int hoverOrder;

    /// Whether this card belongs to the on-screen hand. False for a reward card, which has no hand to
    /// ask for a re-space and must not poke one - see PresentAt.
    private bool inHand = true;

    // Each ever holds at most one live tween, killed by direct reference (never DOTween's id/target
    // search) before being replaced - so hover, selection, layout, spawn and discard can never stack
    // competing tweens on the same transform.
    private Tweener scaleTween;
    private Tweener positionTween;
    private Tweener rotationTween;

    private void Awake()
    {
        // The root collider is the thing OnMouseEnter already fires from, so an unassigned field means
        // the obvious answer rather than no tooltip at all.
        if (hitbox == null) { hitbox = GetComponent<Collider2D>(); }
    }

    public void Setup(Card newCard)
    {
        this.card = newCard;
        cardName.text = card.cardName;

        // Tagged, not printed raw. The card asset holds plain prose - "Apply Block 5 to yourself" - and
        // the glossary is what turns the words it recognises into hoverable, tinted links. Doing it here
        // rather than in the asset means a new glossary term lights up on every card that already
        // mentions it, and no asset can link to a term that does not exist.
        description.text = glossary != null ? glossary.Tag(card.description) : card.description;

        cost.text = card.cost.ToString();
        image.sprite = card.image;
        if (card.range.MaxDistance > 1)
        {
            rangeIndicator.sprite = bowIcon;
        } else if (card.range.MaxDistance == 1){
            rangeIndicator.sprite = swordIcon;
        } else
        {
            rangeIndicator.sprite = null;
        }
    }

    /// Grows the card from nothing - called once, when it is first dealt into a hand.
    public void PlaySpawnIn(float duration)
    {
        transform.localScale = Vector3.zero;
        scaleTween = transform.DOScale(restScale, duration);
    }

    /// <summary>
    /// Presents this card outside any hand - RewardPanel's choice screen is the only caller. Fixes
    /// where it rests (layer, order, scale) and how far hover lifts it, so the hover and hover-exit
    /// paths stop falling back to the hand's answers.
    ///
    /// hoverLayer is the rest layer rather than CardHover: a reward card already sits on Overlay, the
    /// top of the stack, and "lifting" it to CardHover would push it *behind* the reward cards next to
    /// it. The order bump is what lifts it instead.
    /// </summary>
    public void PresentAt(string layer, int order, float rest, float hover)
    {
        inHand = false;
        restLayer = layer;
        hoverLayer = layer;
        handOrder = order;
        hoverOrder = order + HoverOrderLift;
        restScale = rest;
        hoverScale = hover;

        transform.localScale = Vector3.one * rest;
        ApplySorting(restLayer, handOrder);
    }

    /// Clears a hovered card of its neighbours within its own layer. Larger than any plausible number
    /// of cards offered at once, so the hovered one always wins.
    private const int HoverOrderLift = 100;

    /// Where the hand layout wants this card right now. Called on every relayout, whether or not this
    /// particular card's target actually changed.
    public void SetLayoutTarget(Vector3 position, Quaternion rotation, float duration)
    {
        positionTween?.Kill();
        rotationTween?.Kill();
        positionTween = transform.DOMove(position, duration);
        rotationTween = transform.DORotate(rotation.eulerAngles, duration);
    }

    /// <summary>
    /// Where this card sits among the others in hand. Called by ActiveHandViewer on every relayout,
    /// right next to SetLayoutTarget, because the two have to agree: the sorting order decides what
    /// you see, and the per-index z nudge in the layout decides what a click actually hits. A card
    /// that drew on top but picked up clicks from the one behind it is the bug that pairing prevents.
    /// </summary>
    public void SetHandOrder(int index)
    {
        // A hand card needs no order lift: CardHover holds one card at a time, so there is nothing
        // beside it there to out-sort.
        handOrder = index;
        hoverOrder = index;

        // Deliberately not while hovered. A relayout fires whenever any card is drawn, discarded or
        // hovered, so without this a card drawn elsewhere would drop the hovered one back out of
        // CardHover mid-hover. OnMouseExit is the only thing that puts it back.
        if (isHovered) { return; }

        ApplySorting(restLayer, handOrder);
    }

    /// Flies the card to the discard anchor and shrinks it away.
    public void PlayDiscard(Vector3 target, float duration)
    {
        scaleTween?.Kill();
        positionTween?.Kill();
        rotationTween?.Kill();
        scaleTween = transform.DOScale(Vector3.zero, duration);
        positionTween = transform.DOMove(target, duration);
    }

    public void SetSelected(bool value)
    {
        isSelected = value;

        if (value)
        {
            // A card can be clicked without the mouse ever leaving it, which would never fire
            // OnMouseExit - so selection has to clear the hover pop itself. Same reasoning for the
            // sorting layer: without this the selected card stays stuck in CardHover, drawing over
            // the HUD for as long as it is held.
            isHovered = false;
            HideTooltips();
            ApplySorting(restLayer, handOrder);
            scaleTween?.Kill();
            scaleTween = transform.DOScale(restScale, hoverDuration);
        }
    }

    public void BeginPlay()
    {
        isPlaying = true;
        isSelected = false;
        isHovered = false;
        HideTooltips();
        scaleTween?.Kill();
        positionTween?.Kill();
        rotationTween?.Kill();

        // Clears isHovered above, so a card played straight out of a hover does not fly to the
        // discard pile still sitting in the CardHover layer, on top of the HUD.
        ApplySorting(restLayer, handOrder);
    }

    /// The one place the sorting group is written. Null-guarded because a prefab that has not had the
    /// group wired yet should cost you the layering, not throw on every hover.
    private void ApplySorting(string layer, int order)
    {
        if (sortingGroup == null) { return; }

        sortingGroup.sortingLayerName = layer;
        sortingGroup.sortingOrder = order;
    }

    /// <summary>
    /// Whether this card ignores the cursor right now.
    ///
    /// The last two clauses are hand-only, and deliberately so: both describe conditions that are true
    /// *because* a reward panel is up, and the offered cards on that panel are the one thing that still
    /// has to respond to hover. A hand card, meanwhile, must not enlarge under a modal it cannot be
    /// played through - BattleManager.InputLocked is the same gate OnTileClicked and OnCardClicked use.
    /// </summary>
    private bool HoverSuppressed
    {
        get
        {
            if (isPlaying || isSelected) { return true; }

            if (!inHand) { return false; }

            if (CardPlayManager.Instance != null && CardPlayManager.Instance.HasSelection) { return true; }

            return BattleManager.Instance != null && BattleManager.Instance.InputLocked;
        }
    }

    public void OnMouseEnter()
    {
        if (HoverSuppressed) { return; }
        isHovered = true;
        ShowTooltips();
        ApplySorting(hoverLayer, hoverOrder);
        scaleTween?.Kill();
        scaleTween = transform.DOScale(restScale * hoverScale, hoverDuration);
        RelayoutHand();
    }

    public void OnMouseExit()
    {
        if (isPlaying) { return; }
        isHovered = false;
        HideTooltips();
        ApplySorting(restLayer, handOrder);
        scaleTween?.Kill();
        scaleTween = transform.DOScale(restScale, hoverDuration);
        RelayoutHand();
    }

    /// Hovering a card in hand changes its footprint, so the hand re-spaces around it. A reward card
    /// has no hand to re-space and must not reach for one - ActiveHandViewer may not even exist in a
    /// scene that only shows a reward.
    private void RelayoutHand()
    {
        if (!inHand || ActiveHandViewer.Instance == null) { return; }

        StartCoroutine(ActiveHandViewer.Instance.Relayout());
    }

    /// <summary>
    /// The card's own keyword box, plus the watch for the cursor crossing a term in its description.
    ///
    /// Both at once, and they do not conflict: the keyword box is a TooltipPriority.Hovered request and
    /// a term inside the description is a Nested one, so hovering "Block" replaces the box while the
    /// keyword request quietly stays live underneath and reappears the moment the cursor moves off the
    /// word. See TooltipManager.
    /// </summary>
    private void ShowTooltips()
    {
        if (glossary == null || card == null) { return; }

        TooltipAnchor anchor = TooltipAnchor.Of(hitbox, TooltipSide.Right);

        if (TooltipManager.Instance != null)
        {
            TooltipManager.Instance.Show(this, KeywordTooltip(), anchor, TooltipPriority.Hovered);
        }

        if (descriptionLinks != null) { descriptionLinks.BeginPolling(glossary, anchor); }
    }

    /// <summary>
    /// Called from every way a hover can end, not just OnMouseExit.
    ///
    /// A card can be clicked or played without the cursor ever leaving it, which never fires
    /// OnMouseExit - the same reason SetSelected and BeginPlay already have to clear isHovered and put
    /// the sorting layer back by hand.
    /// </summary>
    private void HideTooltips()
    {
        if (TooltipManager.Instance != null) { TooltipManager.Instance.Hide(this); }

        if (descriptionLinks != null) { descriptionLinks.EndPolling(); }
    }

    /// What this card's keywords mean. Empty for a card with none, which the manager reads as "nothing
    /// to show" - so an ordinary card gets no box rather than an empty one.
    private TooltipContent KeywordTooltip()
    {
        TooltipContent content = new();

        foreach (CardKeyword keyword in card.Keywords)
        {
            glossary.KeywordContent(keyword.type, keyword.magnitude, content);
        }

        return content;
    }

    public void OnMouseDown()
    {
        if (clickOverride != null)
        {
            clickOverride(this);
            return;
        }

        if (CardPlayManager.Instance != null)
        {
            CardPlayManager.Instance.OnCardClicked(this);
        }
    }

}
