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
    /// Below the tile's own sprite at Grid/0, still above the isometric floor art at Background/10.
    private const int Depth = -1;

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

        SpriteRenderer overlayRenderer = go.AddComponent<SpriteRenderer>();
        overlayRenderer.sprite = tileRenderer.sprite;
        overlayRenderer.sharedMaterial = tileRenderer.sharedMaterial;
        overlayRenderer.sortingLayerName = SortingLayers.Grid;
        overlayRenderer.sortingOrder = Depth;
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
