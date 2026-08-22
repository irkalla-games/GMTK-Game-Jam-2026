using UnityEngine;

/// <summary>
/// The pulsing red wash a tile shows while an incoming wave's spawn point is being previewed - see
/// GridManager.ShowSpawnWarning and WaveCircle. A deliberate copy of TileAuraOverlay's shape: a second
/// SpriteRenderer rather than a fourth colour on TileSelector, because a warned tile can also be a
/// legal target for the card in hand right now and both have to show at once.
///
/// Sits one order *above* the tile rather than below, unlike TileAuraOverlay/TileEffectOverlay - a
/// spawn warning is what the player is being told to look at right now, and has to read through the
/// green in-range tint rather than being washed out underneath it.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class TileWarningOverlay : MonoBehaviour
{
    /// See TileAuraOverlay.DepthBelowTile for why this is relative to the tile's own order rather than
    /// a flat number - the board sorts back to front per cell, not on a single flat plane.
    private const int DepthAboveTile = 1;

    private static readonly Color WarningColor = new(1f, 0.15f, 0.1f, 1f);

    /// Alpha at the low point of the pulse. Never fully clear while active, so a warned edge still
    /// reads as "on" between crests instead of blinking out.
    private const float MinAlpha = 0.15f;

    private const float MaxAlpha = 0.6f;

    /// Seconds for one full brighten-and-fade cycle.
    private const float Period = 0.8f;

    private SpriteRenderer spriteRenderer;

    private bool active;

    /// <summary>
    /// Builds the overlay for a tile, or returns null if the tile has no sprite worth copying. See
    /// TileAuraOverlay.AttachTo - same reasoning throughout, including the sorting-layer copy: SetParent
    /// does not carry a GameObject's layer across, and a fresh one starts on Default, which the board
    /// camera culls. No collider is added, so the tile keeps its own PolygonCollider2D and so keeps
    /// receiving OnMouseEnter/OnMouseDown.
    /// </summary>
    public static TileWarningOverlay AttachTo(GridTile tile)
    {
        if (tile == null) { return null; }

        SpriteRenderer tileRenderer = tile.GetComponent<SpriteRenderer>();

        if (tileRenderer == null || tileRenderer.sprite == null) { return null; }

        GameObject go = new("SpawnWarningOverlay");

        go.transform.SetParent(tile.transform, false);
        go.layer = tile.gameObject.layer;

        SpriteRenderer overlayRenderer = go.AddComponent<SpriteRenderer>();
        overlayRenderer.sprite = tileRenderer.sprite;
        overlayRenderer.sharedMaterial = tileRenderer.sharedMaterial;
        overlayRenderer.sortingLayerName = SortingLayers.Grid;
        overlayRenderer.sortingOrder = tileRenderer.sortingOrder + DepthAboveTile;
        overlayRenderer.color = Color.clear;

        return go.AddComponent<TileWarningOverlay>();
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// Starts or stops the pulse. Guarded like TileSelector.SetInRange, so a caller re-asserting the
    /// same state every frame - GridManager.ShowSpawnWarning included - costs nothing.
    public void SetActive(bool value)
    {
        if (active == value) { return; }

        active = value;

        if (!active && spriteRenderer != null) { spriteRenderer.color = Color.clear; }
    }

    /// <summary>
    /// Shared Time.time clock rather than a per-instance one, so every warned tile in one row, column
    /// or corner crests together - the same reasoning AuraPulse.RingAlpha documents for keeping
    /// same-coloured totem auras in phase.
    /// </summary>
    private void Update()
    {
        if (!active || spriteRenderer == null) { return; }

        float cycle = Mathf.Max(Period, 0.01f);
        float bump = (Mathf.Sin(Time.time * Mathf.PI * 2f / cycle) + 1f) * 0.5f;

        Color color = WarningColor;
        color.a = Mathf.Lerp(MinAlpha, MaxAlpha, bump);

        spriteRenderer.color = color;
    }
}
