using UnityEngine;

/// <summary>
/// The wash an aura leaves on one tile. A deliberate mirror of TileSelector: one component owns one
/// SpriteRenderer so nothing fights over it.
///
/// It has to be a second renderer rather than a fourth colour on TileSelector, because the two answer
/// different questions and both have to show at once - a tile can be inside an aura *and* be a legal
/// target for the card in your hand. Sitting one order below the tile's own sprite is what puts the
/// aura underneath the green range highlight instead of contending for the same channel.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class TileAuraOverlay : MonoBehaviour
{
    /// How far below the tile's own sprite this sits, *relative to that tile* rather than absolutely.
    ///
    /// It used to be the flat order -1, which worked only while every tile in the game sat at Grid/0.
    /// Tiles now carry a per-cell order so the board sorts back to front - see
    /// GridManager.CellSortingOrder - so a fixed -1 would put every aura in the game behind every
    /// tile, including the ones in front of it. GridManager.DepthStride reserves the room this and
    /// TileEffectOverlay borrow from each cell.
    private const int DepthBelowTile = -1;

    private SpriteRenderer spriteRenderer;

    private Color tint = Color.clear;

    /// <summary>
    /// Builds the overlay for a tile, or returns null if the tile has no sprite worth copying.
    ///
    /// A child object rather than another component on the tile, so it can carry its own renderer
    /// without touching the one TileSelector owns. No collider is added: the tile keeps its
    /// PolygonCollider2D to itself and so keeps receiving OnMouseEnter and OnMouseDown.
    /// </summary>
    public static TileAuraOverlay AttachTo(GridTile tile)
    {
        if (tile == null) { return null; }

        SpriteRenderer tileRenderer = tile.GetComponent<SpriteRenderer>();

        if (tileRenderer == null || tileRenderer.sprite == null) { return null; }

        GameObject go = new("AuraOverlay");

        // worldPositionStays: false keeps the fresh object's identity transform, which as a child of
        // the tile is exactly the same diamond at exactly the same place - no need to know anything
        // about how the grid is laid out.
        go.transform.SetParent(tile.transform, false);

        // SetParent does not carry the layer across, and a fresh GameObject starts on Default - which
        // the board camera culls and the fixed UI camera happily draws, putting the overlay somewhere
        // else on screen entirely. See GameLayers.
        go.layer = tile.gameObject.layer;

        SpriteRenderer overlayRenderer = go.AddComponent<SpriteRenderer>();
        overlayRenderer.sprite = tileRenderer.sprite;
        overlayRenderer.sharedMaterial = tileRenderer.sharedMaterial;
        overlayRenderer.sortingLayerName = SortingLayers.Grid;
        overlayRenderer.sortingOrder = tileRenderer.sortingOrder + DepthBelowTile;
        overlayRenderer.color = Color.clear;

        return go.AddComponent<TileAuraOverlay>();
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// Called by AuraPulse every frame while this tile is covered. Guarded like
    /// TileSelector.SetInRange - the pulse resolves to the same colour on plenty of frames.
    public void SetTint(Color value)
    {
        if (tint == value) { return; }

        tint = value;

        if (spriteRenderer != null) { spriteRenderer.color = value; }
    }

    public void Clear() => SetTint(Color.clear);
}
