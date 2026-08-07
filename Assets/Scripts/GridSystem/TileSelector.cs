using UnityEngine;

/// <summary>
/// The tile's colour. One component owns the SpriteRenderer so nothing fights over it.
///
/// Two layers: a base colour saying whether this tile is a legal target for the selected card, and a
/// hover tint on top. Leaving the tile returns it to its base colour rather than a hard-coded one -
/// that is what lets a range highlight survive the mouse passing over it.
/// </summary>
public class TileSelector : MonoBehaviour
{
    [Tooltip("Resting colour. Matches the alpha authored on the tile prefab.")]
    [SerializeField] private Color idleColor = new(1f, 1f, 1f, 0.08627451f);

    [Tooltip("A legal target for the card currently selected.")]
    [SerializeField] private Color inRangeColor = new(0.3f, 1f, 0.5f, 0.35f);

    [SerializeField] private Color hoverColor = new(1f, 1f, 0f, 0.5f);

    private SpriteRenderer spriteRenderer;

    private bool isInRange;

    private bool isHovered;

    /// Called by GridManager for every tile when a card is selected or put down.
    public void SetInRange(bool value)
    {
        if (isInRange == value) { return; }

        isInRange = value;
        ApplyColor();
    }

    private void Awake()
    {
        // Awake, not Start: GridManager builds the grid in its own Awake and a highlight can arrive
        // before any Start has run.
        spriteRenderer = GetComponent<SpriteRenderer>();
        ApplyColor();
    }

    private void ApplyColor()
    {
        if (spriteRenderer == null) { return; }

        spriteRenderer.color = isHovered ? hoverColor : isInRange ? inRangeColor : idleColor;
    }

    private void OnMouseEnter()
    {
        // Nothing on the board lights up while a modal owns the screen - a tile glowing under a reward
        // panel reads as a tile you could click, and BattleManager.OnTileClicked would refuse it.
        //
        // Only the enter is gated. OnMouseExit stays live on purpose, so a tile already lit when the
        // panel opened still clears itself when the cursor leaves rather than staying stuck on.
        if (BattleManager.Instance != null && BattleManager.Instance.InputLocked) { return; }

        isHovered = true;
        ApplyColor();
    }

    private void OnMouseExit()
    {
        isHovered = false;
        ApplyColor();
    }
}
