using UnityEngine;

/// <summary>
/// Which camera is which, now that there are two.
///
/// The board is rendered by a camera that zooms and pans to frame whatever size board the level asked
/// for; the cards, hand and HUD are rendered by a second camera stacked on top that never moves. That
/// is what lets the board resize without anything counter-scaling the world-space card sprites - see
/// BoardCamera.
///
/// With two cameras `Camera.main` stops being a meaningful answer: it returns whichever one carries
/// the MainCamera tag, and half the call sites in the project want the other one. Every former
/// `Camera.main` now asks here for the one it actually means:
///
///     Board   picking a tile, a character's world-space overhead canvas, anything on the board
///     Ui      the screen-space-camera canvases, tooltips, the tutorial overlay, card picking
///
/// Serialized rather than found by tag, so a mis-tagged camera is a null reference in the Inspector
/// rather than a silent wrong answer. The two properties fall back to Camera.main only so a scene
/// that has not been through Tools/Board/2 - Wire Cameras yet still runs single-camera.
/// </summary>
public class SceneCameras : Singleton<SceneCameras>
{
    [Tooltip("Renders the board - tiles, characters, floor blocks. Zooms and pans; frames the board.")]
    [SerializeField] private Camera boardCamera;

    [Tooltip("Renders cards, the hand and the HUD. Fixed at the 19.2 x 10.8 design frame, never moves.")]
    [SerializeField] private Camera uiCamera;

    /// The camera that draws the board. Null-safe: falls back to Camera.main so a scene that still has
    /// a single camera keeps working rather than throwing on every tile hover.
    public static Camera Board => Resolve(Instance != null ? Instance.boardCamera : null);

    /// The camera the screen-space canvases render through.
    public static Camera Ui => Resolve(Instance != null ? Instance.uiCamera : null);

    private static Camera Resolve(Camera camera)
    {
        // `!=` rather than `??`: a destroyed or unassigned Camera is a fake-null that `??` would hand
        // straight back. See CLAUDE.md.
        return camera != null ? camera : Camera.main;
    }

    /// <summary>
    /// The camera that actually draws `subject` - which is the only one whose WorldToScreenPoint
    /// gives the right answer for it.
    ///
    /// Anything projecting a world position onto the screen has to ask this rather than pick a
    /// camera. A tooltip anchored to a card and a tooltip anchored to a totem are the same code path
    /// on two objects that are rendered by different cameras at different zooms, so a hardcoded
    /// choice is guaranteed to misplace one of them - and it would misplace it by a *growing* amount
    /// as the board zoom diverges from the UI camera, which reads as a tooltip that drifts.
    ///
    /// Answered from the board camera's culling mask rather than from a stored layer number, so it
    /// stays correct by construction: the camera that renders the layer is the camera returned.
    /// </summary>
    public static Camera For(GameObject subject)
    {
        if (subject == null) { return Ui; }

        Camera board = Board;

        if (board != null && (board.cullingMask & (1 << subject.layer)) != 0) { return board; }

        return Ui;
    }
}
