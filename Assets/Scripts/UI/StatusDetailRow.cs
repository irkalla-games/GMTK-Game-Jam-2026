using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One status, written out in full - PartySheetColumn's answer to StatusChip's icon-plus-hover-tooltip
/// chip. Spelled out instead of hidden behind a hover because reading every status across the whole
/// party side by side, without pointing at each one in turn, is the reason the Tab screen exists at all.
///
/// A view only, same split as StatusChip: it owns none of its own layout or which statuses to show,
/// only how to draw the one it is handed.
/// </summary>
public class StatusDetailRow : MonoBehaviour
{
    [SerializeField] private Image icon;

    [SerializeField] private TMP_Text titleLabel;

    [SerializeField] private TMP_Text bodyLabel;

    public RectTransform Rect { get; private set; }

    private void Awake()
    {
        Rect = (RectTransform)transform;
    }

    /// `sprite` may be null - a status with no art authored yet still shows its title and body rather
    /// than vanishing, same reasoning as StatusChip.Show.
    public void Bind(Sprite sprite, string title, string body)
    {
        if (icon != null)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        if (titleLabel != null) { titleLabel.text = title; }

        if (bodyLabel != null) { bodyLabel.text = body; }
    }
}
