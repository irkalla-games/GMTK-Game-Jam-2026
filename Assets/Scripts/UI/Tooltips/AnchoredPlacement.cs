using UnityEngine;

/// <summary>
/// Where a box goes when it has to sit beside something else on screen: flip to the opposite side when
/// the preferred one would run off, then clamp whatever is left back inside the canvas.
///
/// Both steps are needed. Flipping alone still lets a tall box overhang the top; clamping alone would
/// slide a card's box over the card it is describing instead of moving it to the other side.
///
/// Lifted out of TooltipManager when the tutorial popup needed the same answer. Two callers computing
/// "beside, but on screen" independently is the second-answer-to-one-question this codebase deletes
/// wherever it appears - and the failure mode is the quiet kind, where one of them stops flipping at
/// some aspect ratio nobody tests.
///
/// Static and stateless: everything it needs arrives as arguments, so it neither knows nor cares which
/// canvas or which box is asking.
/// </summary>
public static class AnchoredPlacement
{
    /// <summary>
    /// The box's centre, in `canvasRect`'s local space - which is what RectTransform.localPosition
    /// takes. Deliberately not anchoredPosition: that is measured from the box's own anchors and would
    /// be offset by wherever those happen to sit.
    /// </summary>
    /// <param name="anchorScreenRect">What the box is pointing at, in screen pixels. TooltipAnchor.TryResolve
    /// produces one of these for either a uGUI element or a world-space collider.</param>
    /// <param name="side">Preferred side. A preference, not a promise - see the class doc.</param>
    public static Vector2 Place(
        Rect anchorScreenRect,
        TooltipSide side,
        RectTransform canvasRect,
        Canvas canvas,
        Vector2 boxSize,
        float gap,
        float edgePadding)
    {
        if (canvasRect == null || canvas == null) { return Vector2.zero; }

        // Null for an Overlay canvas, which is what RectTransformUtility wants in that case - passing a
        // real camera there produces coordinates off by the whole viewport.
        Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, anchorScreenRect.min, canvasCamera, out Vector2 min);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, anchorScreenRect.max, canvasCamera, out Vector2 max);

        Rect local = Rect.MinMaxRect(
            Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
            Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));

        Rect area = canvasRect.rect;

        Vector2 position = Beside(local, side, boxSize, gap);

        if (!Fits(position, boxSize, area)) { position = Beside(local, Opposite(side), boxSize, gap); }

        position.x = Mathf.Clamp(position.x, area.xMin + boxSize.x * 0.5f + edgePadding,
            area.xMax - boxSize.x * 0.5f - edgePadding);
        position.y = Mathf.Clamp(position.y, area.yMin + boxSize.y * 0.5f + edgePadding,
            area.yMax - boxSize.y * 0.5f - edgePadding);

        return position;
    }

    /// The box's centre for this side, before any flipping or clamping. Centred on the anchor along the
    /// free axis - which for the character panel puts it squarely over the panel, and for a card puts it
    /// level with the card.
    public static Vector2 Beside(Rect anchor, TooltipSide side, Vector2 size, float gap)
    {
        return side switch
        {
            TooltipSide.Below => new Vector2(anchor.center.x, anchor.yMin - gap - size.y * 0.5f),
            TooltipSide.Right => new Vector2(anchor.xMax + gap + size.x * 0.5f, anchor.center.y),
            TooltipSide.Left => new Vector2(anchor.xMin - gap - size.x * 0.5f, anchor.center.y),
            _ => new Vector2(anchor.center.x, anchor.yMax + gap + size.y * 0.5f),
        };
    }

    public static bool Fits(Vector2 centre, Vector2 size, Rect area)
    {
        return centre.x - size.x * 0.5f >= area.xMin
            && centre.x + size.x * 0.5f <= area.xMax
            && centre.y - size.y * 0.5f >= area.yMin
            && centre.y + size.y * 0.5f <= area.yMax;
    }

    public static TooltipSide Opposite(TooltipSide side)
    {
        return side switch
        {
            TooltipSide.Above => TooltipSide.Below,
            TooltipSide.Below => TooltipSide.Above,
            TooltipSide.Right => TooltipSide.Left,
            _ => TooltipSide.Right,
        };
    }
}
