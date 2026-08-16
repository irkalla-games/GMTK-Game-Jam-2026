using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Lays the card prefab's face out from one set of proportions, rather than by dragging children
/// around until they look right.
///
/// A menu command instead of hand-edited prefab YAML for the reason every asset edit in this project
/// is: the Editor holds the prefab open, and a file written underneath it is a race. It is also
/// re-runnable, which is what makes the numbers below tunable - change a constant, run it again, and
/// every element moves together instead of drifting apart one nudge at a time.
///
/// Everything is measured as a fraction of the card's own size, read off CardBackground's real
/// sprite bounds. That is what keeps this correct without anyone having to know the pixels-per-unit
/// each sprite in the Dark UI pack happens to be imported at - the numbers here are proportions, and
/// the sprite answers the question of how big a proportion is.
///
/// The one space that matters is the Wrapper's. Every visible child of a card is a *sibling* under
/// it, so a single (x, y) in that space positions any of them; the plates and the text are not
/// parent and child, which is exactly why the text had drifted off its plates before this existed.
/// </summary>
public static class CardFaceLayout
{
    private const string PrefabPath = "Assets/Prefabs/UI/CardPrefab.prefab";

    /// The filled disc behind the energy number. Replaces the cauldron, which read as an object
    /// rather than as a pip.
    private const string CostPipSprite = "Assets/Extra Assets/Dark UI/Free/CIRCLE4PXMED.png";

    // Proportions of the card's own height, unless noted. They sum to a shade under 1 so the
    // description gets whatever is left rather than a fixed height that could disagree with the rest.
    private const float MarginY = 0.035f;
    private const float MarginXFraction = 0.055f;   // of card *width*
    private const float Gap = 0.022f;
    private const float PipDiameter = 0.115f;
    private const float GlyphDiameter = 0.085f;
    private const float NameHeight = 0.105f;
    private const float ArtHeight = 0.315f;

    /// Breathing room between a plate's edge and the text inside it, as a fraction of card width.
    private const float TextPadX = 0.045f;

    private const float TextPadY = 0.02f;

    [MenuItem("Tools/Cards/Apply Card Face Layout")]
    public static void Apply()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);

        if (root == null)
        {
            Debug.LogError($"Card face layout: could not load {PrefabPath}");
            return;
        }

        try
        {
            if (Layout(root)) { PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool Layout(GameObject root)
    {
        Transform wrapper = root.transform.Find("Wrapper");

        if (wrapper == null)
        {
            Debug.LogError("Card face layout: no Wrapper child - the face's shared parent is missing.");
            return false;
        }

        SpriteRenderer background = Sprite(wrapper, "CardBackground");

        if (background == null || background.sprite == null)
        {
            Debug.LogError("Card face layout: CardBackground has no sprite to measure the card from.");
            return false;
        }

        Vector2 card = SizeInParent(background);
        Vector2 centre = background.transform.localPosition;

        float halfW = card.x * 0.5f;
        float halfH = card.y * 0.5f;
        float marginX = card.x * MarginXFraction;
        float marginY = card.y * MarginY;
        float gap = card.y * Gap;

        // The single vertical axis everything lines up on. Read off the background rather than
        // assumed to be zero, because the whole face sits at a slight offset inside the Wrapper.
        float axis = centre.x;
        float plateWidth = card.x - marginX * 2f;

        // The border shares the background's footprint exactly - it is the same rectangle, drawn on
        // top - so any drift between the two shows as a lopsided frame.
        SpriteRenderer border = Sprite(wrapper, "CardBorder");

        if (border != null)
        {
            border.transform.localPosition = background.transform.localPosition;
            border.transform.localScale = background.transform.localScale;
        }

        // ---- top row: cost pip on the left, area marker and range glyph on the right ----

        float pipD = card.y * PipDiameter;
        float glyphD = card.y * GlyphDiameter;
        float rowY = centre.y + halfH - marginY - pipD * 0.5f;

        SpriteRenderer costPip = Sprite(wrapper, "CostBackground");

        if (costPip != null)
        {
            Sprite disc = AssetDatabase.LoadAssetAtPath<Sprite>(CostPipSprite);

            if (disc != null) { costPip.sprite = disc; }
            else { Debug.LogWarning($"Card face layout: no sprite at {CostPipSprite}, keeping the old one."); }

            SetSize(costPip, new Vector2(pipD, pipD));
            costPip.transform.localPosition = new Vector3(centre.x - halfW + marginX + pipD * 0.5f, rowY, 0f);
        }

        CentreText(Text(wrapper, "Cost"), costPip != null
            ? costPip.transform.localPosition
            : new Vector3(centre.x - halfW + marginX, rowY, 0f), new Vector2(pipD, pipD));

        // Right cluster, laid out inward from the card's right margin: digit outermost, then the
        // range glyph it belongs to, then the area marker.
        float digitWidth = card.x * 0.11f;
        float digitX = centre.x + halfW - marginX - digitWidth * 0.5f;
        float glyphX = digitX - digitWidth * 0.5f - glyphD * 0.5f;
        float markerX = glyphX - glyphD - card.x * 0.02f;

        SpriteRenderer rangeIcon = Sprite(wrapper, "RangeIndicator");

        if (rangeIcon != null)
        {
            SetSize(rangeIcon, new Vector2(glyphD, glyphD));
            rangeIcon.transform.localPosition = new Vector3(glyphX, rowY, 0f);
        }

        // ---- plates, stacked downward from the top row ----

        float cursor = rowY - pipD * 0.5f - gap;

        float nameH = card.y * NameHeight;
        float nameY = cursor - nameH * 0.5f;
        cursor = nameY - nameH * 0.5f - gap;

        PlacePlate(Sprite(wrapper, "NameBackground"), axis, nameY, plateWidth, nameH);
        CentreText(Text(wrapper, "Name"), new Vector3(axis, nameY, 0f),
            new Vector2(plateWidth - card.x * TextPadX, nameH));

        float artH = card.y * ArtHeight;
        float artY = cursor - artH * 0.5f;
        cursor = artY - artH * 0.5f - gap;

        PlacePlate(Sprite(wrapper, "Image"), axis, artY, plateWidth, artH);

        // The description takes the rest of the card, so the bottom margin matches the top by
        // construction rather than by a fourth constant that could fall out of step.
        float bottom = centre.y - halfH + marginY;
        float descH = Mathf.Max(cursor - bottom, card.y * 0.12f);
        float descY = bottom + descH * 0.5f;

        PlacePlate(Sprite(wrapper, "DescriptionBackground"), axis, descY, plateWidth, descH);
        CentreText(Text(wrapper, "Description"), new Vector3(axis, descY, 0f),
            new Vector2(plateWidth - card.x * TextPadX, descH - card.y * TextPadY));

        WriteViewerFields(root, new Vector3(markerX, rowY, 0f), glyphD,
            new Vector3(digitX, rowY, 0f),
            new Vector3(axis - plateWidth * 0.5f + card.x * 0.012f, descY, 0f),
            new Vector2(card.x * 0.022f, descH * 0.82f));

        Debug.Log($"Card face layout applied - card measured {card.x:0.###} x {card.y:0.###} units.");

        return true;
    }

    /// <summary>
    /// Pushes the computed positions into CardViewer's own serialized fields, so the three elements
    /// it builds at runtime land in the same layout as the children placed above.
    ///
    /// Through SerializedObject rather than the C# properties because these are private
    /// [SerializeField]s - the same reason CardSheetImporter writes card assets this way.
    /// </summary>
    private static void WriteViewerFields(GameObject root, Vector3 markerPosition, float markerSize,
        Vector3 digitPosition, Vector3 stripePosition, Vector2 stripeSize)
    {
        CardViewer viewer = root.GetComponent<CardViewer>();

        if (viewer == null) { return; }

        SerializedObject serialized = new(viewer);

        Set(serialized, "areaIconLocalPosition", markerPosition);
        Set(serialized, "rangeDigitLocalPosition", digitPosition);
        Set(serialized, "stripeLocalPosition", stripePosition);

        SerializedProperty size = serialized.FindProperty("areaIconSize");
        if (size != null) { size.floatValue = markerSize; }

        SerializedProperty stripe = serialized.FindProperty("stripeSize");
        if (stripe != null) { stripe.vector2Value = stripeSize; }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Set(SerializedObject serialized, string field, Vector3 value)
    {
        SerializedProperty property = serialized.FindProperty(field);

        if (property != null) { property.vector3Value = value; }
    }

    /// Centres a plate on the shared axis at `y`, sized to fill `width` x `height`.
    private static void PlacePlate(SpriteRenderer plate, float axis, float y, float width, float height)
    {
        if (plate == null || plate.sprite == null) { return; }

        SetSize(plate, new Vector2(width, height));
        plate.transform.localPosition = new Vector3(axis, y, 0f);
    }

    /// <summary>
    /// Puts a text object exactly on the box it belongs to, and centres it both ways inside it.
    ///
    /// The whole point of the pass: these are siblings of their plates, not children, so nothing was
    /// keeping the two together and the name had drifted a tenth of a unit off its own background.
    /// </summary>
    private static void CentreText(TMP_Text text, Vector3 position, Vector2 box)
    {
        if (text == null) { return; }

        text.rectTransform.localPosition = position;
        text.alignment = TextAlignmentOptions.Center;

        // sizeDelta is in the text's own scaled space, so the box has to be divided back out by
        // whatever scale the object carries - 0.15 on every text on this card.
        float scale = text.rectTransform.localScale.x;

        if (scale > 0.0001f)
        {
            text.rectTransform.sizeDelta = new Vector2(box.x / scale, box.y / scale);
        }
    }

    /// A renderer's size in its parent's space - sprite bounds times its own scale. Reading the
    /// sprite rather than assuming a pixels-per-unit is what makes this work across the mixed import
    /// settings in the Dark UI pack.
    private static Vector2 SizeInParent(SpriteRenderer renderer)
    {
        Vector2 native = renderer.sprite.bounds.size;
        Vector3 scale = renderer.transform.localScale;

        return new Vector2(native.x * scale.x, native.y * scale.y);
    }

    /// Scales a renderer so it measures `target` in its parent's space.
    private static void SetSize(SpriteRenderer renderer, Vector2 target)
    {
        Vector2 native = renderer.sprite.bounds.size;

        if (native.x <= 0.0001f || native.y <= 0.0001f) { return; }

        Vector3 scale = renderer.transform.localScale;
        scale.x = target.x / native.x;
        scale.y = target.y / native.y;

        renderer.transform.localScale = scale;
    }

    private static SpriteRenderer Sprite(Transform wrapper, string child)
    {
        Transform found = wrapper.Find(child);

        if (found == null)
        {
            Debug.LogWarning($"Card face layout: no child named {child}, skipping it.");
            return null;
        }

        return found.GetComponent<SpriteRenderer>();
    }

    private static TMP_Text Text(Transform wrapper, string child)
    {
        Transform found = wrapper.Find(child);

        return found != null ? found.GetComponent<TMP_Text>() : null;
    }
}
