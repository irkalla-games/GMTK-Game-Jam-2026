using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One corner badge on an intent slot: a small coloured plate with a glyph, and a stack-count number
/// to its own right. IntentBadgeStack owns a pool of these and computes every position; this class
/// only knows how to build its own three-piece hierarchy and paint whatever it is told.
///
/// Root's pivot is (0, 1) - anchoredPosition marks the plate's own top-left corner - because
/// IntentBadgeStack lays badges out from an icon's bottom-right corner leftward and needs to hand
/// this class one top-left point per badge with no further conversion on either side.
/// </summary>
public class IntentBadge
{
    private RectTransform root;
    private Image plate;
    private Image glyph;
    private TextMeshProUGUI number;

    /// <summary>
    /// Builds the badge's hierarchy, inactive, as a sibling of every other slot's icon window -
    /// `parent` and `anchorPoint` come from IntentStrip and are the same for every slot on the strip,
    /// which is what makes this badge's anchoredPosition directly comparable to an icon window's own.
    /// </summary>
    public void Build(Transform parent, Vector2 anchorPoint, int layer)
    {
        GameObject rootGo = new("IntentBadge", typeof(RectTransform));
        root = (RectTransform)rootGo.transform;
        root.SetParent(parent, false);
        rootGo.layer = layer;
        root.anchorMin = anchorPoint;
        root.anchorMax = anchorPoint;
        root.pivot = new Vector2(0f, 1f);

        GameObject plateGo = new("Plate", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform plateRect = (RectTransform)plateGo.transform;
        plateRect.SetParent(root, false);
        plateGo.layer = layer;
        plateRect.anchorMin = Vector2.zero;
        plateRect.anchorMax = Vector2.one;
        plateRect.pivot = new Vector2(0.5f, 0.5f);
        plateRect.sizeDelta = Vector2.zero;
        plateRect.anchoredPosition = Vector2.zero;
        plate = plateGo.GetComponent<Image>();
        plate.raycastTarget = false;

        GameObject glyphGo = new("Glyph", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform glyphRect = (RectTransform)glyphGo.transform;
        glyphRect.SetParent(root, false);
        glyphGo.layer = layer;
        glyphRect.anchorMin = Vector2.zero;
        glyphRect.anchorMax = Vector2.one;
        glyphRect.pivot = new Vector2(0.5f, 0.5f);
        glyphRect.sizeDelta = Vector2.zero;
        glyphRect.anchoredPosition = Vector2.zero;
        glyph = glyphGo.GetComponent<Image>();
        glyph.raycastTarget = false;
        glyph.preserveAspect = true;

        GameObject numberGo = new("Number", typeof(RectTransform));
        RectTransform numberRect = (RectTransform)numberGo.transform;
        numberRect.SetParent(root, false);
        numberGo.layer = layer;
        // Right of the plate's own box, vertically centred on it - a normal nested anchor within this
        // badge's own root, unaffected by root's own outer anchor scheme in the shared strip frame.
        numberRect.anchorMin = new Vector2(1f, 0.5f);
        numberRect.anchorMax = new Vector2(1f, 0.5f);
        numberRect.pivot = new Vector2(0f, 0.5f);
        number = numberGo.AddComponent<TextMeshProUGUI>();
        number.alignment = TextAlignmentOptions.MidlineLeft;
        number.textWrappingMode = TextWrappingModes.NoWrap;
        number.raycastTarget = false;

        rootGo.SetActive(false);
    }

    /// <summary>
    /// Positions, sizes and paints this badge in one call - IntentBadgeStack recomputes every visible
    /// badge's full geometry on every refresh, so there is no reason to split "move" from "paint".
    /// `topLeft` is in the shared strip frame (see Build); `size` is the plate's own square side.
    /// `showNumber` hides the digit outright for a rider with no count (Push) or while
    /// GameSettings.ShowIntentDamage is off - the glyph and plate stay visible either way.
    /// </summary>
    public void Show(Sprite sprite, Color plateColor, Vector2 topLeft, float size, float fontSize,
                      int amount, bool showNumber, float numberGap, Vector2 numberSize)
    {
        root.gameObject.SetActive(true);
        root.anchoredPosition = topLeft;
        root.sizeDelta = new Vector2(size, size);

        plate.color = plateColor;
        glyph.sprite = sprite;
        glyph.enabled = sprite != null;

        bool visible = showNumber && amount > 0;
        number.gameObject.SetActive(visible);

        if (visible)
        {
            number.text = amount.ToString();
            number.fontSize = fontSize;
            RectTransform numberRect = (RectTransform)number.transform;
            numberRect.anchoredPosition = new Vector2(numberGap, 0f);
            numberRect.sizeDelta = numberSize;
        }
    }

    public void Hide() => root.gameObject.SetActive(false);
}
