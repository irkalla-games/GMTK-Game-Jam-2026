using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The size/spacing settings one IntentBadgeStack.Show call needs, bundled so the call site does not
/// carry six loose floats - every value already resolved to real units by IntentStrip (a fraction of
/// the primary icon's own width), not a fraction itself.
/// </summary>
public readonly struct IntentBadgeLayout
{
    public readonly float size;
    public readonly float overlap;
    public readonly float spacing;
    public readonly float numberGap;
    public readonly Vector2 numberSize;
    public readonly float numberFontSize;

    public IntentBadgeLayout(float size, float overlap, float spacing, float numberGap,
                              Vector2 numberSize, float numberFontSize)
    {
        this.size = size;
        this.overlap = overlap;
        this.spacing = spacing;
        this.numberGap = numberGap;
        this.numberSize = numberSize;
        this.numberFontSize = numberFontSize;
    }
}

/// <summary>
/// The badges hung off one intent slot's bottom-right corner, one per rider the committed card would
/// apply - Root, Vulnerable, Weaken, and the like (see IntentRider). Grows right-to-left from the
/// icon's own corner, each badge's plate overlapping the icon by IntentBadgeLayout.overlap and its
/// stack-count number sitting to its own right - which is why two riders on one card reach left past
/// the icon's own bounds: badge 1's number has to fit in the gap before badge 0's plate begins.
///
/// Built and positioned in the same shared coordinate frame every IntentSlot's icon window lives in
/// (see IntentStrip and IntentBadge.Build) - a point anchor shared with every slot on the strip, so
/// this stack's anchoredPosition values are directly comparable to the icon window's own, with no
/// separate conversion.
/// </summary>
public class IntentBadgeStack
{
    private readonly List<IntentBadge> pool = new();

    private Transform parent;
    private Vector2 anchorPoint;
    private int layer;

    /// How far this stack's rightmost extent (badge 0's plate, or its number when one is showing)
    /// sits past the icon window's own right edge - what IntentStrip adds to a slot's reserved width
    /// so the next slot in the row does not start on top of it. 0 when nothing is visible.
    public float RightOverhang { get; private set; }

    /// How far this stack's leftmost extent sits past the icon window's own left edge - what
    /// IntentStrip adds when placing the *previous* slot in the row, since badges grow leftward and a
    /// two-rider stack can reach back past its own icon's bounds. 0 with fewer riders than it takes to
    /// reach that far, or when nothing is visible.
    public float LeftOverhang { get; private set; }

    public bool Visible { get; private set; }

    public void Build(Transform parent, Vector2 anchorPoint, int layer)
    {
        this.parent = parent;
        this.anchorPoint = anchorPoint;
        this.layer = layer;
    }

    /// <summary>
    /// Lays out every rider in `riders` (capped at `maxBadges`, oldest-authored first) along
    /// `window`'s own bottom-right corner and paints each one. A rider's own selfTargeted flag picks
    /// which of the two colours its plate takes - see IntentRider.
    /// </summary>
    public void Show(IReadOnlyList<IntentRider> riders, StatusIcons statusIcons, IntentIcons icons,
                      RectTransform window, IntentBadgeLayout layoutSettings,
                      Color selfColor, Color targetColor, bool showNumbers, int maxBadges)
    {
        int count = Mathf.Min(riders.Count, maxBadges);

        if (count == 0)
        {
            Hide();
            return;
        }

        float iconRight = IntentSlot.RightEdge(window);
        float iconLeft = IntentSlot.LeftEdge(window);
        float iconBottom = IntentSlot.BottomEdge(window);

        float plateTop = iconBottom + layoutSettings.size * layoutSettings.overlap;
        float cursor = iconRight - layoutSettings.size * layoutSettings.overlap;

        float rightExtent = 0f;
        float leftmostPlateLeft = cursor;

        for (int i = 0; i < count; i++)
        {
            IntentBadge badge = BadgeAt(i);
            IntentRider rider = riders[i];

            Sprite sprite = rider.status != StatusType.None
                ? (statusIcons != null ? statusIcons.For(rider.status) : null)
                : (icons != null ? icons.ForRider(rider.kind) : null);

            Color plateColor = rider.selfTargeted ? selfColor : targetColor;
            bool hasNumber = showNumbers && rider.amount > 0;

            badge.Show(sprite, plateColor, new Vector2(cursor, plateTop), layoutSettings.size,
                       layoutSettings.numberFontSize, rider.amount, hasNumber,
                       layoutSettings.numberGap, layoutSettings.numberSize);

            if (i == 0)
            {
                rightExtent = hasNumber
                    ? cursor + layoutSettings.size + layoutSettings.numberGap + layoutSettings.numberSize.x
                    : cursor + layoutSettings.size;
            }

            leftmostPlateLeft = cursor;

            float width = layoutSettings.size + (hasNumber ? layoutSettings.numberGap + layoutSettings.numberSize.x : 0f);
            cursor -= width + layoutSettings.spacing;
        }

        for (int i = count; i < pool.Count; i++) { pool[i].Hide(); }

        Visible = true;
        RightOverhang = Mathf.Max(0f, rightExtent - iconRight);
        LeftOverhang = Mathf.Max(0f, iconLeft - leftmostPlateLeft);
    }

    public void Hide()
    {
        foreach (IntentBadge badge in pool) { badge.Hide(); }

        Visible = false;
        RightOverhang = 0f;
        LeftOverhang = 0f;
    }

    /// <summary>
    /// The same RightOverhang/LeftOverhang Show above computes, but callable before any window exists
    /// at its final position for the frame - IntentStrip.LayOut needs to know how far a step's badges
    /// will reach, in both directions, before it can find where that step (and the one before it)
    /// belong in a row it is about to centre as a whole.
    ///
    /// Expressed purely as offsets from the icon's own edges rather than from `window`'s current
    /// anchoredPosition - it needs the icon's own width (`slotWidth`) but nothing about where that icon
    /// currently sits. Algebraically identical to Show's own walk (each is the other minus/plus
    /// iconRight), so the two can never disagree about how much room a given set of riders needs -
    /// see Show's own cursor variable, which starts at `iconRight - size*overlap`, exactly `slotWidth`
    /// ahead of where this one starts.
    /// </summary>
    public static void PeekOverhangs(IReadOnlyList<IntentRider> riders, IntentBadgeLayout layoutSettings,
                                      bool showNumbers, int maxBadges, float slotWidth,
                                      out float rightOverhang, out float leftOverhang)
    {
        int count = Mathf.Min(riders.Count, maxBadges);

        if (count == 0)
        {
            rightOverhang = 0f;
            leftOverhang = 0f;
            return;
        }

        float cursor = -layoutSettings.size * layoutSettings.overlap;
        float rightExtent = 0f;
        float leftmost = cursor;

        for (int i = 0; i < count; i++)
        {
            bool hasNumber = showNumbers && riders[i].amount > 0;

            if (i == 0)
            {
                rightExtent = hasNumber
                    ? cursor + layoutSettings.size + layoutSettings.numberGap + layoutSettings.numberSize.x
                    : cursor + layoutSettings.size;
            }

            leftmost = cursor;

            float width = layoutSettings.size + (hasNumber ? layoutSettings.numberGap + layoutSettings.numberSize.x : 0f);
            cursor -= width + layoutSettings.spacing;
        }

        rightOverhang = Mathf.Max(0f, rightExtent);
        leftOverhang = Mathf.Max(0f, -(slotWidth + leftmost));
    }

    private IntentBadge BadgeAt(int index)
    {
        while (pool.Count <= index)
        {
            IntentBadge badge = new();
            badge.Build(parent, anchorPoint, layer);
            pool.Add(badge);
        }

        return pool[index];
    }
}
