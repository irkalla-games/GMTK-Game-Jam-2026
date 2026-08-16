using UnityEngine;

/// <summary>
/// Explains what a totem is doing while the cursor sits on it, in the same popup card keywords and
/// status chips use.
///
/// Totems have no collider of their own - see BattleManager.OnTileClicked, which points out that no
/// character does, and the tile underneath is what receives every mouse event instead. This adds a
/// collider over the totem's art so its body is hoverable, which means it also has to hand back the
/// two things that collider steals from the tile beneath it: the hover tint (SetHovered) and the
/// click itself (forwarded straight to BattleManager.OnTileClicked, so selecting and targeting the
/// totem still work exactly as if the tile itself had been clicked).
/// </summary>
[RequireComponent(typeof(Totem))]
public class TotemTooltip : MonoBehaviour
{
    [SerializeField] private Glossary glossary;

    [Tooltip("What the tooltip is positioned against for hover/click purposes. Falls back to the "
             + "root collider, the same reasoning as CardViewer.hitbox.")]
    [SerializeField] private Collider2D hitbox;

    [Tooltip("Anchors the popup above the health bar - the Overhead child, not the collider, so the "
             + "box sits by the bar regardless of how tall the art hitbox is.")]
    [SerializeField] private RectTransform overheadRect;

    [SerializeField] private Canvas overheadCanvas;

    private Totem totem;

    private Character owner;

    private void Awake()
    {
        totem = GetComponent<Totem>();
        owner = GetComponent<Character>();

        if (hitbox == null) { hitbox = GetComponent<Collider2D>(); }

        // The prefab's world-space Canvas is authored with no camera assigned. TooltipAnchor's rect
        // path projects through canvas.worldCamera for anything but an Overlay canvas, and
        // RectTransformUtility.WorldToScreenPoint(null, ...) on a World Space canvas answers world
        // coordinates rather than screen ones - the tooltip would park in the corner of the screen.
        if (overheadCanvas != null && overheadCanvas.worldCamera == null)
        {
            overheadCanvas.worldCamera = Camera.main;
        }
    }

    private void OnMouseEnter()
    {
        // Same gate as TileSelector.OnMouseEnter: nothing on the board should light up or explain
        // itself while a modal owns the screen. Only the enter is gated - OnMouseExit stays live so a
        // totem already hovered when the panel opened still clears itself on the way out.
        if (BattleManager.Instance != null && BattleManager.Instance.InputLocked) { return; }

        if (TooltipManager.Instance != null)
        {
            TooltipAnchor anchor = TooltipAnchor.Of(overheadRect, overheadCanvas, TooltipSide.Above);
            TooltipManager.Instance.Show(this, BuildContent(), anchor, TooltipPriority.Hovered);
        }

        if (owner != null && owner.Tile != null)
        {
            owner.Tile.SetHovered(true);

            // The totem's collider steals OnMouseEnter from the tile beneath it, so it has to forward
            // to the same hover door TileSelector uses or an area-of-effect preview would never learn
            // the cursor is sitting on a totem-covered tile.
            if (BattleManager.Instance != null) { BattleManager.Instance.OnTileHovered(owner.Tile); }
        }
    }

    private void OnMouseExit()
    {
        HideTooltip();
    }

    private void OnDisable()
    {
        // A totem can die - or the whole scene can tear down - while the cursor is still sitting on
        // it, which never fires OnMouseExit. CardViewer.HideTooltips has the same call from BeginPlay
        // for the same reason: every non-exit way a hover can end still has to clear it.
        HideTooltip();
    }

    private void HideTooltip()
    {
        if (TooltipManager.Instance != null) { TooltipManager.Instance.Hide(this); }

        if (owner != null && owner.Tile != null) { owner.Tile.SetHovered(false); }

        if (BattleManager.Instance != null) { BattleManager.Instance.OnTileHovered(null); }
    }

    private void OnMouseDown()
    {
        // The totem's own hitbox would otherwise swallow the click the tile beneath it is supposed to
        // receive - forward it through the same door every other tile click uses.
        if (BattleManager.Instance != null && owner != null && owner.Tile != null)
        {
            BattleManager.Instance.OnTileClicked(owner.Tile);
        }
    }

    /// <summary>
    /// The same box a card's "Venom Totem" link shows, built by the same Glossary.SummonContent - a
    /// totem's health, remaining lifetime, aura reach, its aura's own status text and any reactions.
    /// The only thing this hover supplies that the card link cannot: the totem's actual remaining
    /// lifetime rather than the authored one, since this totem is already ticking down on the board.
    /// </summary>
    private TooltipContent BuildContent()
    {
        if (glossary == null || totem == null) { return new TooltipContent(); }

        return glossary.SummonContent(gameObject, RemainingLifetime());
    }

    /// Turns left before this totem crumbles on its own, or 0 for a permanent one. SummonedStatus is
    /// only ever applied when SummonEffect.lifetimeTurns was nonzero, so a totem carrying none of it is
    /// exactly the permanent case SummonContent already reads a 0 as.
    private int RemainingLifetime()
    {
        Status summoned = owner != null ? owner.FindStatus(StatusType.Summoned) : null;

        return summoned != null ? summoned.stacks : 0;
    }
}
