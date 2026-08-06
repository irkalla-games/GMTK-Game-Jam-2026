using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The one hover popup, shared by everything that wants to explain itself.
///
/// Callers push a request - some TooltipContent, a TooltipAnchor and a priority - and drop it again
/// when the cursor leaves. They never touch the box itself, so nothing but this class knows what a
/// tooltip looks like or where it is allowed to sit.
///
/// **Requests are a list, not a single slot.** That is the whole reason hovering a card and then
/// hovering the word "Block" inside its description works: the card's keyword request stays live at
/// priority 0 the entire time, the link's request arrives at priority 10 and wins, and when the cursor
/// leaves the word its request is dropped and the card's reappears by itself. Neither caller knows the
/// other exists, and neither has to re-show anything. A single slot would need CardViewer and
/// TooltipLinkText to negotiate, which is exactly the coupling this avoids.
///
/// Screen-space uGUI even though half its callers are world-space sprites. One implementation serves
/// both because TooltipAnchor does the camera projection, and the canvas sits on the Overlay sorting
/// layer, which is already above CardHover - so the hovered card can never draw over its own tooltip.
/// </summary>
public class TooltipManager : Singleton<TooltipManager>
{
    [Tooltip("The canvas this box lives on. Positioning is done in its local space, so the panel must " +
        "be a direct child of it.")]
    [SerializeField] private Canvas canvas;

    [Tooltip("The box. Its width is whatever the prefab says; its height is driven by a " +
        "ContentSizeFitter from the text inside.")]
    [SerializeField] private RectTransform panel;

    [Tooltip("Fades the box in after showDelay. Alpha rather than SetActive so the layout has already " +
        "settled - a panel activated and positioned in the same frame shows up at the wrong size for " +
        "one frame.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [SerializeField] private TMP_Text text;

    [Header("Style")]
    [SerializeField] private Color titleColor = new(1f, 0.83f, 0.48f);

    [SerializeField] private Color bodyColor = new(0.88f, 0.9f, 0.94f);

    [Tooltip("Gap between the box and the thing it is explaining.")]
    [SerializeField] private float gap = 12f;

    [Tooltip("Closest the box may get to the edge of the screen before it is pushed back inside.")]
    [SerializeField] private float edgePadding = 16f;

    [Tooltip("How long the cursor must rest before the box appears. Only paid once - moving from one " +
        "hovered thing straight to another swaps the text immediately rather than fading out and back.")]
    [SerializeField] private float showDelay = 0.1f;

    /// <summary>
    /// One live ask for the box. `sequence` breaks ties at equal priority in favour of the newer
    /// request, so two things claiming the same priority behave predictably instead of depending on
    /// list order.
    /// </summary>
    private struct Request
    {
        public object owner;
        public TooltipContent content;
        public TooltipAnchor anchor;
        public int priority;
        public int sequence;
    }

    private readonly List<Request> requests = new();

    private int nextSequence;

    /// Who is currently rendered, so the text is only rebuilt when the winning request actually
    /// changes rather than every frame.
    private object shownOwner;

    private bool contentDirty;

    private float delayElapsed;

    private bool faded;

    /// Cached because TryResolve asks for it every frame a world-space tooltip is up, and Camera.main
    /// is a scene search.
    private Camera worldCamera;

    private static readonly StringBuilder Builder = new();

    protected override void Awake()
    {
        base.Awake();

        // Log and degrade rather than throw, the same as ActiveHandViewer.Start - a half-wired
        // tooltip should cost you the tooltip, not every hover in the game.
        if (canvas == null || panel == null || text == null)
        {
            Debug.LogError($"{name}: TooltipManager is missing its canvas, panel or text - no tooltips " +
                "will show");
            enabled = false;
            return;
        }

        SetAlpha(0f);
    }

    /// <summary>
    /// Ask for the box. Re-showing with the same owner replaces that owner's request rather than
    /// stacking a second one, so a caller whose numbers changed just calls this again.
    /// </summary>
    /// <param name="owner">Whatever is doing the hovering. Its identity is the handle Hide uses.</param>
    /// <param name="priority">Higher wins. Anything layered on top of another hover - a linked word
    /// inside a card that is itself hovered - goes above it.</param>
    public void Show(object owner, TooltipContent content, TooltipAnchor anchor, int priority = 0)
    {
        if (owner == null || content == null || content.IsEmpty)
        {
            // An empty content is a caller saying "I have nothing to explain" - treated as dropping the
            // request, so a card with no keywords does not have to test for that itself.
            Hide(owner);
            return;
        }

        Request request = new()
        {
            owner = owner,
            content = content,
            anchor = anchor,
            priority = priority,
            sequence = nextSequence++,
        };

        int existing = IndexOf(owner);

        if (existing >= 0) { requests[existing] = request; }
        else { requests.Add(request); }

        contentDirty = true;
    }

    /// Drops this owner's request. Safe to call for an owner that never showed anything, which is what
    /// lets every exit path - OnMouseExit, SetSelected, BeginPlay - call it unconditionally.
    public void Hide(object owner)
    {
        int index = IndexOf(owner);

        if (index < 0) { return; }

        requests.RemoveAt(index);
        contentDirty = true;
    }

    private int IndexOf(object owner)
    {
        for (int i = 0; i < requests.Count; i++)
        {
            // ReferenceEquals, not ==. Owners are arbitrary objects and some are MonoBehaviours, whose
            // overloaded == would report a destroyed one as equal to a null owner and remove the
            // wrong request.
            if (ReferenceEquals(requests[i].owner, owner)) { return i; }
        }

        return -1;
    }

    /// LateUpdate so the anchors have finished moving for this frame - a card's hover tween and the
    /// status panel's resize both run in Update, and reading them earlier trails them by a frame.
    private void LateUpdate()
    {
        if (requests.Count == 0)
        {
            Dismiss();
            return;
        }

        Request top = Top();

        if (contentDirty || !ReferenceEquals(top.owner, shownOwner))
        {
            Render(top.content);
            shownOwner = top.owner;
            contentDirty = false;
        }

        if (!top.anchor.TryResolve(WorldCamera(), out Rect anchorRect))
        {
            // The anchored thing has been destroyed - a card played out from under the cursor. Drop the
            // request rather than leaving a box pointing at nothing.
            Hide(top.owner);
            return;
        }

        Position(anchorRect, top.anchor.side);

        if (faded) { return; }

        delayElapsed += Time.unscaledDeltaTime;

        if (delayElapsed < showDelay) { return; }

        faded = true;
        SetAlpha(1f);
    }

    /// Highest priority, newest wins a tie.
    private Request Top()
    {
        Request best = requests[0];

        for (int i = 1; i < requests.Count; i++)
        {
            Request candidate = requests[i];

            bool better = candidate.priority > best.priority
                || (candidate.priority == best.priority && candidate.sequence > best.sequence);

            if (better) { best = candidate; }
        }

        return best;
    }

    private void Dismiss()
    {
        shownOwner = null;
        delayElapsed = 0f;

        if (!faded) { return; }

        faded = false;
        SetAlpha(0f);
    }

    /// <summary>
    /// Entries into one rich-text string, rather than one child object per entry.
    ///
    /// A tooltip is a paragraph or three of text; giving each entry its own prefab and pooling them
    /// would be a layout group, a ContentSizeFitter per row and a pool to maintain in exchange for
    /// nothing TMP cannot already do with a bold line and a blank one.
    /// </summary>
    private void Render(TooltipContent content)
    {
        string titleHex = ColorUtility.ToHtmlStringRGB(titleColor);
        string bodyHex = ColorUtility.ToHtmlStringRGB(bodyColor);

        Builder.Clear();

        for (int i = 0; i < content.Entries.Count; i++)
        {
            TooltipEntry entry = content.Entries[i];

            if (i > 0) { Builder.Append("\n\n"); }

            if (!string.IsNullOrWhiteSpace(entry.title))
            {
                Builder.Append("<b><color=#").Append(titleHex).Append('>')
                    .Append(entry.title)
                    .Append("</color></b>\n");
            }

            Builder.Append("<color=#").Append(bodyHex).Append('>')
                .Append(entry.body)
                .Append("</color>");
        }

        text.text = Builder.ToString();

        // Forced now, not next frame: the box is about to be positioned from its own height, and a
        // ContentSizeFitter that has not run yet still reports the previous entry's size.
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
    }

    /// <summary>
    /// Places the box beside the anchor, flipping to the opposite side when the preferred one would
    /// leave the screen and then clamping whatever is left.
    ///
    /// Both steps are needed: flipping alone still lets a tall box overhang the top, and clamping alone
    /// would slide a card's tooltip over the card it is describing instead of moving it to the other
    /// side.
    /// </summary>
    private void Position(Rect anchorScreenRect, TooltipSide side)
    {
        RectTransform canvasRect = (RectTransform)canvas.transform;
        Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, anchorScreenRect.min, canvasCamera, out Vector2 min);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, anchorScreenRect.max, canvasCamera, out Vector2 max);

        Rect local = Rect.MinMaxRect(
            Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
            Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));

        Vector2 size = panel.rect.size;
        Rect area = canvasRect.rect;

        Vector2 position = Place(local, side, size, gap);

        if (!Fits(position, size, area)) { position = Place(local, Opposite(side), size, gap); }

        position.x = Mathf.Clamp(position.x, area.xMin + size.x * 0.5f + edgePadding,
            area.xMax - size.x * 0.5f - edgePadding);
        position.y = Mathf.Clamp(position.y, area.yMin + size.y * 0.5f + edgePadding,
            area.yMax - size.y * 0.5f - edgePadding);

        // localPosition, not anchoredPosition. Everything above is in the canvas rect's local space,
        // which is what localPosition takes; anchoredPosition is measured from the panel's own anchors
        // and would be offset by wherever those happen to sit.
        panel.localPosition = new Vector3(position.x, position.y, panel.localPosition.z);
    }

    /// The box's centre for this side. Centred on the anchor along the free axis - which for the
    /// character panel puts it squarely over the panel, and for a card puts it level with the card.
    private static Vector2 Place(Rect anchor, TooltipSide side, Vector2 size, float gap)
    {
        return side switch
        {
            TooltipSide.Below => new Vector2(anchor.center.x, anchor.yMin - gap - size.y * 0.5f),
            TooltipSide.Right => new Vector2(anchor.xMax + gap + size.x * 0.5f, anchor.center.y),
            TooltipSide.Left => new Vector2(anchor.xMin - gap - size.x * 0.5f, anchor.center.y),
            _ => new Vector2(anchor.center.x, anchor.yMax + gap + size.y * 0.5f),
        };
    }

    private static bool Fits(Vector2 centre, Vector2 size, Rect area)
    {
        return centre.x - size.x * 0.5f >= area.xMin
            && centre.x + size.x * 0.5f <= area.xMax
            && centre.y - size.y * 0.5f >= area.yMin
            && centre.y + size.y * 0.5f <= area.yMax;
    }

    private static TooltipSide Opposite(TooltipSide side)
    {
        return side switch
        {
            TooltipSide.Above => TooltipSide.Below,
            TooltipSide.Below => TooltipSide.Above,
            TooltipSide.Right => TooltipSide.Left,
            _ => TooltipSide.Right,
        };
    }

    private Camera WorldCamera()
    {
        if (worldCamera == null) { worldCamera = Camera.main; }

        return worldCamera;
    }

    private void SetAlpha(float alpha)
    {
        if (canvasGroup != null) { canvasGroup.alpha = alpha; }
        else { panel.gameObject.SetActive(alpha > 0f); }
    }
}

/// <summary>
/// The layers a tooltip request can claim. Named rather than passed as bare ints so the one rule that
/// matters - a word inside a hovered thing outranks the hovered thing itself - is written down
/// somewhere instead of being two magic numbers in two files.
/// </summary>
public static class TooltipPriority
{
    /// A whole object under the cursor: a card, a status chip, a character.
    public const int Hovered = 0;

    /// Something inside an already-hovered object: a glossary term in a card's description.
    public const int Nested = 10;
}
