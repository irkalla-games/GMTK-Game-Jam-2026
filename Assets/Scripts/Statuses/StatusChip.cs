using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One status icon with its stack count, as shown in SelectedCharacterPanel's row.
///
/// A component rather than the panel reaching into the prefab's children by index - the same reason
/// CardViewer exists instead of ActiveHandViewer poking at a card's SpriteRenderers. Two fields and
/// one method is small, but the alternative is the panel knowing this prefab's hierarchy.
///
/// It owns no layout. Where a chip sits is the panel's business, because only the panel knows how
/// many there are and how wide a row may be. Same for its tooltip: the chip reports hover and shows
/// whatever sentence it was handed, because only the panel knows whose statuses these are.
/// </summary>
public class StatusChip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image icon;

    [Tooltip("Stack count, badged in the corner of the icon.")]
    [SerializeField] private TMP_Text count;

    /// Cached because the panel positions every visible chip on each refresh.
    public RectTransform Rect { get; private set; }

    /// What this chip explains, and where the box should sit. Rebuilt by the panel on every refresh, so
    /// a Parry charge spent while the cursor is resting on the chip updates the sentence underneath it.
    private TooltipContent tooltip;

    private TooltipAnchor tooltipAnchor;

    private bool hovered;

    private void Awake()
    {
        Rect = (RectTransform)transform;
    }

    /// <summary>
    /// Hands this chip its explanation. Separate from Show because the panel may have art for a status
    /// and no glossary entry, or the reverse - neither should suppress the other.
    ///
    /// Re-shows immediately when the cursor is already on the chip, which is what keeps the numbers in
    /// the sentence honest: the panel refreshes on every resolved action, and without this the tooltip
    /// would keep quoting whatever the status said when the cursor arrived.
    /// </summary>
    public void Bind(TooltipContent content, TooltipAnchor anchor)
    {
        tooltip = content;
        tooltipAnchor = anchor;

        if (hovered) { Push(); }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        Push();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        Clear();
    }

    /// <summary>
    /// Points this chip at a status. `sprite` may be null - a status with no art authored yet still
    /// shows its count rather than vanishing, which is what makes the icon asset safe to fill in
    /// gradually.
    /// </summary>
    public void Show(Sprite sprite, int stacks)
    {
        gameObject.SetActive(true);

        if (icon != null)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        if (count != null) { count.text = stacks.ToString(); }
    }

    public void Hide()
    {
        // Chips are pooled and never destroyed, so a hidden one still owning a tooltip request would
        // leave a box describing a status the newly selected character does not have. SetActive(false)
        // does not fire OnPointerExit, so this has to clear it by hand.
        hovered = false;
        Clear();

        gameObject.SetActive(false);
    }

    private void OnDisable() => Clear();

    private void Push()
    {
        if (TooltipManager.Instance == null || tooltip == null) { return; }

        TooltipManager.Instance.Show(this, tooltip, tooltipAnchor, TooltipPriority.Hovered);
    }

    private void Clear()
    {
        if (TooltipManager.Instance != null) { TooltipManager.Instance.Hide(this); }
    }
}
