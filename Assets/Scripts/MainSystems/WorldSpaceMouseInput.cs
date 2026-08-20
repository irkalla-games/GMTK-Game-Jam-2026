using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Sends OnMouseEnter / OnMouseExit / OnMouseDown to world-space colliders drawn by the UI camera.
///
/// ## Why this has to exist
///
/// Unity raises those messages itself, but only for cameras its internal mouse pass actually walks -
/// and a URP **Overlay** camera is not one of them. Overlay cameras do not render on their own; they
/// render as part of a Base camera's stack, and the built-in pass skips them. So the moment the cards
/// moved onto the overlay UI camera they stopped receiving hover and clicks entirely, while tiles and
/// characters - drawn by the Base board camera - kept working perfectly.
///
/// That is the one assumption the two-camera split rested on, and it turned out to be false. This is
/// the replacement: the same three messages, dispatched explicitly, against the camera that actually
/// draws the thing.
///
/// ## Not a second input path
///
/// Tools/Board/2 - Wire Cameras sets `uiCamera.eventMask = 0`, so even if a future Unity version
/// starts walking overlay cameras, it will not also deliver these messages and double every click.
/// This component is the only source for the UI camera; the board camera keeps using Unity's own.
///
/// Deliberately does *not* consult BattleManager.InputLocked or block on UGUI. Neither did the
/// built-in path, and CardViewer already gates itself - adding a new rule here would change behaviour
/// that has nothing to do with the camera split.
/// </summary>
public class WorldSpaceMouseInput : MonoBehaviour
{
    [Tooltip("The camera whose world-space colliders this dispatches for - the overlay UI camera. "
             + "Leave unassigned to fall back to SceneCameras.Ui.")]
    [SerializeField] private Camera dispatchFor;

    /// What the cursor was over last frame, so enter and exit fire exactly once each. Held as the
    /// Collider2D rather than its GameObject so a destroyed card - played out from under the cursor -
    /// compares false against everything and simply stops receiving messages.
    private Collider2D hovered;

    /// Reused so a per-frame hit test allocates nothing. 16 is far more than the cards that can
    /// overlap one point in a fanned hand; OverlapPoint fills what fits and reports the real count.
    private readonly Collider2D[] hits = new Collider2D[16];

    private void Update()
    {
        Camera cam = dispatchFor != null ? dispatchFor : SceneCameras.Ui;

        if (cam == null || Mouse.current == null) { return; }

        Collider2D top = Topmost(cam);

        if (top != hovered)
        {
            // Enter and exit are sent to the collider's own GameObject, matching where Unity sends
            // them - CardViewer sits on the same object as its BoxCollider2D.
            if (hovered != null) { hovered.SendMessage("OnMouseExit", SendMessageOptions.DontRequireReceiver); }

            hovered = top;

            if (hovered != null) { hovered.SendMessage("OnMouseEnter", SendMessageOptions.DontRequireReceiver); }
        }

        if (hovered != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            hovered.SendMessage("OnMouseDown", SendMessageOptions.DontRequireReceiver);
        }
    }

    /// <summary>
    /// The collider nearest the camera under the cursor, or null.
    ///
    /// Picked by z rather than by sorting order, which is what the built-in pass does and what the
    /// hand is already laid out to suit: ActiveHandViewer pushes each card `0.01 * index` further
    /// toward the camera, and hovering pushes the hovered one further still. So nearest-z is the same
    /// card the player sees on top, and a fanned hand picks the one whose visible corner you clicked
    /// rather than an arbitrary overlapping neighbour.
    ///
    /// Filtered by the camera's own culling mask, so this can never pick a board collider out from
    /// under the board camera's own event handling.
    /// </summary>
    private Collider2D Topmost(Camera cam)
    {
        Vector3 world = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());

        ContactFilter2D filter = new();
        filter.SetLayerMask(cam.cullingMask);
        filter.useTriggers = true;

        int count = Physics2D.OverlapPoint(world, filter, hits);

        Collider2D best = null;
        float bestZ = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider2D candidate = hits[i];

            if (candidate == null) { continue; }

            float z = candidate.transform.position.z;

            if (best != null && z >= bestZ) { continue; }

            best = candidate;
            bestZ = z;
        }

        return best;
    }
}
