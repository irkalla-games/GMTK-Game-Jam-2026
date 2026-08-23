using UnityEngine;

/// Which way the tooltip prefers to sit relative to the thing it explains. A preference, not a
/// promise - TooltipManager flips to the opposite side when the preferred one would run off screen.
public enum TooltipSide
{
    Above = 0,
    Below = 1,
    Right = 2,
    Left = 3,
}

/// <summary>
/// What a tooltip is pointing at, in screen pixels.
///
/// Holds the *source* rather than a snapshot rect, and resolves on demand. That matters because the
/// things being hovered move: a card scales up under the cursor over hoverDuration, and the status
/// panel regrows its own height whenever a second row of chips appears. A rect captured at hover time
/// would be wrong a frame later, and every caller would need to remember to re-Show.
///
/// It also means a source that has been destroyed answers false instead of a stale rectangle, which is
/// how the manager drops a tooltip whose card has been played out from under it.
///
/// Three builders cover the project's worlds - uGUI for the HUD, world-space colliders for cards and
/// characters, and world-space renderers with no collider of their own - so no caller has to do camera
/// maths itself.
/// </summary>
public readonly struct TooltipAnchor
{
    /// Set for uGUI sources. Its canvas is needed too: a Screen Space - Camera canvas has to project
    /// through canvas.worldCamera, and an Overlay one through no camera at all.
    ///
    /// An array rather than a single rect so a group of manually-positioned siblings - the pips in a
    /// hero's mana row, the circles in the next-wave strip - can be lit as one anchor. Each sibling is
    /// placed by its owner via anchoredPosition rather than grown by a Layout Group, so their shared
    /// parent's own rect is never resized to bound them and cannot be used as a stand-in; this unions
    /// whichever of them are actually active on screen instead.
    private readonly RectTransform[] rects;
    private readonly Canvas canvas;

    /// Set for world-space sources. A Collider2D rather than a Renderer because it is the collider that
    /// decides what the mouse is actually over, so the tooltip lines up with the thing you can hit.
    private readonly Collider2D collider;

    /// Set for a world-space source with no collider of its own - a card's cost pip, a piece of its
    /// text. Projects what is actually drawn rather than what a click would hit, since nothing here is
    /// a click target.
    private readonly Renderer renderer;

    public readonly TooltipSide side;

    /// Reused by GetWorldCorners, which fills a caller-supplied array specifically so it need not
    /// allocate one per call - and this resolves every frame a tooltip is up.
    private static readonly Vector3[] Corners = new Vector3[4];

    private TooltipAnchor(RectTransform[] rects, Canvas canvas, Collider2D collider, Renderer renderer, TooltipSide side)
    {
        this.rects = rects;
        this.canvas = canvas;
        this.collider = collider;
        this.renderer = renderer;
        this.side = side;
    }

    /// A uGUI element - the status panel's backing plate, a HUD button, anything on a canvas.
    public static TooltipAnchor Of(RectTransform rect, Canvas canvas, TooltipSide side = TooltipSide.Above)
    {
        return new TooltipAnchor(rect != null ? new[] { rect } : null, canvas, null, null, side);
    }

    /// A group of uGUI elements treated as one - see the rects field's own doc.
    public static TooltipAnchor Of(RectTransform[] rects, Canvas canvas, TooltipSide side = TooltipSide.Above)
    {
        return new TooltipAnchor(rects, canvas, null, null, side);
    }

    /// A world-space object with a collider - a card in hand, a character on the board.
    public static TooltipAnchor Of(Collider2D collider, TooltipSide side = TooltipSide.Right)
    {
        return new TooltipAnchor(null, null, collider, null, side);
    }

    /// A world-space object with no collider of its own - a card's cost pip, its name, its description.
    public static TooltipAnchor Of(Renderer renderer, TooltipSide side = TooltipSide.Right)
    {
        return new TooltipAnchor(null, null, null, renderer, side);
    }

    /// <summary>
    /// Where the anchored thing is on screen right now, or false if it has gone away.
    ///
    /// False rather than a zero rect on purpose: a zero rect would park the tooltip in the corner of
    /// the screen, which reads as a bug rather than as nothing to show.
    /// </summary>
    public bool TryResolve(out Rect screenRect)
    {
        screenRect = default;

        if (rects != null) { return TryResolveRects(out screenRect); }

        if (collider != null) { return ResolveWorldBounds(collider.gameObject, collider.bounds, out screenRect); }

        if (renderer != null) { return ResolveWorldBounds(renderer.gameObject, renderer.bounds, out screenRect); }

        return false;
    }

    /// Unions every active rect in the group rather than trusting a single one - see the rects field's
    /// own doc for why the group shape exists at all.
    private bool TryResolveRects(out Rect screenRect)
    {
        screenRect = default;

        if (canvas == null) { return false; }

        // Null for an Overlay canvas, which is what RectTransformUtility wants in that case - passing a
        // real camera there produces coordinates off by the whole viewport.
        Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        bool any = false;
        Vector2 min = default;
        Vector2 max = default;

        foreach (RectTransform member in rects)
        {
            if (member == null || !member.gameObject.activeInHierarchy) { continue; }

            member.GetWorldCorners(Corners);

            // Corners are bottom-left, top-left, top-right, bottom-right. Taking index 0 and 2 is enough
            // for an unrotated rect, which every rect on these canvases is.
            Vector2 memberMin = RectTransformUtility.WorldToScreenPoint(canvasCamera, Corners[0]);
            Vector2 memberMax = RectTransformUtility.WorldToScreenPoint(canvasCamera, Corners[2]);

            if (!any)
            {
                min = memberMin;
                max = memberMax;
                any = true;
            }
            else
            {
                min = Vector2.Min(min, memberMin);
                max = Vector2.Max(max, memberMax);
            }
        }

        if (!any) { return false; }

        screenRect = FromCorners(min, max);

        return true;
    }

    /// <summary>
    /// Projects a world-space bounds onto the screen - shared by the collider and renderer sources,
    /// since a card's hitbox and its cost pip's renderer resolve exactly the same way.
    ///
    /// The camera is resolved here, from the anchored object itself, rather than passed in by the
    /// caller. There are two cameras now - one for the board, one fixed for the cards and HUD (see
    /// SceneCameras) - and this same method is used for a card in hand, a totem on the board, and now a
    /// piece of a card's own face. Every caller passing its own guess meant three places that each had
    /// to be right about something only the anchored object knows; asking the object removes the
    /// question.
    /// </summary>
    private static bool ResolveWorldBounds(GameObject subject, Bounds bounds, out Rect screenRect)
    {
        screenRect = default;

        Camera worldCamera = SceneCameras.For(subject);

        if (worldCamera == null) { return false; }

        Vector2 min = worldCamera.WorldToScreenPoint(bounds.min);
        Vector2 max = worldCamera.WorldToScreenPoint(bounds.max);

        screenRect = FromCorners(min, max);

        return true;
    }

    /// MinMaxRect on the sorted pair rather than on the two points as given - a flipped camera or a
    /// negatively scaled rect would otherwise produce a rectangle with negative width.
    private static Rect FromCorners(Vector2 a, Vector2 b)
    {
        return Rect.MinMaxRect(
            Mathf.Min(a.x, b.x),
            Mathf.Min(a.y, b.y),
            Mathf.Max(a.x, b.x),
            Mathf.Max(a.y, b.y));
    }
}
