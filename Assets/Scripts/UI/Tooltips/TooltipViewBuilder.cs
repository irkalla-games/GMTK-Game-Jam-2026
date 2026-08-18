using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small factory helpers for the tooltip panel's runtime-built chrome - one rect, one label, one rule,
/// created the same way everywhere so TooltipSectionView and TooltipAreaFigureView do not each invent
/// their own version of "a TMP_Text with these five properties set."
///
/// Runtime object creation rather than prefabs: a tooltip section's row mix changes per caller (three
/// stats and a figure today, a run of terms tomorrow), which is exactly the kind of shape prefabs are
/// bad at - see PanelPalette's own doc comment for why this system's styling lives in code, not an
/// asset, in the first place.
/// </summary>
internal static class TooltipViewBuilder
{
    public static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    public static TMP_Text CreateLabel(Transform parent, string name, TMP_FontAsset font, float size,
        float spacing, Color color, FontStyles style)
    {
        RectTransform rect = CreateRect(name, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.characterSpacing = spacing;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    public static Image CreateRule(Transform parent, string name, Color color)
    {
        Image image = CreateFill(parent, name, color);

        LayoutElement element = image.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = PanelPalette.RuleHeight;
        element.flexibleHeight = 0f;

        return image;
    }

    public static Image CreateFill(Transform parent, string name, Color color)
    {
        RectTransform rect = CreateRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }
}
