using UnityEngine;

/// <summary>
/// Fits a card's art fully inside the card face's art window, without cropping any of it.
///
/// Every sprite in Assets/Card Art is square, and the card face's Image slot is a landscape band -
/// showing the whole icon inside that shape means scaling it down by whichever axis would otherwise
/// overflow and letting the other axis fall short of the window, rather than stretching or cropping
/// to fill it exactly. The empty margin that leaves on the long axis is a deliberate trade: a card
/// that shows 100% of its art with a sliver of the card's own background peeking through the sides
/// reads better than one that fills the window but has cut the top and bottom off the artwork to do
/// it.
/// </summary>
public static class CardArt
{
    /// <summary>
    /// The uniform scale that fits `sprite` entirely inside a `window`-sized box (world units) without
    /// cropping or distorting it. Multiply the sprite's renderer's transform.localScale by this - see
    /// CardViewer.ApplyArt, which switches to Simple draw mode for real art so the transform (not
    /// SpriteRenderer.size, which only Sliced/Tiled read) is what controls the final size.
    /// </summary>
    public static float ContainScale(Sprite sprite, Vector2 window)
    {
        if (sprite == null) { return 1f; }

        Vector2 native = sprite.bounds.size;

        if (native.x <= 0f || native.y <= 0f || window.x <= 0f || window.y <= 0f) { return 1f; }

        return Mathf.Min(window.x / native.x, window.y / native.y);
    }
}
