using UnityEngine;

/// <summary>
/// The red wash a tile shows when something dangerous is happening to it. A deliberate copy of
/// TileAuraOverlay's shape: a second SpriteRenderer rather than a fourth colour on TileSelector,
/// because a warned tile can also be a legal target for the card in hand right now and both have to
/// show at once.
///
/// Sits one order *above* the tile rather than below, unlike TileAuraOverlay/TileEffectOverlay - a
/// warning is what the player is being told to look at right now, and has to read through the green
/// in-range tint rather than being washed out underneath it.
///
/// Two independent channels on the one renderer, composed in Update:
///
///   SetActive  a looping pulse while an incoming wave's spawn point is previewed - see
///              GridManager.ShowSpawnWarning and WaveCircle.
///   Flash      a single stab as an enemy's attack resolves on this tile - see
///              BattleManager.Execute and GridTile.FlashThreat.
///
/// Two channels rather than two components because the sorting budget has no room for a fourth
/// above-tile renderer: DepthStride is 4 and TileBorder, TileHatchOverlay and this already share
/// base + 1 - see TileHatchOverlay.DepthAboveTile. Composing them here also means a flash can never
/// destroy a spawn warning running underneath it; the warning simply resumes when the stab ends.
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

    /// <summary>
    /// The attack stab's colour - a brighter scarlet than the spawn warning above, so the two never
    /// read as the same statement.
    ///
    /// Deliberately *not* shifted toward orange to make it "hotter": WallOfFlamesTileEffect.OverlayColor
    /// is already (1, 0.35, 0.1), and a second orange-red on the board would trade one collision for
    /// another. The heat is carried by brightness, peak alpha and speed instead.
    /// </summary>
    private static readonly Color ThreatColor = new(1f, 0.28f, 0.28f, 1f);

    /// Crest of the attack stab. Well above the spawn warning's MaxAlpha, since it has one pass to be
    /// noticed rather than a loop the eye can catch up with.
    private const float ThreatPeakAlpha = 0.85f;

    /// Seconds for the whole stab, rise and fall. Comfortably inside BattleManager.stepDuration, so it
    /// finishes before the next enemy acts.
    private const float FlashDuration = 0.45f;

    private SpriteRenderer spriteRenderer;

    private bool active;

    private bool flashing;

    /// When the current stab began. A per-instance clock, which is the opposite of what the looping
    /// pulse below does on purpose - see Update.
    private float flashStart;

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

    /// Starts or stops the looping spawn-warning pulse. Guarded like TileSelector.SetInRange, so a
    /// caller re-asserting the same state every frame - GridManager.ShowSpawnWarning included - costs
    /// nothing. Does not touch the flash channel: turning a warning off mid-stab leaves the stab to
    /// finish, and Update clears the renderer once both are quiet.
    public void SetActive(bool value)
    {
        if (active == value) { return; }

        active = value;

        // Cleared here rather than left to the next Update, as this always did - but never over a live
        // stab, which owns the renderer until its own falling edge.
        if (!active && !flashing && spriteRenderer != null) { spriteRenderer.color = Color.clear; }
    }

    /// <summary>
    /// Fires one attack stab on this tile. Re-calling restarts the envelope rather than being ignored,
    /// which is the right answer for a tile two enemies hit in a row: two swings should read as two.
    /// </summary>
    public void Flash()
    {
        flashing = true;
        flashStart = Time.time;
    }

    /// <summary>
    /// Composes both channels, loudest wins: a stab overrides the spawn warning for its half second,
    /// and the warning resumes underneath rather than having been cleared.
    ///
    /// The two use deliberately different clocks. The looping pulse reads the shared Time.time so every
    /// warned tile in one row, column or corner crests together - the reasoning AuraPulse.RingAlpha
    /// documents for keeping same-coloured totem auras in phase. The stab reads its own start time,
    /// which is not a violation of that rule but the same goal reached the other way: every tile in one
    /// footprint calls Flash in the same frame, so they are already in phase, and a discrete event has a
    /// beginning that a free-running shared clock cannot express.
    /// </summary>
    private void Update()
    {
        if (spriteRenderer == null) { return; }

        float threat = 0f;

        if (flashing)
        {
            float t = (Time.time - flashStart) / Mathf.Max(FlashDuration, 0.01f);

            if (t >= 1f)
            {
                // The stab's falling edge, and the only frame this channel clears on. A warning running
                // underneath repaints further down this same pass, so handing the renderer back never
                // shows as a blink.
                flashing = false;
                spriteRenderer.color = Color.clear;
            }
            else { threat = Mathf.Sin(t * Mathf.PI) * ThreatPeakAlpha; }
        }

        if (threat > 0f)
        {
            Color stab = ThreatColor;
            stab.a = threat;

            spriteRenderer.color = stab;
            return;
        }

        // Both channels quiet. Early-out rather than re-clearing, so an overlay that has been attached
        // to a tile and gone idle costs nothing per frame - every tile ever hit keeps one for the rest
        // of the battle.
        if (!active) { return; }

        float cycle = Mathf.Max(Period, 0.01f);
        float bump = (Mathf.Sin(Time.time * Mathf.PI * 2f / cycle) + 1f) * 0.5f;

        Color color = WarningColor;
        color.a = Mathf.Lerp(MinAlpha, MaxAlpha, bump);

        spriteRenderer.color = color;
    }
}
