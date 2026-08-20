using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dims the whole screen except for a few rectangular holes - the tutorial's way of saying "this, and
/// nothing else."
///
/// **A hole-punch rather than tinting what is behind it.** The obvious implementation - walk every
/// SpriteRenderer and Graphic and darken the ones that are not the target - cannot work here, because
/// half this project's visuals already own their own colour and rewrite it constantly: TileSelector owns
/// a tile's colour by design, and CharacterOutline, HealthBarFill, DamagePreviewFill and the rarity
/// tints all write theirs on their own schedule. A dim that touched them would fight every one of them
/// and would have to restore each exactly; the first missed restore is a permanently mis-coloured
/// object. This draws *over* the board instead and touches nothing, so there is nothing to restore and
/// nothing to fight.
///
/// The same choice is what lets one implementation cover both of the project's worlds. Holes arrive as
/// TooltipAnchors, which already resolve either a uGUI RectTransform or a world-space Collider2D to a
/// screen rect - so a card in hand, a grid tile and the End Turn button are all the same kind of hole
/// here, and none of them has to be re-sorted or reparented to be seen.
///
/// **Never blocks input.** Every quad is raycastTarget = false. A quad that swallowed clicks would only
/// half-work anyway - it would stop the End Turn button, since that is uGUI, but not a tile or a card,
/// which are physics OnMouseDown and pass straight through a canvas (the same fact
/// BattleManager.InputLocked's doc comment turns on). Blocking is TutorialGate's job, in one place, for
/// every kind of click.
/// </summary>
public class TutorialSpotlight : Singleton<TutorialSpotlight>
{
    [Tooltip("The canvas the dim is drawn on. Positioning is done in its local space, so quadParent "
             + "must be a direct child of it. Overlay sorting layer, ordered above the battle HUD and "
             + "below the tutorial popup - see TutorialWiring.")]
    [SerializeField] private Canvas canvas;

    [Tooltip("Every dim quad is parented here. Switched off wholesale when nothing is spotlit.")]
    [SerializeField] private RectTransform quadParent;

    [Tooltip("How dark the screen goes outside the holes. Alpha well under 1 on purpose - the point is "
             + "to push the rest of the board back, not to hide it, so the player keeps their bearings.")]
    [SerializeField] private Color dimColor = new(0.02f, 0.02f, 0.04f, 0.78f);

    [Tooltip("Screen pixels of breathing room added around every hole, so a target is never clipped "
             + "tight to its own collider.")]
    [SerializeField] private float holePadding = 10f;

    /// The live asks. Re-resolved every frame rather than snapshotted, for exactly the reason
    /// TooltipAnchor exists: the things being lit move. A card scales up under the cursor, a character
    /// walks, the hand re-lays itself out. A rect captured when the step began would be wrong a frame
    /// later.
    private readonly List<TooltipAnchor> holes = new();

    /// Grown on demand and reused, never destroyed - the same pooling contract TooltipManager's sections
    /// and SelectedCharacterPanel's status chips both use.
    private readonly List<Image> quads = new();

    private static readonly List<float> Xs = new();
    private static readonly List<float> Ys = new();
    private static readonly List<Rect> Resolved = new();

    public bool IsShowing { get; private set; }

    protected override void Awake()
    {
        base.Awake();

        // Log and degrade rather than throw, the same as TooltipManager.Awake - a half-wired spotlight
        // should cost you the dim, not every step of the tutorial.
        if (canvas == null || quadParent == null)
        {
            Debug.LogError($"{name}: TutorialSpotlight is missing its canvas or quad parent - run "
                           + "Tools > Tutorial > Wire Tutorial Overlay, or nothing will be dimmed");
            enabled = false;
            return;
        }

        quadParent.gameObject.SetActive(false);
    }

    /// <summary>
    /// Dims everything but these.
    ///
    /// No anchors dims the whole screen rather than nothing - that is what a centre-screen read beat
    /// wants, where the box is talking about the game as a whole and everything behind it should recede.
    /// Only Clear takes the dim down, so "show with no holes" and "show nothing" stay distinguishable;
    /// collapsing them would leave those beats as a box floating over an undimmed board.
    /// </summary>
    public void Show(params TooltipAnchor[] anchors)
    {
        holes.Clear();

        if (anchors != null) { holes.AddRange(anchors); }

        IsShowing = true;
        quadParent.gameObject.SetActive(true);

        // Immediately, not next LateUpdate: the popup is about to be positioned against these same
        // anchors this frame, and a dim that trails it by a frame reads as a flicker on every step.
        Rebuild();
    }

    public void Clear()
    {
        holes.Clear();
        IsShowing = false;
        quadParent.gameObject.SetActive(false);
    }

    /// LateUpdate for the reason TooltipManager.LateUpdate documents: the things being anchored to
    /// finish moving in Update - a card's hover tween, a panel's resize - so reading them any earlier
    /// trails them by a frame.
    private void LateUpdate()
    {
        if (!IsShowing) { return; }

        // Cheap to skip: with no holes the layout is one full-screen quad that cannot move, so there is
        // nothing to track.
        if (holes.Count == 0) { return; }

        Rebuild();
    }

    private void Rebuild()
    {
        RectTransform canvasRect = (RectTransform)canvas.transform;
        Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Rect area = canvasRect.rect;

        Resolved.Clear();

        foreach (TooltipAnchor anchor in holes)
        {
            // False means the thing being lit has been destroyed - a card played out from under the
            // spotlight. Dropping the hole rather than keeping a stale one is the same call
            // TooltipManager makes when an anchor stops resolving.
            if (!anchor.TryResolve(out Rect screenRect)) { continue; }

            screenRect = Pad(screenRect, holePadding);

            Resolved.Add(ToLocal(screenRect, canvasRect, canvasCamera));
        }

        BuildEdges(area, Resolved);

        int used = 0;

        // One quad per grid cell whose centre is not inside a hole. Exact for any number of holes, and
        // seam-free: both sides of every internal edge read the identical float out of Xs/Ys, so two
        // neighbouring quads share an edge exactly rather than leaving a hairline gap.
        for (int i = 0; i < Xs.Count - 1; i++)
        {
            for (int j = 0; j < Ys.Count - 1; j++)
            {
                Rect cell = Rect.MinMaxRect(Xs[i], Ys[j], Xs[i + 1], Ys[j + 1]);

                if (cell.width <= Mathf.Epsilon || cell.height <= Mathf.Epsilon) { continue; }

                if (IsInsideAnyHole(cell.center, Resolved)) { continue; }

                Place(QuadAt(used++), cell);
            }
        }

        for (int i = used; i < quads.Count; i++) { quads[i].gameObject.SetActive(false); }
    }

    /// <summary>
    /// The distinct vertical and horizontal cut lines of the whole layout: the canvas edges, plus both
    /// edges of every hole. Slicing on all of them at once is what makes the result exact - every hole
    /// then falls on cell boundaries rather than partway through one, so no cell is half-lit.
    /// </summary>
    private static void BuildEdges(Rect area, List<Rect> holeRects)
    {
        Xs.Clear();
        Ys.Clear();

        Xs.Add(area.xMin);
        Xs.Add(area.xMax);
        Ys.Add(area.yMin);
        Ys.Add(area.yMax);

        foreach (Rect hole in holeRects)
        {
            AddEdge(Xs, hole.xMin, area.xMin, area.xMax);
            AddEdge(Xs, hole.xMax, area.xMin, area.xMax);
            AddEdge(Ys, hole.yMin, area.yMin, area.yMax);
            AddEdge(Ys, hole.yMax, area.yMin, area.yMax);
        }

        Xs.Sort();
        Ys.Sort();
    }

    /// Clamped into the canvas, because a hole may hang off the edge of the screen and a cut line
    /// outside the area would produce a zero-width column that is only ever skipped anyway.
    private static void AddEdge(List<float> edges, float value, float min, float max)
    {
        value = Mathf.Clamp(value, min, max);

        foreach (float existing in edges)
        {
            if (Mathf.Approximately(existing, value)) { return; }
        }

        edges.Add(value);
    }

    private static bool IsInsideAnyHole(Vector2 point, List<Rect> holeRects)
    {
        foreach (Rect hole in holeRects)
        {
            if (hole.Contains(point)) { return true; }
        }

        return false;
    }

    private static Rect Pad(Rect rect, float padding)
    {
        return Rect.MinMaxRect(
            rect.xMin - padding, rect.yMin - padding, rect.xMax + padding, rect.yMax + padding);
    }

    /// Screen pixels to the canvas rect's local space - the same projection AnchoredPlacement does for
    /// the tooltip box, and for the same reason: a Screen Space - Camera canvas has to go through
    /// canvas.worldCamera, an Overlay one through no camera at all.
    private static Rect ToLocal(Rect screenRect, RectTransform canvasRect, Camera canvasCamera)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, screenRect.min, canvasCamera, out Vector2 min);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, screenRect.max, canvasCamera, out Vector2 max);

        return Rect.MinMaxRect(
            Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
            Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }

    private Image QuadAt(int index)
    {
        while (quads.Count <= index)
        {
            Image quad = TooltipViewBuilder.CreateFill(quadParent, $"Dim{quads.Count}", dimColor);

            RectTransform rect = (RectTransform)quad.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            quads.Add(quad);
        }

        Image existing = quads[index];
        existing.gameObject.SetActive(true);
        existing.color = dimColor;

        return existing;
    }

    private static void Place(Image quad, Rect cell)
    {
        RectTransform rect = (RectTransform)quad.transform;

        rect.sizeDelta = cell.size;
        rect.localPosition = new Vector3(cell.center.x, cell.center.y, 0f);
    }

}
