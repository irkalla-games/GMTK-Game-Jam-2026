using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One glyph in CharacterOverheadViewer's small status row: a dark backing plate (the background this
/// component sits on) behind a status sprite (the icon child), matching the enemy intent icon's own
/// IntentBg treatment. No stack count, no tooltip - see CharacterOverheadViewer.RefreshStatusIcons for
/// why this is deliberately simpler than StatusChip.
/// </summary>
public class OverheadStatusIcon : MonoBehaviour
{
    [SerializeField] private Image icon;

    public RectTransform Rect { get; private set; }

    private void Awake() => Rect = (RectTransform)transform;

    public void Show(Sprite sprite)
    {
        if (icon == null) { return; }

        icon.sprite = sprite;
        icon.enabled = sprite != null;
    }
}
