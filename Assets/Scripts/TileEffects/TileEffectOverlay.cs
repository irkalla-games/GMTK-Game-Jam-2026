using UnityEngine;

/// <summary>
/// The wash a tile effect leaves on its own tile. A deliberate copy of TileAuraOverlay's shape, sitting
/// one order further back so a totem's aura wash - which can legitimately cover the same tile as a
/// wall - is never fought over the same renderer. See TileAuraOverlay for why this is a second
/// SpriteRenderer rather than a third colour on TileSelector.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class TileEffectOverlay : MonoBehaviour
{
    /// One order below TileAuraOverlay, and like it measured *relative to the tile* rather than
    /// absolutely - tiles now carry a per-cell sorting order so the board draws back to front. See
    /// TileAuraOverlay.DepthBelowTile and GridManager.DepthStride.
    private const int DepthBelowTile = -2;

    private SpriteRenderer spriteRenderer;

    private Color tint = Color.clear;

    /// Builds the overlay for a tile, or returns null if the tile has no sprite worth copying. See
    /// TileAuraOverlay.AttachTo - same reasoning throughout.
    public static TileEffectOverlay AttachTo(GridTile tile)
    {
        if (tile == null) { return null; }

        SpriteRenderer tileRenderer = tile.GetComponent<SpriteRenderer>();

        if (tileRenderer == null || tileRenderer.sprite == null) { return null; }

        GameObject go = new("TileEffectOverlay");

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

        return go.AddComponent<TileEffectOverlay>();
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void SetTint(Color value)
    {
        if (tint == value) { return; }

        tint = value;

        if (spriteRenderer != null) { spriteRenderer.color = value; }
    }

    public void Clear() => SetTint(Color.clear);
}
