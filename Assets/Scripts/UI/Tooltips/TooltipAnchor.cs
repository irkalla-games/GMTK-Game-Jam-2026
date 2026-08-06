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
/// The two builders cover the project's two worlds - uGUI for the HUD, world-space colliders for cards
/// and characters - so no caller has to do camera maths itself.
/// </summary>
public readonly struct TooltipAnchor
{
    /// Set for uGUI sources. Its canvas is needed too: a Screen Space - Camera canvas has to project
    /// through canvas.worldCamera, and an Overlay one through no camera at all.
    private readonly RectTransform rect;
    private readonly Canvas canvas;

    /// Set for world-space sources. A Collider2D rather than a Renderer because it is the collider that
    /// decides what the mouse is actually over, so the tooltip lines up with the thing you can hit.
    private readonly Collider2D collider;

    public readonly TooltipSide side;

    /// Reused by GetWorldCorners, which fills a caller-supplied array specifically so it need not
    /// allocate one per call - and this resolves every frame a tooltip is up.
    private static readonly Vector3[] Corners = new Vector3[4];

    private TooltipAnchor(RectTransform rect, Canvas canvas, Collider2D collider, TooltipSide side)
    {
        this.rect = rect;
        this.canvas = canvas;
        this.collider = collider;
        this.side = side;
    }

    /// A uGUI element - the status panel's backing plate, a HUD button, anything on a canvas.
    public static TooltipAnchor Of(RectTransform rect, Canvas canvas, TooltipSide side = TooltipSide.Above)
    {
        return new TooltipAnchor(rect, canvas, null, side);
    }

    /// A world-space object with a collider - a card in hand, a character on the board.
    public static TooltipAnchor Of(Collider2D collider, TooltipSide side = TooltipSide.Right)
    {
        return new TooltipAnchor(null, null, collider, side);
    }

    /// <summary>
    /// Where the anchored thing is on screen right now, or false if it has gone away.
    ///
    /// False rather than a zero rect on purpose: a zero rect would park the tooltip in the corner of
    /// the screen, which reads as a bug rather than as nothing to show.
    /// </summary>
    public bool TryResolve(Camera worldCamera, out Rect screenRect)
    {
        screenRect = default;

        if (rect != null) { return TryResolveRect(out screenRect); }

        if (collider != null) { return TryResolveBounds(worldCamera, out screenRect); }

        return false;
    }

    private bool TryResolveRect(out Rect screenRect)
    {
        screenRect = default;

        if (canvas == null) { return false; }

        // Null for an Overlay canvas, which is what RectTransformUtility wants in that case - passing a
        // real camera there produces coordinates off by the whole viewport.
        Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        rect.GetWorldCorners(Corners);

        // Corners are bottom-left, top-left, top-right, bottom-right. Taking index 0 and 2 is enough
        // for an unrotated rect, which every rect on these canvases is.
        Vector2 min = RectTransformUtility.WorldToScreenPoint(canvasCamera, Corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(canvasCamera, Corners[2]);

        screenRect = FromCorners(min, max);

        return true;
    }

    private bool TryResolveBounds(Camera worldCamera, out Rect screenRect)
    {
        screenRect = default;

        if (worldCamera == null) { return false; }

        Bounds bounds = collider.bounds;

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
