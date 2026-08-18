using System.Collections.Generic;
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
///
/// **The chrome (header bar, outline, ruled sections) is a fixed scene skeleton plus runtime-pooled
/// rows**, not a single rich-text string. `panel`/`panelFill`/`headerRoot`/`headerRule`/`body` are
/// built once by Tools > UI > Wire Tooltip Panel; everything inside `body` - one TooltipSectionView
/// per TooltipContent.Section - is built and pooled at runtime by Render, since a section's row mix
/// (three stats and a figure today, a run of terms tomorrow) is exactly the kind of shape a prefab is
/// bad at matching. See PanelPalette for where every colour and size in that chrome comes from.
/// </summary>
public class TooltipManager : Singleton<TooltipManager>
{
    [Tooltip("The canvas this box lives on. Positioning is done in its local space, so the panel must " +
        "be a direct child of it.")]
    [SerializeField] private Canvas canvas;

    [Tooltip("The outer bordered rect. Its width is fixed by PanelPalette.PanelWidth; its height is " +
        "driven by a ContentSizeFitter from the content inside.")]
    [SerializeField] private RectTransform panel;

    [Tooltip("Fades the box in after showDelay. Alpha rather than SetActive so the layout has already " +
        "settled - a panel activated and positioned in the same frame shows up at the wrong size for " +
        "one frame.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Skeleton (built by Tools > UI > Wire Tooltip Panel)")]
    [SerializeField] private RectTransform panelFill;
    [SerializeField] private RectTransform headerRoot;
    [SerializeField] private TMP_Text headerTitle;
    [SerializeField] private RectTransform headerRule;
    [SerializeField] private RectTransform body;
    [SerializeField] private TMP_FontAsset font;

    [Header("Layout")]
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

    /// Who is currently rendered, so the content is only rebuilt when the winning request actually
    /// changes rather than every frame.
    private object shownOwner;

    private bool contentDirty;

    private float delayElapsed;

    private bool faded;

    /// <summary>
    /// Whether the box is actually on screen right now - a request is live *and* it has outlasted
    /// showDelay. Deliberately not "requests.Count > 0": during the delay a caller has asked but the
    /// player has not been shown anything yet, and a tutorial beat waiting for "they hovered a status"
    /// must not count that as done.
    /// </summary>
    public bool IsShowing => faded;

    /// Cached because TryResolve asks for it every frame a world-space tooltip is up, and Camera.main
    /// is a scene search.
    private Camera worldCamera;

    /// Grown on demand and reused, never destroyed - the same pooling contract PartySheetColumn's rows
    /// already use. One entry per TooltipContent.Section the box has ever needed to show at once, so a
    /// card's three-section expanded view costs nothing to render a second time.
    private readonly List<TooltipSectionView> sections = new();

    protected override void Awake()
    {
        base.Awake();

        // Log and degrade rather than throw, the same as ActiveHandViewer.Start - a half-wired
        // tooltip should cost you the tooltip, not every hover in the game.
        if (canvas == null || panel == null || canvasGroup == null || panelFill == null
            || headerRoot == null || headerTitle == null || headerRule == null || body == null
            || font == null)
        {
            Debug.LogError($"{name}: TooltipManager is missing part of its skeleton - run " +
                "Tools > UI > Wire Tooltip Panel, or no tooltips will show");
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
    /// Walks the content once, opening a new pooled TooltipSectionView every time a Section entry
    /// appears and routing every Stat/Term/Figure into whichever section is currently open - the
    /// implicit unlabelled section 0 if a caller never opens one at all, which is what lets a status
    /// chip's single Term render as plain chrome with no heading.
    /// </summary>
    private void Render(TooltipContent content)
    {
        bool hasHeader = false;
        int sectionIndex = -1;

        for (int i = 0; i < content.Entries.Count; i++)
        {
            TooltipEntry entry = content.Entries[i];

            switch (entry.kind)
            {
                case TooltipEntryKind.Header:
                    headerTitle.text = entry.title;
                    hasHeader = true;
                    break;

                case TooltipEntryKind.Section:
                    sectionIndex++;
                    TooltipSectionView opened = SectionAt(sectionIndex);
                    opened.SetActive(true);
                    opened.BeginRender();
                    opened.SetLabel(entry.title);
                    break;

                case TooltipEntryKind.Stat:
                    OpenSection(ref sectionIndex).AddStat(entry.title, entry.body);
                    break;

                case TooltipEntryKind.Term:
                    OpenSection(ref sectionIndex).AddTerm(entry.title, entry.body);
                    break;

                case TooltipEntryKind.Figure:
                    OpenSection(ref sectionIndex).SetFigure(entry.figure);
                    break;
            }
        }

        int visibleSections = sectionIndex + 1;

        for (int i = 0; i < visibleSections; i++) { sections[i].EndRender(showRule: i < visibleSections - 1); }

        for (int i = visibleSections; i < sections.Count; i++) { sections[i].SetActive(false); }

        headerRoot.gameObject.SetActive(hasHeader);
        headerRule.gameObject.SetActive(hasHeader);

        // Bottom-up: each section's own layout groups have to settle before Body sizes around them, and
        // Body before the panel's ContentSizeFitter reads a height that is not the previous hover's. A
        // single top-down ForceRebuildLayoutImmediate over the whole panel does not reliably resolve
        // that in one pass - a section reactivated by SetActive(true) this frame still reports last
        // frame's size on the first pass. The second pass is what makes it report the real one, and
        // Position() reads panel.rect right after this returns.
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
    }

    private TooltipSectionView OpenSection(ref int sectionIndex)
    {
        if (sectionIndex < 0)
        {
            sectionIndex = 0;
            TooltipSectionView opened = SectionAt(0);
            opened.SetActive(true);
            opened.BeginRender();
        }

        return sections[sectionIndex];
    }

    private TooltipSectionView SectionAt(int index)
    {
        while (sections.Count <= index) { sections.Add(new TooltipSectionView(body, font)); }

        return sections[index];
    }

    /// Places the box beside the anchor. The flip-and-clamp rule itself lives in AnchoredPlacement,
    /// shared with the tutorial popup - see that class for why both steps are needed.
    private void Position(Rect anchorScreenRect, TooltipSide side)
    {
        RectTransform canvasRect = (RectTransform)canvas.transform;

        Vector2 position = AnchoredPlacement.Place(
            anchorScreenRect, side, canvasRect, canvas, panel.rect.size, gap, edgePadding);

        // localPosition, not anchoredPosition. AnchoredPlacement answers in the canvas rect's local
        // space, which is what localPosition takes; anchoredPosition is measured from the panel's own
        // anchors and would be offset by wherever those happen to sit.
        panel.localPosition = new Vector3(position.x, position.y, panel.localPosition.z);
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
