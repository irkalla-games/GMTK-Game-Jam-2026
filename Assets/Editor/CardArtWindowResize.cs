using UnityEditor;
using UnityEngine;

/// <summary>
/// Narrows the card face's art window without touching anything else on the card.
///
/// CardViewer shows card art contain-fit rather than cropped (see CardArt.ContainScale) - the whole
/// square icon is scaled down to fit inside the window on whichever axis would otherwise overflow,
/// leaving an empty margin on the other. A landscape window is wider than a square icon needs, so
/// that margin lands on the left and right; narrowing the window towards square shrinks that margin,
/// letting the art fill more of the space it sits in even though none of it was ever being cropped.
///
/// A small, surgical LoadPrefabContents/SaveAsPrefabAsset edit rather than a re-run of
/// CardFaceV2Builder.Build() - that command composes the *entire* face from scratch (re-baking
/// CardBody.png etc.), and this only needs to move one child's width. CardViewer reads the window's
/// size straight off this SpriteRenderer at runtime (see CardViewer.ArtWindowSize), so nothing
/// downstream needs to know the new numbers - it just follows.
///
/// Idempotent: the target width is re-derived from the card's own measured size every run, never
/// multiplied against whatever is already there, so running this twice does not narrow the window
/// twice.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
static class CardArtWindowResize
{
    private const string PrefabPath = "Assets/Prefabs/UI/CardFaceV2.prefab";

    /// CardFaceV2Builder's own plate width - card width minus a PlateInsetX margin on each side
    /// (0.030 x 2). Every plate (Name, Description) still uses this fraction; only the art window is
    /// narrowed further below.
    private const float PlateWidthFraction = 0.94f;

    /// How much narrower than the other plates the art window is - a 15% cut, requested so a square
    /// icon crops less off its top and bottom. Applied on top of PlateWidthFraction, not in place of it.
    private const float ArtNarrowing = 0.85f;

    [MenuItem("Tools/Cards/3 - Narrow Card Art Window")]
    private static void Narrow()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Card art window resize: exit Play Mode first - prefab edits made in play do not persist.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);

        if (root == null)
        {
            Debug.LogError($"Card art window resize: could not load {PrefabPath}.");
            return;
        }

        try
        {
            Transform face = root.transform.Find("Face");
            Transform body = face != null ? face.Find("Body") : null;
            Transform image = face != null ? face.Find("Image") : null;

            if (body == null || image == null)
            {
                Debug.LogError("Card art window resize: expected Face/Body and Face/Image children - "
                                + "found one or both missing. Has CardFaceV2's hierarchy changed?");
                return;
            }

            SpriteRenderer bodyRenderer = body.GetComponent<SpriteRenderer>();
            SpriteRenderer imageRenderer = image.GetComponent<SpriteRenderer>();

            if (bodyRenderer == null || imageRenderer == null)
            {
                Debug.LogError("Card art window resize: Body or Image has no SpriteRenderer.");
                return;
            }

            float cardWidth = bodyRenderer.size.x;
            float targetWidth = cardWidth * PlateWidthFraction * ArtNarrowing;

            Vector2 size = imageRenderer.size;
            size.x = targetWidth;
            imageRenderer.size = size;

            // Recentred explicitly rather than assumed, so this stays idempotent even if a previous
            // run (or a hand edit) left the local X position off zero.
            Vector3 localPosition = image.localPosition;
            localPosition.x = 0f;
            image.localPosition = localPosition;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);

            Debug.Log($"Card art window resize: art window is now {targetWidth:F4} x {size.y:F4} "
                      + $"world units (aspect {targetWidth / size.y:F3}:1), narrowed {1f - ArtNarrowing:P0} "
                      + $"from the other plates' {cardWidth * PlateWidthFraction:F4} width. Height unchanged.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
    }
}
