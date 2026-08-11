using UnityEngine;

/// <summary>
/// The tile's colour. One component owns the SpriteRenderer so nothing fights over it.
///
/// Three layers plus a hover tint: a base colour saying whether this tile is a legal target for the
/// selected card, an area colour on top of that saying this tile would actually be hit by the card's
/// footprint as currently aimed, and hover on top of both. Leaving the tile returns it to whatever it
/// was under the cursor rather than a hard-coded one - that is what lets a range or area highlight
/// survive the mouse passing over it.
/// </summary>
public class TileSelector : MonoBehaviour
{
    [Tooltip("Resting colour. Matches the alpha authored on the tile prefab.")]
    [SerializeField] private Color idleColor = new(1f, 1f, 1f, 0.08627451f);

    [Tooltip("A legal target for the card currently selected.")]
    [SerializeField] private Color inRangeColor = new(0.3f, 1f, 0.5f, 0.35f);

    [Tooltip("Would actually be hit by the selected card's area footprint, aimed at the hovered tile.")]
    [SerializeField] private Color areaColor = new(1f, 0.25f, 0.2f, 0.45f);

    [SerializeField] private Color hoverColor = new(1f, 1f, 0f, 0.5f);

    private SpriteRenderer spriteRenderer;

    /// Cached so OnMouseEnter/Exit can report themselves through BattleManager.OnTileHovered without a
    /// GetComponent call every frame the cursor moves.
    private GridTile gridTile;

    private bool isInRange;

    private bool isInArea;

    private bool isHovered;

    /// Called by GridManager for every tile when a card is selected or put down.
    public void SetInRange(bool value)
    {
        if (isInRange == value) { return; }

        isInRange = value;
        ApplyColor();
    }

    /// Called by GridManager.ShowAreaPreview while a card with an area effect is selected and a tile is
    /// hovered - the tiles its footprint would actually cover, aimed at the cursor.
    public void SetInArea(bool value)
    {
        if (isInArea == value) { return; }

        isInArea = value;
        ApplyColor();
    }

    /// Called by whatever is standing on this tile and stealing its OnMouseEnter/OnMouseExit with a
    /// collider of its own - a Totem's hitbox, say. Lets the tile still light up under the cursor
    /// even though it never received the mouse event itself.
    public void SetHovered(bool value)
    {
        if (isHovered == value) { return; }

        isHovered = value;
        ApplyColor();
    }

    private void Awake()
    {
        // Awake, not Start: GridManager builds the grid in its own Awake and a highlight can arrive
        // before any Start has run.
        spriteRenderer = GetComponent<SpriteRenderer>();
        gridTile = GetComponent<GridTile>();
        ApplyColor();
    }

    private void ApplyColor()
    {
        if (spriteRenderer == null) { return; }

        spriteRenderer.color = isHovered ? hoverColor : isInArea ? areaColor : isInRange ? inRangeColor : idleColor;
    }

    private void OnMouseEnter()
    {
        // Nothing on the board lights up while a modal owns the screen - a reward panel or a
        // notification. A tile glowing under one reads as a tile you could click, and
        // BattleManager.OnTileClicked would refuse it.
        //
        // Only the enter is gated. OnMouseExit stays live on purpose, so a tile already lit when the
        // panel opened still clears itself when the cursor leaves rather than staying stuck on - and
        // for the case where the cursor never moves at all, BattleManager.ClearStuckHover sweeps the
        // board the frame the lock goes up.
        if (BattleManager.Instance != null && BattleManager.Instance.InputLocked) { return; }

        isHovered = true;
        ApplyColor();

        if (BattleManager.Instance != null) { BattleManager.Instance.OnTileHovered(gridTile); }
    }

    private void OnMouseExit()
    {
        isHovered = false;
        ApplyColor();

        // Deliberately not gated on InputLocked, same reasoning as the rest of this method - a
        // preview left lit behind a modal that opened without the cursor moving is ClearStuckHover's
        // job, but a preview that should clear because the cursor genuinely left must not get stuck.
        if (BattleManager.Instance != null) { BattleManager.Instance.OnTileHovered(null); }
    }
}
