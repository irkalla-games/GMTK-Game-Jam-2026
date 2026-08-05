using UnityEngine;

/// <summary>
/// Keeps a fixed rectangle of world space on screen no matter what the window's aspect ratio is.
///
/// An orthographic camera only ever locks its *height* - `orthographicSize` is half the vertical
/// extent, and the width falls out of the aspect ratio. So a board authored to fit at 16:9 loses its
/// left and right columns the moment somebody plays at 16:10 or 4:3. This widens the camera on
/// anything narrower than the design aspect so the whole frame always fits, and leaves it alone on
/// anything wider, where the extra room is free margin.
///
/// The numbers are the same 1920x1080 the UI is authored against, read as world units at 100 pixels
/// per unit - that is where 19.2 x 10.8 comes from, and why the camera sat at size 5.4 to begin with.
///
/// This has to agree with the CanvasScaler on every screen-space Canvas, or the UI and the board
/// drift apart as the window resizes - the bug this was written to fix. Set those to Scale With
/// Screen Size, 1920x1080, Screen Match Mode = **Expand**, and the two laws are identical:
///
///     canvas scale = min(width / 1920, height / 1080)
///     camera scale = height / (2 * orthographicSize * 100)
///
/// Work the second one through with the size chosen below and it collapses into the first at every
/// aspect ratio. Change the design frame here and those canvases have to move with it.
/// </summary>
[RequireComponent(typeof(Camera))]
[ExecuteAlways]
public class CameraFrame : MonoBehaviour
{
    [Tooltip("Width of the rectangle that must always be visible, in world units. 19.2 is 1920px at " +
        "100 pixels per unit.")]
    [SerializeField] private float designWidth = 19.2f;

    [Tooltip("Height of the rectangle that must always be visible, in world units. 10.8 is 1080px at " +
        "100 pixels per unit.")]
    [SerializeField] private float designHeight = 10.8f;

    private Camera cam;

    /// The aspect the size was last computed for, so the work is skipped on the frames - nearly all of
    /// them - where the window has not changed.
    private float lastAspect;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        // Force the next Update to recompute rather than trusting a stale aspect from before the
        // component was switched off, or from a domain reload in the Editor.
        lastAspect = 0f;
        Apply();
    }

    /// <summary>
    /// Polled rather than driven by an event: Unity raises nothing on a plain desktop window resize,
    /// and `Screen.width`/`Screen.height` are the only honest source. The comparison keeps it to a
    /// float test per frame.
    /// </summary>
    private void Update()
    {
        Apply();
    }

    private void Apply()
    {
        if (cam == null) { cam = GetComponent<Camera>(); }

        if (cam == null || !cam.orthographic) { return; }

        float aspect = cam.aspect;

        // Guard against the degenerate frames - a zero-height window while minimising, an aspect Unity
        // has not filled in yet - that would otherwise divide by zero and blow the size out to
        // infinity, which is not something you can recover from by resizing back.
        if (aspect <= 0f || float.IsNaN(aspect)) { return; }

        if (Mathf.Approximately(aspect, lastAspect)) { return; }

        lastAspect = aspect;

        // Half-height is the larger of "show the design height" and "show the design width" - so the
        // frame is always contained, never cropped. On anything wider than the design aspect the first
        // term wins and this is exactly the hand-authored size it replaces.
        cam.orthographicSize = Mathf.Max(designHeight * 0.5f, designWidth * 0.5f / aspect);
    }
}
