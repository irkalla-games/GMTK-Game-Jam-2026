using UnityEngine;

/// <summary>
/// Frames the board, whatever size the level asked for.
///
/// This is the *base* camera of a two-camera stack. It renders only the Board layer and is free to
/// move and zoom; the UI camera stacked on top of it renders the cards, hand and HUD at a fixed
/// 19.2 x 10.8 frame and never moves at all. That split is the whole reason nothing has to
/// counter-scale: a bigger board means this camera pulls back, and the card sprites - which are
/// world-space SpriteRenderers, not UGUI - are drawn by a camera that did not.
///
/// Deliberately shaped around Focus and Zoom rather than writing transform.position directly, so the
/// drag-pan and scroll-zoom that come later have somewhere to write that already knows the clamps.
/// Frame() is then just "pick a Focus and Zoom that happen to fit the whole board".
///
/// The board is fitted into `boardViewport` - a *sub-rectangle* of the screen, not the whole of it -
/// because the hand occupies the bottom of the frame and the HUD the top. Fitting to the full screen
/// would centre the board behind the cards.
/// </summary>
[RequireComponent(typeof(Camera))]
public class BoardCamera : MonoBehaviour
{
    [Header("Framing")]
    [Tooltip("The part of the screen the whole board has to fit inside, in normalized 0-1 screen "
             + "coordinates. y = 0 is the bottom. The default reserves the bottom quarter for the hand "
             + "and a sliver at the top for the HUD. Shrink the height to give the cards more room; "
             + "move the rect to re-centre and re-fit in one go.")]
    [SerializeField] private Rect boardViewport = new(0.02f, 0.26f, 0.96f, 0.70f);

    [Tooltip("Shifts the board on screen, in world units: +x moves it RIGHT, +y moves it UP. Does not "
             + "change the zoom, only where the board sits. This is the 'just nudge it' knob.")]
    [SerializeField] private Vector2 focusOffset = new(1f, -0.8f);

    [Header("Zoom")]
    [Tooltip("Multiplies the fitted zoom. Below 1 pulls the camera IN so the board is drawn larger "
             + "than a bare fit (and may overhang the viewport rectangle); above 1 pushes it out and "
             + "leaves margin. Separate from boardViewport on purpose: the rectangle says where the "
             + "board is allowed to sit, this says how tightly it is fitted into it, and conflating "
             + "the two makes either one impossible to tune without disturbing the other.")]
    [SerializeField] private float zoomScale = 0.9f;

    [Header("Zoom limits")]
    [Tooltip("Closest the camera is allowed to get - the max-zoom cap. A board small enough to be "
             + "drawn bigger than this stops here and sits in the middle of the viewport with slack "
             + "around it, rather than filling the screen with three enormous tiles.")]
    [SerializeField] private float minOrthoSize = 6f;

    [Tooltip("Furthest the camera is allowed to pull back. A board too big to fit at this size gets "
             + "cropped rather than shrinking without limit - which is the louder failure of the two.")]
    [SerializeField] private float maxOrthoSize = 12f;

    private Camera cam;

    /// The bounds Frame() was last handed, kept so a window resize can re-fit without BattleManager
    /// having to notice and call again - the same reason CameraFrame caches its aspect.
    private Bounds framedBounds;

    private bool hasFramed;

    /// The aspect the current framing was computed for. An orthographic camera locks its *height*, so
    /// the horizontal fit changes with the window and has to be redone when it does.
    private float lastAspect;

    /// World point the centre of `boardViewport` is looking at. Pan writes here.
    public Vector2 Focus { get; private set; }

    /// Half the vertical extent of the *whole screen*, in world units - Camera.orthographicSize. Zoom
    /// writes here; it is clamped to [minOrthoSize, maxOrthoSize] on the way in.
    public float Zoom { get; private set; }

    private void Awake()
    {
        cam = GetComponent<Camera>();
        Zoom = Mathf.Clamp(cam != null ? cam.orthographicSize : minOrthoSize, minOrthoSize, maxOrthoSize);
        Focus = transform.position;
    }

    /// <summary>
    /// Polled rather than event-driven for the reason CameraFrame gives: Unity raises nothing on a
    /// plain desktop window resize, so the aspect has to be watched. Only re-fits when it actually
    /// changed, and only once a board has been framed at all.
    /// </summary>
    private void Update()
    {
        if (!hasFramed || cam == null) { return; }

        float aspect = cam.aspect;

        if (aspect <= 0f || float.IsNaN(aspect)) { return; }

        if (Mathf.Approximately(aspect, lastAspect)) { return; }

        Frame(framedBounds);
    }

    /// <summary>
    /// Points the camera at `board` and zooms so the whole thing sits inside `boardViewport`.
    ///
    /// Called by BattleManager immediately after GridManager.BuildGrid, which is the only moment the
    /// board's extent changes. Safe to call again with the same bounds - it is a pure recompute.
    /// </summary>
    public void Frame(Bounds board)
    {
        if (cam == null) { cam = GetComponent<Camera>(); }

        if (cam == null || !cam.orthographic) { return; }

        float aspect = cam.aspect;

        // Same degenerate-frame guard CameraFrame carries: a zero-height window while minimising would
        // otherwise divide through to infinity, which resizing back does not recover from.
        if (aspect <= 0f || float.IsNaN(aspect)) { return; }

        // A zero-size viewport would divide by zero and a negative one is meaningless, so a mis-typed
        // rect degrades to the full screen rather than blowing the camera out.
        float viewWidth = Mathf.Max(boardViewport.width, 0.01f);
        float viewHeight = Mathf.Max(boardViewport.height, 0.01f);

        framedBounds = board;
        hasFramed = true;
        lastAspect = aspect;

        // orthographicSize is half the *screen's* height, while the board only gets `viewHeight` of
        // that screen - hence dividing by 2 * viewHeight rather than by 2. The width term converts
        // through the aspect ratio the same way.
        float forHeight = board.size.y / (2f * viewHeight);
        float forWidth = board.size.x / (2f * viewWidth * aspect);

        Zoom = Mathf.Clamp(Mathf.Max(forHeight, forWidth) * zoomScale, minOrthoSize, maxOrthoSize);

        // Minus, not plus. Focus is the world point the viewport centre looks at, so moving the focus
        // left is what makes the board appear further right - the offset and the apparent motion are
        // opposites. Subtracting here is what lets focusOffset be stated the way it is read in the
        // Inspector: +x moves the board right.
        Focus = (Vector2)board.center - focusOffset;

        Apply();
    }

    private void Apply()
    {
        if (cam == null) { return; }

        cam.orthographicSize = Zoom;

        // Focus is where the centre of boardViewport should point, but the camera's transform is the
        // centre of the *screen*. Walk back from one to the other: a viewport centred above the middle
        // of the screen means the camera has to sit below the thing it is framing.
        Vector2 fromScreenCentre = boardViewport.center - new Vector2(0.5f, 0.5f);

        Vector2 worldOffset = new(
            fromScreenCentre.x * 2f * Zoom * cam.aspect,
            fromScreenCentre.y * 2f * Zoom);

        Vector2 position = Focus - worldOffset;

        // z is preserved rather than zeroed: it is what puts the camera in front of the board at all,
        // and it is authored on the transform.
        transform.position = new Vector3(position.x, position.y, transform.position.z);
    }

    /// <summary>
    /// Draws the viewport rectangle and the framed board in world space, so `boardViewport` and
    /// `focusOffset` can be dialled in against something visible instead of by trial and error.
    ///
    /// Select the BoardCamera in the Hierarchy while in Play Mode: yellow is the rectangle the board
    /// is being fitted into, cyan is the board's actual extent. When the two touch on one axis, that
    /// axis is what the zoom is being driven by.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Camera preview = cam != null ? cam : GetComponent<Camera>();

        if (preview == null || !preview.orthographic) { return; }

        float halfHeight = preview.orthographicSize;
        float halfWidth = halfHeight * preview.aspect;
        Vector3 centre = preview.transform.position;

        Vector2 min = new(
            centre.x + (boardViewport.xMin - 0.5f) * 2f * halfWidth,
            centre.y + (boardViewport.yMin - 0.5f) * 2f * halfHeight);

        Vector2 size = new(
            boardViewport.width * 2f * halfWidth,
            boardViewport.height * 2f * halfHeight);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(new Vector3(min.x + size.x / 2f, min.y + size.y / 2f, 0f),
                            new Vector3(size.x, size.y, 0f));

        if (!hasFramed) { return; }

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(new Vector3(framedBounds.center.x, framedBounds.center.y, 0f),
                            new Vector3(framedBounds.size.x, framedBounds.size.y, 0f));
    }
}
