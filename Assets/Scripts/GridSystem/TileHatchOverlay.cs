using UnityEngine;

/// <summary>
/// The cross-hatch wash a tile shows while it sits inside the selected card's range but cannot actually
/// be clicked - see GridManager.ShowPlayableTiles. Green says "click here"; this says "in reach, but
/// there is nothing here to click", which is what makes a card's shape legible when only one or two
/// tiles in it happen to hold a legal target.
///
/// A second SpriteRenderer rather than a fourth colour on TileSelector, for the reason TileWarningOverlay
/// and TileAuraOverlay both give: a hatched tile still has to take the yellow hover tint and the red
/// area preview on top, and one colour channel cannot carry all of them at once.
///
/// Sits one order *above* the tile fill, like TileWarningOverlay and unlike the aura/effect washes - the
/// stripes have to read through whatever tint the tile itself is wearing rather than be washed out
/// underneath it. Inert once lit: no pulse, no Update. This is information, not an alarm.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class TileHatchOverlay : MonoBehaviour
{
    /// See TileAuraOverlay.DepthBelowTile for why this is relative to the tile's own order rather than a
    /// flat number. Shares base + 1 with TileBorder deliberately: GridManager.DepthStride is 4 and the
    /// cell's four slots are already spoken for, so base + 2 would collide with the *next* cell
    /// forward's effect overlay. Do not widen the stride for this.
    private const int DepthAboveTile = 1;

    /// Black and dim on purpose - it marks the tiles you cannot use, so it reads as the tile being
    /// struck through rather than lit up, and never competes with the green highlight marking the tiles
    /// you can actually click.
    private static readonly Color HatchColor = new(0f, 0f, 0f, 0.4f);

    /// Gap between one hatch line and the next, in texture pixels. Picked rather than tuned: the diamond
    /// is 256 x 128, so a neighbouring tile's overlay sits 128 across and 64 up from this one, and a
    /// period dividing 64 is what carries every line straight on into the next tile instead of breaking
    /// it at the seam. That leaves 16 (dense enough to read as a flat wash once a big board zooms out),
    /// 32, and 64 (barely a line per tile) - so 32.
    private const float Period = 32f;

    /// Thin enough to hold the hatch near a sixth of the tile, which is the density that still reads as
    /// separate lines rather than as a fourth tile colour.
    private const float LineWidth = 3f;

    /// Matches Unity's IsometricDiamond and TileBorder's generated outline: 256 x 128 at 256 pixels per
    /// unit. Generating at the tile's own size and PPU is what lets this child inherit the tile's scale
    /// and land exactly on one cell however cellSize is tuned.
    private const int TextureWidth = 256;

    private const int TextureHeight = 128;

    /// Built once and shared by every tile on every board - one Texture2D describing a shape that is
    /// identical everywhere, the same reasoning TileBorder.outline documents.
    private static Sprite hatch;

    private SpriteRenderer spriteRenderer;

    private bool active;

    /// <summary>
    /// Builds the overlay for a tile, or returns null if the tile has no renderer worth sitting against.
    /// See TileWarningOverlay.AttachTo - same reasoning throughout, including the sorting-layer copy:
    /// SetParent does not carry a GameObject's layer across, and a fresh one starts on Default, which the
    /// board camera culls. No collider is added, so the tile keeps its own PolygonCollider2D and so keeps
    /// receiving OnMouseEnter/OnMouseDown.
    /// </summary>
    public static TileHatchOverlay AttachTo(GridTile tile)
    {
        if (tile == null) { return null; }

        SpriteRenderer tileRenderer = tile.GetComponent<SpriteRenderer>();

        if (tileRenderer == null || tileRenderer.sprite == null) { return null; }

        GameObject go = new("RangeHatchOverlay");

        // worldPositionStays: false so the child keeps an identity transform and lands exactly on the
        // tile, inheriting its scale.
        go.transform.SetParent(tile.transform, false);
        go.layer = tile.gameObject.layer;

        SpriteRenderer overlayRenderer = go.AddComponent<SpriteRenderer>();
        overlayRenderer.sprite = Hatch();
        overlayRenderer.sharedMaterial = tileRenderer.sharedMaterial;
        overlayRenderer.sortingLayerName = SortingLayers.Grid;
        overlayRenderer.sortingOrder = tileRenderer.sortingOrder + DepthAboveTile;
        overlayRenderer.color = Color.clear;

        return go.AddComponent<TileHatchOverlay>();
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// Shows or hides the hatching. Guarded like TileSelector.SetInRange and TileWarningOverlay.SetActive,
    /// so a caller re-asserting the same state - GridManager rebuilding the highlight on every card
    /// selection included - costs nothing.
    public void SetActive(bool value)
    {
        if (active == value) { return; }

        active = value;

        if (spriteRenderer != null) { spriteRenderer.color = active ? HatchColor : Color.clear; }
    }

    /// <summary>
    /// The shared hatch sprite: diagonal lines in both directions, clipped to the tile's diamond and
    /// transparent everywhere else, tinted by the renderer.
    ///
    /// Generated rather than authored for TileBorder.Outline's reason - an imported PNG would be a second
    /// description of the same diamond, free to drift from the first the moment either changed.
    ///
    /// The lines run along the diamond's own two axes - left corner to right corner, and top corner to
    /// bottom corner. Those are the only lines that pass through its corners at all, and on an isometric
    /// board they are the grid's *diagonals*: a screen-horizontal line here joins tiles that are diagonal
    /// neighbours in grid coordinates.
    ///
    /// Deliberately not parallel to the diamond's edges, which is the obvious-looking choice and the
    /// wrong one. Those edges are the grid's own rows and columns, so hatching along them lines up with
    /// every tile boundary on the board and reads as more grid - lines running across and down - rather
    /// than as something drawn over a tile.
    ///
    /// Because Period divides the 64px vertical pitch between neighbouring tiles, the lines continue
    /// straight across tile seams: a run of hatched tiles reads as one pattern rather than as a row of
    /// separately striped diamonds.
    /// </summary>
    private static Sprite Hatch()
    {
        if (hatch != null) { return hatch; }

        Texture2D texture = new(TextureWidth, TextureHeight, TextureFormat.RGBA32, false)
        {
            // Point, and clamped, for TileBorder's reason: this is pixel art on a pixel-art board, and a
            // bilinear filter smears thin lines into a grey haze at the zoom a big board pulls back to.
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        float halfWidth = TextureWidth / 2f;
        float halfHeight = TextureHeight / 2f;
        float centreX = (TextureWidth - 1) / 2f;
        float centreY = (TextureHeight - 1) / 2f;

        Color32[] pixels = new Color32[TextureWidth * TextureHeight];
        Color32 line = new(255, 255, 255, 255);
        Color32 clear = new(255, 255, 255, 0);

        for (int y = 0; y < TextureHeight; y++)
        {
            for (int x = 0; x < TextureWidth; x++)
            {
                float offsetX = x - centreX;
                float offsetY = y - centreY;

                // The diamond as an L1 distance in normalized axes - TileBorder's own test. 1 exactly on
                // the edge, less inside.
                bool insideDiamond = Mathf.Abs(offsetX) / halfWidth + Mathf.Abs(offsetY) / halfHeight <= 1f;

                // Phase-shifted by half a line so one lands centred on offset 0 in each axis - that is
                // what puts a line exactly *through* the diamond's corners rather than just near them.
                bool onLine = Mathf.Repeat(offsetY + LineWidth / 2f, Period) < LineWidth
                              || Mathf.Repeat(offsetX + LineWidth / 2f, Period) < LineWidth;

                pixels[y * TextureWidth + x] = insideDiamond && onLine ? line : clear;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        hatch = Sprite.Create(texture,
                              new Rect(0f, 0f, TextureWidth, TextureHeight),
                              new Vector2(0.5f, 0.5f),
                              TextureWidth);

        return hatch;
    }
}
