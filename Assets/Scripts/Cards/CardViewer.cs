using System.Collections.Generic;
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

    [Header("Area Icon")]
    [Tooltip("Where the area-footprint glyph sits, in the card's local space. Built at runtime rather "
        + "than placed on the prefab, so there is nothing to drag in the Editor - tune these two "
        + "fields instead to land it in whichever corner reads best against the card art.")]
    [SerializeField] private Vector3 areaIconLocalPosition = new(0.75f, 0.95f, 0f);

    [Tooltip("How wide the glyph sits on the card, in world units, regardless of how far the shape "
        + "itself actually reaches - a tight radius and a long cone both render at this same size.")]
    [SerializeField] private float areaIconSize = 0.35f;

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

    [Header("Playability")]
    [Tooltip("The card's frame - the CardBorder child. Turned green while this card can actually be " +
        "played. Optional: without it a card still greys out, it just gains no outline.")]
    [SerializeField] private SpriteRenderer border;

    [Tooltip("The frame's colour while this card is playable right now - feeds both the flat tint on " +
        "CardBorder itself and the enlarged ring behind it, so its alpha controls how intrusive both " +
        "read together. Kept translucent by default rather than a solid fill.")]
    [SerializeField] private Color playableBorderColor = new(0.45f, 1f, 0.55f, 0.5f);

    [Tooltip("A solid, non-hollow sprite for the backing ring - CardBackground's own sprite (White " +
        "Stop) is the natural choice, since it already matches the card's silhouette. CardBorder's " +
        "own sprite cannot be reused here the way it first was: it is a hollow frame graphic, and " +
        "enlarging a hollow shape draws a second, separate ring rather than thickening the first one.")]
    [SerializeField] private Sprite outlineSprite;

    [Tooltip("How much bigger than CardBorder the backing ring is, as a multiplier on its own scale - " +
        "1.2 means 20% bigger on both axes, growing evenly outward from CardBorder's own centre.")]
    [SerializeField] private float outlineScale = 1.2f;

    [Tooltip("How far an unplayable card is pushed toward grey. 0 leaves it fully coloured, 1 makes " +
        "it monochrome.")]
    [Range(0f, 1f)]
    [SerializeField] private float unplayableDesaturation = 0.75f;

    [Tooltip("How far an unplayable card is dimmed, after the desaturation above.")]
    [Range(0f, 1f)]
    [SerializeField] private float unplayableBrightness = 0.6f;

    [Tooltip("What an unplayable card's text fades to. Separate from the brightness above because " +
        "alpha is the only channel that reaches the glossary-tinted keywords in the description - a " +
        "<color> tag overrides the rest. Keep it high enough that the card stays readable.")]
    [Range(0f, 1f)]
    [SerializeField] private float unplayableTextAlpha = 0.55f;

    [Header("Timers")]
    [Tooltip("The turns-remaining badge behind lockCounter - shown whenever Cooldown or Dormant still " +
        "has this card locked. Optional: without it a locked card still greys out via SetPlayable, it " +
        "just does not say for how many more turns.")]
    [SerializeField] private SpriteRenderer lockBadge;

    [Tooltip("The number on the badge - Card.LockedTurns, whichever of Cooldown/Dormant is currently " +
        "longer. Excluded from the Awake grey-out scan below along with lockBadge, on purpose: this " +
        "is only ever visible on a card SetPlayable has already dimmed, and letting Dimmed() reach it " +
        "would grey out the one thing explaining why the card is grey.")]
    [SerializeField] private TMP_Text lockCounter;

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

    // Every renderer under the card, with the colour the prefab authored for it. Cached so the grey-out
    // is a transform of what was authored rather than a hard-coded palette - the same reason
    // TileSelector keeps an idleColor to return to instead of assuming white.
    private readonly List<SpriteRenderer> sprites = new();
    private readonly List<Color> spriteRestColors = new();
    private readonly List<TMP_Text> texts = new();
    private readonly List<Color> textRestColors = new();

    private bool isPlayable = true;

    /// False until SetPlayable has run once, so the first call always paints even though isPlayable
    /// already starts true. A card is dealt into a hand it may not be able to afford.
    private bool playabilityApplied;

    /// The enlarged, tinted copy of `border` sitting behind it - the ring SetPlayable turns on and
    /// off. Null whenever `border` was never wired up, same bargain every optional reference in this
    /// class strikes.
    private SpriteRenderer outlineRenderer;

    /// The area-footprint glyph - see BuildAreaIconRenderer. Always created, but its sprite stays
    /// null (drawing nothing) for a card whose entries are all Single.
    private SpriteRenderer areaIconRenderer;

    private void Awake()
    {
        // The root collider is the thing OnMouseEnter already fires from, so an unassigned field means
        // the obvious answer rather than no tooltip at all.
        if (hitbox == null) { hitbox = GetComponent<Collider2D>(); }

        // Found rather than serialized: a card is eleven renderers deep and listing them all in the
        // Inspector would mean a new child silently escaping the grey-out. The border is the one
        // exception - it needs naming, because it is the only one that gets its own colour.
        GetComponentsInChildren(true, sprites);
        foreach (SpriteRenderer sprite in sprites) { spriteRestColors.Add(sprite.color); }

        GetComponentsInChildren(true, texts);
        foreach (TMP_Text text in texts) { textRestColors.Add(text.color); }

        // lockBadge/lockCounter are the second exception, for the opposite reason to border: they
        // must only ever be visible on a card SetPlayable has already dimmed, so letting the grey-out
        // reach them would grey out the one thing explaining why the card is grey.
        RemoveFromGreyOut(lockBadge);
        RemoveFromGreyOut(lockCounter);

        BuildOutlineRenderer();
        BuildAreaIconRenderer();
    }

    /// <summary>
    /// Creates the area-footprint glyph as a runtime child, the same "built here, not on the prefab"
    /// choice BuildOutlineRenderer makes for the playable ring - there is then nothing on the card
    /// prefab that a future artist could accidentally leave stale.
    ///
    /// Added to the grey-out lists *after* the Awake scan above found everything the prefab authored,
    /// so it dims exactly like the card's own art on an unplayable card without ever being caught by
    /// RemoveFromGreyOut. Its rest colour is left at the SpriteRenderer default (opaque white) rather
    /// than set here - CardAreaIconBuilder already paints the shape's colour into the sprite's own
    /// pixels, the same way `image`'s art carries its own colour and this component's tint stays white.
    /// </summary>
    private void BuildAreaIconRenderer()
    {
        GameObject iconObject = new("CardAreaIcon", typeof(SpriteRenderer));
        iconObject.transform.SetParent(transform, false);
        iconObject.transform.localPosition = areaIconLocalPosition;

        areaIconRenderer = iconObject.GetComponent<SpriteRenderer>();

        // One layer above the highest order the prefab authored, so the glyph always draws on top of
        // the card art it summarizes rather than disappearing behind whichever renderer happens to
        // share its corner.
        int topOrder = 0;
        string layer = SortingLayers.Cards;

        foreach (SpriteRenderer sprite in sprites)
        {
            if (sprite == null) { continue; }

            topOrder = Mathf.Max(topOrder, sprite.sortingOrder);
            layer = sprite.sortingLayerName;
        }

        areaIconRenderer.sortingLayerName = layer;
        areaIconRenderer.sortingOrder = topOrder + 1;

        sprites.Add(areaIconRenderer);
        spriteRestColors.Add(areaIconRenderer.color);
    }

    /// Drops `renderer` out of the grey-out scan, keeping sprites/spriteRestColors paired by index.
    /// No-op if it was never wired or GetComponentsInChildren never found it.
    private void RemoveFromGreyOut(SpriteRenderer renderer)
    {
        if (renderer == null) { return; }

        int index = sprites.IndexOf(renderer);
        if (index < 0) { return; }

        sprites.RemoveAt(index);
        spriteRestColors.RemoveAt(index);
    }

    /// Same as above, for the text half.
    private void RemoveFromGreyOut(TMP_Text text)
    {
        if (text == null) { return; }

        int index = texts.IndexOf(text);
        if (index < 0) { return; }

        texts.RemoveAt(index);
        textRestColors.RemoveAt(index);
    }

    /// <summary>
    /// Builds the backing ring once, as a sibling of `border` sitting behind everything on the card -
    /// the same "bigger copy of the same shape, further back" trick CharacterOutline uses for a
    /// health bar. Uses outlineSprite rather than border's own sprite on purpose: CardBorder is
    /// itself a hollow frame graphic (that hollowness is what makes tinting it read as a border in
    /// the first place), so an enlarged copy of it is a second, separate ring rather than a thicker
    /// version of the first - the same mistake would repeat with any other hollow shape. A solid
    /// sprite has no inner edge to draw a second ring from; only its one outer edge ever shows.
    ///
    /// A pure uniform scale multiplier rather than a fixed-unit padding: CardBorder's own scale is
    /// non-uniform (its rectangle isn't square), and multiplying both axes by the same factor grows
    /// it proportionally without distorting the shape or needing any per-axis correction - there is
    /// no shader here trying to reconstruct a world-unit thickness, so there is nothing for a
    /// non-uniform scale to trip up.
    /// </summary>
    private void BuildOutlineRenderer()
    {
        if (border == null || outlineSprite == null) { return; }

        GameObject outlineObject = new("CardOutlineRing", typeof(SpriteRenderer));
        Transform outlineTransform = outlineObject.transform;

        outlineTransform.SetParent(border.transform.parent, false);
        outlineTransform.SetLocalPositionAndRotation(border.transform.localPosition, border.transform.localRotation);
        outlineTransform.localScale = border.transform.localScale * outlineScale;

        outlineRenderer = outlineObject.GetComponent<SpriteRenderer>();
        outlineRenderer.sprite = outlineSprite;
        outlineRenderer.sortingLayerID = border.sortingLayerID;

        // Below every other renderer on the card (the lowest authored order is CardBackground's 0),
        // so only the margin this scale-up grows past CardBorder's own edge is ever visible - the
        // rest sits behind CardBackground, fully hidden, same as BarBack hides HealthBarOutline's
        // own body on a character.
        outlineRenderer.sortingOrder = -1;
        outlineRenderer.color = new Color(1f, 1f, 1f, 0f);
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
        if (card.range.IsRanged)
        {
            rangeIndicator.sprite = bowIcon;
        } else if (card.range.MaxDistance == 1){
            rangeIndicator.sprite = swordIcon;
        } else
        {
            rangeIndicator.sprite = null;
        }

        if (areaIconRenderer != null)
        {
            Sprite icon = card.AreaIcon();
            areaIconRenderer.sprite = icon;
            ApplyAreaIconScale(icon);
        }
    }

    /// A footprint glyph is built at a fixed pixels-per-cell, so its native size grows with how far
    /// the shape reaches - a long cone would otherwise dwarf a tight blast on the card face. Scaling
    /// by the sprite's own bounds, rather than a hardcoded factor per AreaKind, is what keeps every
    /// shape landing at the same areaIconSize regardless of how CardAreaIconBuilder happened to size
    /// its texture.
    private void ApplyAreaIconScale(Sprite icon)
    {
        if (icon == null) { return; }

        float nativeSize = icon.bounds.size.x;
        float scale = nativeSize > 0f ? areaIconSize / nativeSize : 1f;

        areaIconRenderer.transform.localScale = Vector3.one * scale;
    }

    /// <summary>
    /// Whether this card can be played right now, said in colour: a green frame when it can, the whole
    /// card pushed grey when it cannot.
    ///
    /// The one place the card's colour is written, so nothing else has to know what a dimmed card
    /// looks like - the same bargain TileSelector.ApplyColor strikes for a tile. ActiveHandViewer is
    /// the only caller, and it answers from Card.PlayRefusal, the same predicate the click is gated
    /// on. A reward card is never told either way and simply keeps the colours the prefab authored:
    /// nobody owns it yet, so "can you afford it" has no answer.
    /// </summary>
    public void SetPlayable(bool value)
    {
        if (playabilityApplied && isPlayable == value) { return; }

        isPlayable = value;
        playabilityApplied = true;

        for (int i = 0; i < sprites.Count; i++)
        {
            if (sprites[i] == null) { continue; }

            sprites[i].color = value ? spriteRestColors[i] : Dimmed(spriteRestColors[i]);
        }

        for (int i = 0; i < texts.Count; i++)
        {
            if (texts[i] == null) { continue; }

            if (value) { texts[i].color = textRestColors[i]; continue; }

            // The alpha here is load-bearing, not decoration. The description is glossary-tagged, and
            // a <color> tag overrides the RGB this sets - so the grey alone never reaches the tinted
            // keywords. TMP does cap a tag's alpha at the base colour's, which is the one channel
            // that still gets through; without it a greyed-out card keeps a row of bright links
            // across its middle.
            Color dimmed = Dimmed(textRestColors[i]);
            dimmed.a = textRestColors[i].a * unplayableTextAlpha;
            texts[i].color = dimmed;
        }

        // After the loop above, which has just painted the border grey along with everything else.
        if (border != null && value) { border.color = playableBorderColor; }

        ApplyOutline(value);
    }

    /// <summary>
    /// The enlarged ring behind `border` - additive to the flat tint above, not a replacement for it.
    /// Both use playableBorderColor, so together they read as one thicker margin; turning the ring
    /// off when unplayable leaves border's own colour to fall through to Dimmed() exactly as it did
    /// before this existed. Alpha doubles as the switch, same as CharacterOutline's health-bar ring -
    /// a fully transparent copy is indistinguishable from no ring at all.
    /// </summary>
    private void ApplyOutline(bool value)
    {
        if (outlineRenderer == null) { return; }

        outlineRenderer.color = value ? playableBorderColor : new Color(1f, 1f, 1f, 0f);
    }

    /// <summary>
    /// Shows or hides the turns-remaining badge and updates its number from Card.LockedTurns -
    /// whichever of Cooldown/Dormant is currently longer. Kept separate from SetPlayable rather than
    /// folded into it: SetPlayable early-returns once isPlayable stops moving, but a locked card's
    /// countdown keeps changing turn over turn while it stays unplayable the whole time. Called by
    /// ActiveHandViewer alongside every SetPlayable call, so the two can never disagree.
    /// </summary>
    public void RefreshLockCounter()
    {
        if (card == null) { return; }

        int turns = card.LockedTurns;

        if (lockBadge != null) { lockBadge.enabled = turns > 0; }

        if (lockCounter != null)
        {
            lockCounter.enabled = turns > 0;
            lockCounter.text = turns.ToString();
        }
    }

    /// Pushes a colour toward grey and then darkens it, leaving alpha alone except for the text dim.
    /// Two steps rather than one multiply because a flat multiply only darkens - a bright red cost
    /// pip stays a bright red pip, and the card reads as "in shadow" rather than "unavailable".
    private Color Dimmed(Color color)
    {
        float grey = color.grayscale;
        Color desaturated = Color.Lerp(color, new Color(grey, grey, grey, color.a), unplayableDesaturation);

        return new Color(
            desaturated.r * unplayableBrightness,
            desaturated.g * unplayableBrightness,
            desaturated.b * unplayableBrightness,
            color.a);
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
