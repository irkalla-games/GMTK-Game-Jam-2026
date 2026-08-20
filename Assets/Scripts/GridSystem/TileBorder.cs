using UnityEngine;

/// <summary>
/// The dark outline around one tile, telling it apart from the one next to it.
///
/// It earns its place because the tiles now meet edge to edge. They used to be drawn 17% smaller than
/// the pitch they were laid out on, so a gutter of bare floor separated every diamond and did this
/// job by accident. Sizing the tile to the cell - which is what stopped the highlights overlapping -
/// closed that gutter, and a continuous wash across the whole board has no visible cell boundaries at
/// all. This puts them back deliberately, and as a line rather than as dead space.
///
/// A separate renderer, not a fourth colour on TileSelector, for the reason TileAuraOverlay gives:
/// the border and the highlight have to show at once, so they cannot contend for one channel. It sits
/// *above* the tile fill and above both overlays, so a tile inside a totem's aura is still legibly a
/// tile.
///
/// Inert - no component, no behaviour, nothing to update. Just a child renderer that GridManager makes
/// once per tile and never speaks to again.
/// </summary>
public static class TileBorder
{
    /// Matches Unity's IsometricDiamond, which is what the tile prefab draws: 256 x 128 at 256 pixels
    /// per unit, i.e. 1.0 x 0.5 world units before the tile's own scale. Generating at the same size
    /// and PPU means the border child needs no scale of its own - it inherits the tile's, and so is
    /// exactly one cell however cellSize is tuned.
    private const int TextureWidth = 256;

    private const int TextureHeight = 128;

    /// Built once and shared by every tile on every board. A Texture2D per tile would be 100+ textures
    /// describing the identical shape.
    private static Sprite outline;

    /// <summary>
    /// Adds the outline to a tile, or does nothing if the tile has no renderer to sit against.
    ///
    /// `thickness` is a fraction of the diamond's half-width, measured horizontally - 0.05 is about
    /// 6px of the 128px half-width. Measured that way rather than perpendicular to the edge because
    /// the diamond's two edge slopes differ, so there is no single perpendicular to measure from.
    /// </summary>
    public static void AttachTo(GridTile tile, Color color, float thickness)
    {
        if (tile == null) { return; }

        SpriteRenderer tileRenderer = tile.GetComponent<SpriteRenderer>();

        if (tileRenderer == null) { return; }

        GameObject go = new("TileBorder");

        // worldPositionStays: false so the child keeps an identity transform and lands exactly on the
        // tile, inheriting its scale - the same trick TileAuraOverlay uses.
        go.transform.SetParent(tile.transform, false);

        // SetParent does not carry the layer, and a fresh GameObject starts on Default - which the
        // board camera culls and the UI camera would draw somewhere else entirely. See GameLayers.
        go.layer = tile.gameObject.layer;

        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = Outline(thickness);
        renderer.sharedMaterial = tileRenderer.sharedMaterial;
        renderer.sortingLayerName = SortingLayers.Grid;

        // Above the tile fill, which sits at the cell's base order, and so above both overlays - they
        // are at base - 1 and base - 2. GridManager.DepthStride reserves the room; the next cell
        // forward is a full stride away, so this can never climb over it.
        renderer.sortingOrder = tileRenderer.sortingOrder + 1;
        renderer.color = color;
    }

    /// <summary>
    /// The shared outline sprite: a hollow 2:1 diamond, opaque white on its edge and transparent
    /// everywhere else, tinted per tile by the renderer.
    ///
    /// Generated rather than authored so it always matches the diamond the tiles actually draw. An
    /// imported outline PNG would be a second description of the same shape, free to drift from the
    /// first the moment either changed.
    /// </summary>
    private static Sprite Outline(float thickness)
    {
        if (outline != null) { return outline; }

        Texture2D texture = new(TextureWidth, TextureHeight, TextureFormat.RGBA32, false)
        {
            // Point, and clamped: this is pixel art sitting on a pixel-art board, and a bilinear
            // filter would smear the one-pixel edge into a grey haze at the zoom levels a big board
            // pulls back to.
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        float halfWidth = TextureWidth / 2f;
        float halfHeight = TextureHeight / 2f;
        float centreX = (TextureWidth - 1) / 2f;
        float centreY = (TextureHeight - 1) / 2f;

        float inner = 1f - Mathf.Clamp(thickness, 0.001f, 0.5f);

        Color32[] pixels = new Color32[TextureWidth * TextureHeight];
        Color32 line = new(255, 255, 255, 255);
        Color32 clear = new(255, 255, 255, 0);

        for (int y = 0; y < TextureHeight; y++)
        {
            for (int x = 0; x < TextureWidth; x++)
            {
                // The diamond as an L1 distance in normalized axes: 1 exactly on the edge, less inside,
                // more outside. A band just inside that edge is the border.
                float distance = Mathf.Abs(x - centreX) / halfWidth
                                 + Mathf.Abs(y - centreY) / halfHeight;

                bool onEdge = distance <= 1f && distance >= inner;

                pixels[y * TextureWidth + x] = onEdge ? line : clear;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        outline = Sprite.Create(texture,
                                new Rect(0f, 0f, TextureWidth, TextureHeight),
                                new Vector2(0.5f, 0.5f),
                                TextureWidth);

        return outline;
    }
}
