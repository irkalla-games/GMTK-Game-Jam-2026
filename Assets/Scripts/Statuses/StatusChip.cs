using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One status icon with its stack count, as shown in SelectedCharacterPanel's row.
///
/// A component rather than the panel reaching into the prefab's children by index - the same reason
/// CardViewer exists instead of ActiveHandViewer poking at a card's SpriteRenderers. Two fields and
/// one method is small, but the alternative is the panel knowing this prefab's hierarchy.
///
/// It owns no layout. Where a chip sits is the panel's business, because only the panel knows how
/// many there are and how wide a row may be.
/// </summary>
public class StatusChip : MonoBehaviour
{
    [SerializeField] private Image icon;

    [Tooltip("Stack count, badged in the corner of the icon.")]
    [SerializeField] private TMP_Text count;

    /// Cached because the panel positions every visible chip on each refresh.
    public RectTransform Rect { get; private set; }

    private void Awake()
    {
        Rect = (RectTransform)transform;
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
        gameObject.SetActive(false);
    }
}
