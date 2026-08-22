using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One enemy in the next-wave preview strip at the top-left of the HUD - see NextWavePanel. Shows the
/// enemy's portrait in a circle; hovering it explains who it is and pulses the board edge it will
/// arrive on red, so the player can see both what is coming and where.
///
/// Pooled by NextWavePanel rather than destroyed between refreshes - the same pooling contract
/// StatusChip documents, and for the same reason: SetActive(false) fires no OnPointerExit, so hiding a
/// circle has to clear its tooltip and board pulse by hand.
/// </summary>
public class WaveCircle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image portraitImage;

    private Vector2Int spawnCell;

    private string enemyName;

    private int maxHealth;

    private Canvas canvas;

    private bool hovered;

    /// <summary>
    /// Points this circle at one wave placement. Reads the prefab's own Character without instantiating
    /// it - CharacterOption.Portrait does the same thing for the character-select screen - so building
    /// the preview costs nothing beyond a GetComponent.
    /// </summary>
    public void Bind(EnemyPlacement placement, Canvas hostCanvas)
    {
        canvas = hostCanvas;
        spawnCell = placement.cell;

        gameObject.SetActive(true);

        if (placement.prefab == null || !placement.prefab.TryGetComponent(out Character character))
        {
            enemyName = "???";
            maxHealth = 0;
            SetPortrait(null);
            return;
        }

        enemyName = character.DisplayName;
        maxHealth = character.MaxHealth;
        SetPortrait(ResolvePortrait(placement.prefab, character));

        // Bind can run while this circle is already hovered - NextWavePanel rebinds pooled circles in
        // place on every TurnAdvanced rather than hiding and re-showing them - so a stale tooltip from
        // the previous wave has to be refreshed rather than left quoting the old enemy.
        if (hovered) { PushTooltip(); }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Matches TileSelector.OnMouseEnter and TotemTooltip's own gate: refuse a *new* hover while a
        // modal is up. BattleManager.ClearStuckHover backstops the case where the modal opens while the
        // cursor is already resting here.
        if (BattleManager.Instance != null && BattleManager.Instance.InputLocked) { return; }

        hovered = true;
        PushTooltip();

        if (GridManager.Instance != null) { GridManager.Instance.ShowSpawnWarning(spawnCell); }
    }

    public void OnPointerExit(PointerEventData eventData) => Clear();

    /// <summary>
    /// Pooled, never destroyed - see the class comment. SetActive(false) fires no OnPointerExit, so this
    /// clears the tooltip and the board pulse by hand before hiding, the same as StatusChip.Hide.
    /// </summary>
    public void Hide()
    {
        Clear();
        gameObject.SetActive(false);
    }

    private void OnDisable() => Clear();

    private void Clear()
    {
        if (!hovered) { return; }

        hovered = false;

        if (TooltipManager.Instance != null) { TooltipManager.Instance.Hide(this); }
        if (GridManager.Instance != null) { GridManager.Instance.ClearSpawnWarning(); }
    }

    private void PushTooltip()
    {
        if (TooltipManager.Instance == null || canvas == null) { return; }

        TooltipContent content = new TooltipContent()
            .Header(enemyName)
            .Stat("HEALTH", maxHealth.ToString());

        // Right, not the Above default: this circle sits in the screen's top-left corner, where Above
        // and its Below flip both fail AnchoredPlacement's horizontal fit check for the same reason -
        // see AnchoredPlacement.Place's comment on EndTurnButton/PartyPortraitPanel. Neither side then
        // switches, and the leftover clamp used to drag the box back down over the circle it was
        // meant to sit beside. Right is the side this corner actually has room on.
        TooltipManager.Instance.Show(
            this,
            content,
            TooltipAnchor.Of((RectTransform)transform, canvas, TooltipSide.Right),
            TooltipPriority.Hovered);
    }

    /// <summary>
    /// No enemy prefab authors Character.Portrait today, so falling back to the prefab's own board
    /// sprite is what keeps a wave circle from rendering blank - and keeps working the day a new enemy
    /// is added without portrait art of its own. Two-line null checks throughout: SpriteRenderer and
    /// Sprite are UnityEngine.Objects, and GetComponent on one that is missing returns a fake-null that
    /// `??` would not catch. See CLAUDE.md.
    /// </summary>
    private static Sprite ResolvePortrait(GameObject prefab, Character character)
    {
        Sprite portrait = character.Portrait;

        if (portrait != null) { return portrait; }

        SpriteRenderer boardSprite = prefab.GetComponent<SpriteRenderer>();

        return boardSprite != null ? boardSprite.sprite : null;
    }

    private void SetPortrait(Sprite sprite)
    {
        if (portraitImage == null) { return; }

        portraitImage.sprite = sprite;
        portraitImage.enabled = sprite != null;
    }
}
