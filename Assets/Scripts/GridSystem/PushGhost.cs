using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A translucent stand-in for a character, shown on the tile a push would land it on - the visual half
/// of GridManager.PlanPush, alongside the red area preview and the damage-loss numbers. Built fresh per
/// preview and destroyed by GridManager.ClearPushPreview, never pooled.
///
/// Clones every SpriteRenderer under the subject rather than moving or disabling the real character -
/// the real one has to keep standing where it actually is until the card is played. A hero is a
/// SortingGroup of six or seven opaque renderers (back arm, weapon, body, legs, head, front arm), so
/// tinting each one's own alpha lets its own parts show through each other where they overlap. Accepted:
/// the ghost only has to say "here", and doing better means a render target or a shader.
/// </summary>
public class PushGhost : MonoBehaviour
{
    private const float GhostAlpha = 0.45f;

    /// <summary>
    /// Builds a ghost of `subject` parented to `destination`, or null if either is missing or the
    /// subject has no sprites worth copying. Skips anything under a Canvas - the subject's overhead
    /// health/status bars ride along in the hierarchy but must not appear in the ghost.
    /// </summary>
    public static PushGhost Create(Character subject, GridTile destination)
    {
        if (subject == null || destination == null || GridManager.Instance == null) { return null; }

        GameObject go = new($"PushGhost ({subject.name})");
        go.transform.SetParent(destination.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.layer = destination.gameObject.layer;

        // GridManager scales every tile so its sprite spans exactly one cell (see GridManager.
        // TileScale), which means anything parented to a tile inherits that factor and draws that much
        // too big. Cancelling it here - and pinning world rotation rather than local - puts this root
        // at world scale 1 with no rotation, so the parts below can be laid out in plain world units
        // and come out exactly the size of the body they copy.
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = InvertScale(destination.transform.lossyScale);

        SortingGroup sortingGroup = go.AddComponent<SortingGroup>();
        sortingGroup.sortingLayerName = SortingLayers.Characters;
        sortingGroup.sortingOrder = GridManager.Instance.CellDepth(destination.Coordinates);

        int copied = 0;

        foreach (SpriteRenderer source in subject.GetComponentsInChildren<SpriteRenderer>())
        {
            if (source.GetComponentInParent<Canvas>() != null) { continue; }

            GameObject part = new(source.name);
            part.transform.SetParent(go.transform, false);
            part.layer = go.layer;

            // The root above is unscaled and unrotated, so local values here are world values. That is
            // what keeps the ghost's parts laid out (back arm behind the body, and so on) at exactly
            // the spacing and size they have on the real character, whatever the tile's own scale is.
            //
            // lossyScale, not localScale, for the same reason one level down: it is the size the
            // renderer actually draws at with every parent folded in, and a negative component - a
            // mirrored puppet, see CharacterAnimator.counterFlip - carries the flip across intact.
            part.transform.localPosition = source.transform.position - subject.transform.position;
            part.transform.localRotation = source.transform.rotation;
            part.transform.localScale = source.transform.lossyScale;

            SpriteRenderer clone = part.AddComponent<SpriteRenderer>();
            clone.sprite = source.sprite;
            clone.sharedMaterial = source.sharedMaterial;
            clone.flipX = source.flipX;
            clone.flipY = source.flipY;
            clone.sortingLayerName = source.sortingLayerName;
            clone.sortingOrder = source.sortingOrder;

            Color tint = source.color;
            tint.a = GhostAlpha;
            clone.color = tint;

            copied++;
        }

        if (copied == 0)
        {
            Destroy(go);
            return null;
        }

        return go.AddComponent<PushGhost>();
    }

    /// Component-wise reciprocal, guarding the degenerate zero. A tile scaled flat on some axis would
    /// otherwise put an infinity into the ghost's transform, which makes it disappear outright rather
    /// than merely look wrong - and a preview that vanishes reads as "no push here", which is a lie.
    private static Vector3 InvertScale(Vector3 scale)
    {
        return new Vector3(
            Mathf.Approximately(scale.x, 0f) ? 1f : 1f / scale.x,
            Mathf.Approximately(scale.y, 0f) ? 1f : 1f / scale.y,
            Mathf.Approximately(scale.z, 0f) ? 1f : 1f / scale.z);
    }
}
