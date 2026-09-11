using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a second card prefab from nothing - its shapes baked here rather than borrowed from the
/// Dark UI pack, so the face can match the design page's geometry instead of approximating it with
/// whatever panel sprites happened to be to hand.
///
/// Deliberately additive. It writes CardFaceV2.prefab and never touches CardPrefab.prefab, so the two
/// can sit side by side until one wins; swapping between them is a single reference on
/// ActiveHandViewer.
///
/// Why bake the sprites: the existing face is assembled from `White Stop`, `64.png` and
/// `White Cauldron`, each with its own silhouette, corner radius and import scale. Matching a spec
/// built from CSS rounded rectangles means owning those radii, which means generating them. The
/// rounded rects come out 9-sliced, so a plate stretches to any size without its corners smearing -
/// the one thing a non-sliced rounded rect always gets wrong.
///
/// Sizes itself to the existing card's real footprint rather than to the page's pixel dimensions, so
/// it drops into the same hand layout. The page fixes the *proportions*; the old prefab fixes the
/// scale.
/// </summary>
public static class CardFaceV2Builder
{
    private const string SourcePrefab = "Assets/Prefabs/UI/CardPrefab.prefab";
    private const string TargetPrefab = "Assets/Prefabs/UI/CardFaceV2.prefab";
    private const string SpriteFolder = "Assets/Sprites/CardFace";

    private const string BodyPath = SpriteFolder + "/CardBody.png";
    private const string PlatePath = SpriteFolder + "/CardPlate.png";
    private const string PipPath = SpriteFolder + "/CardPip.png";
    private const string HatchPath = SpriteFolder + "/CardHatch.png";
    private const string StripePath = SpriteFolder + "/CardStripe.png";

    private const string FontPath =
        "Assets/Extra Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    private const string BowPath = "Assets/Extra Assets/Dark UI/New Icons/White Bow.png";

    /// The same person glyph the original card face uses for a self-targeted card, so the two faces
    /// teach one vocabulary rather than two.
    private const string SelfPath = "Assets/Extra Assets/Dark UI/New Icons/White Person 2.png";

    /// Left unset, matching the original face. CardViewer falls back to the bow and drops the digit,
    /// which is a better answer than a stand-in glyph that means nothing to the player yet.
    private const string AnywherePath = null;
    private const string LockPath = "Assets/Extra Assets/Dark UI/New Icons/White Lock.png";
    private const string GlossaryPath = "Assets/Scripts/UI/Tooltips/Glossary.asset";

    // ---- palette, lifted straight off the design page ----
    private static readonly Color Body = new(0.659f, 0.545f, 0.310f);      // #A88B4F
    private static readonly Color Frame = new(0.090f, 0.071f, 0.039f);     // #17120A
    private static readonly Color Plate = new(0.388f, 0.278f, 0.063f);     // #634710
    private static readonly Color Pip = new(0.220f, 0.514f, 0.863f);       // #3883DC
    private static readonly Color PipInk = new(0.027f, 0.082f, 0.133f);    // #071522
    private static readonly Color Ink = new(0.965f, 0.945f, 0.902f);       // #F6F1E6

    /// <summary>
    /// The range glyph and its digit, read as one mark.
    ///
    /// Dark rather than the green it started as, because this row sits directly on the tan body
    /// rather than on a plate: a deep warm brown clears roughly 7:1 against #A88B4F, where the green
    /// and the cream both sat nearer 2.5:1 and went muddy at hand scale. Warm rather than black so it
    /// belongs to the card's own palette instead of reading as a UI overlay dropped on top.
    /// </summary>
    private static readonly Color GlyphInk = new(0.141f, 0.106f, 0.055f);  // #241B0E

    private static readonly Color Hatch = new(0f, 0f, 0f, 0.34f);

    // ---- proportions, as fractions of the card's own height unless noted ----
    private const float Pad = 0.030f;          // page padding: 8px on a ~282px card
    private const float Gap = 0.021f;          // page gap: 6px
    private const float TopRow = 0.106f;       // the 30px cost-pip row
    private const float NameRow = 0.092f;
    private const float ArtRow = 0.315f;
    private const float GlyphSize = 0.068f;    // the 19px svg
    private const float FrameWidth = 0.011f;   // the 3px border
    private const float PlateInsetX = 0.030f;  // of card *width*

    /// <summary>
    /// The scale every text object on the card carries, as a fraction of card height, with the point
    /// sizes below then read against it.
    ///
    /// Two numbers rather than one because TMP's point size is not a world measurement: a
    /// TextMeshPro left at scale 1 renders a line roughly a tenth of its font size in world units, so
    /// setting fontSize from card units directly produces text about ten times too big. Shrinking the
    /// transform and keeping ordinary point sizes is the arrangement the original card already uses,
    /// and it keeps these numbers recognisable as font sizes.
    /// </summary>
    private const float TextScale = 0.05f;

    private const float CostFont = 15f;
    private const float NameFont = 11f;
    private const float DescriptionFont = 9f;
    private const float LockFont = 12f;
    private const float DigitFont = 12f;

    [MenuItem("Tools/Cards/Build Card Face V2 (new prefab)")]
    public static void Build()
    {
        BakeSprites();

        Vector2 card = MeasureExistingCard();

        if (card == Vector2.zero)
        {
            Debug.LogError("Card face V2: could not measure the existing card, aborting.");
            return;
        }

        GameObject root = Compose(card);

        Directory.CreateDirectory(Path.GetDirectoryName(TargetPrefab));
        PrefabUtility.SaveAsPrefabAsset(root, TargetPrefab);
        Object.DestroyImmediate(root);

        AssetDatabase.Refresh();

        Debug.Log($"Card face V2 written to {TargetPrefab} at {card.x:0.###} x {card.y:0.###} units. "
                  + "Point ActiveHandViewer.cardPrefab at it to try it in a hand.");
    }

    // ================================================================
    // Choosing which face the game uses
    // ================================================================

    [MenuItem("Tools/Cards/Use Card Face V2")]
    public static void UseV2() => SwapCardPrefab(TargetPrefab, "Card Face V2");

    /// The way back. The two faces are interchangeable by design - same CardViewer, same fields - so
    /// switching is a reference swap rather than a migration, and trying V2 costs nothing.
    [MenuItem("Tools/Cards/Use Original Card Face")]
    public static void UseOriginal() => SwapCardPrefab(SourcePrefab, "the original card face");

    /// <summary>
    /// Repoints every `cardPrefab` reference in the project at one prefab.
    ///
    /// Works on the scene *assets*, opening each one it needs rather than searching whatever happens
    /// to be loaded. That distinction is the whole point: the card screens live in Game.unity, but
    /// the Editor is usually sitting in MainMenu.unity, and a search of the open scene finds nothing,
    /// reports nothing changed, and leaves Play Mode loading the old references off disk.
    ///
    /// Four components hold a reference today - the hand, the reward screen, the removal screen and
    /// the pile browser - but they are found by *having a cardPrefab field* rather than by type, so a
    /// fifth screen added later is picked up without editing this.
    /// </summary>
    private static void SwapCardPrefab(string prefabPath, string label)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Card face: exit Play Mode first - scenes cannot be edited while playing.");
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        CardViewer viewer = prefab != null ? prefab.GetComponent<CardViewer>() : null;

        if (viewer == null)
        {
            Debug.LogError($"Card face: no CardViewer on {prefabPath}. "
                           + "Run Tools > Cards > Build Card Face V2 first if you have not yet.");
            return;
        }

        // Scenes get opened and closed below, so anything unsaved has to be dealt with first.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return; }

        int total = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
        {
            total += SwapInScene(AssetDatabase.GUIDToAssetPath(guid), viewer);
        }

        // Prefabs too, in case a card screen is ever pulled out of the scene into one of its own.
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
        {
            total += SwapInPrefab(AssetDatabase.GUIDToAssetPath(guid), viewer);
        }

        if (total == 0)
        {
            Debug.LogWarning($"Card face: found no cardPrefab reference that was not already {label}.");
            return;
        }

        AssetDatabase.SaveAssets();

        Debug.Log($"Card face: {total} reference(s) now use {label}, saved to disk.");
    }

    /// <summary>
    /// Swaps every cardPrefab reference in one scene asset and saves it.
    ///
    /// Opens the scene additively when it is not already loaded, and closes it again afterwards, so
    /// running this from any scene has the same result and leaves the Editor where it found it.
    /// </summary>
    private static int SwapInScene(string scenePath, CardViewer viewer)
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool alreadyOpen = scene.IsValid() && scene.isLoaded;

        if (!alreadyOpen) { scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive); }

        int changed = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            changed += Repoint(root, viewer);
        }

        if (changed > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"Card face: {changed} reference(s) updated in {scenePath}");
        }

        if (!alreadyOpen) { EditorSceneManager.CloseScene(scene, true); }

        return changed;
    }

    private static int SwapInPrefab(string prefabPath, CardViewer viewer)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);

        if (contents == null) { return 0; }

        int changed;

        try
        {
            changed = Repoint(contents, viewer);

            if (changed > 0) { PrefabUtility.SaveAsPrefabAsset(contents, prefabPath); }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        if (changed > 0) { Debug.Log($"Card face: {changed} reference(s) updated in {prefabPath}"); }

        return changed;
    }

    /// <summary>
    /// Points every `cardPrefab` field under `root` at `viewer`.
    ///
    /// Found by field name rather than by component type, so this needs no list of which screens show
    /// cards - and inactive children are included, since the reward and removal screens spend nearly
    /// all their time disabled, which is exactly when a search that skipped them would miss both.
    /// </summary>
    private static int Repoint(GameObject root, CardViewer viewer)
    {
        int count = 0;

        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            // Null for a component whose script is missing - common enough in a jam project, and it
            // would throw the moment SerializedObject touched it.
            if (behaviour == null) { continue; }

            SerializedObject serialized = new(behaviour);
            SerializedProperty property = serialized.FindProperty("cardPrefab");

            if (property == null) { continue; }
            if (property.propertyType != SerializedPropertyType.ObjectReference) { continue; }
            if (property.objectReferenceValue == viewer) { continue; }

            property.objectReferenceValue = viewer;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            count++;
        }

        return count;
    }

    /// <summary>
    /// The existing card's real world footprint, so V2 lands the same size in a hand rather than the
    /// design page's pixel dimensions, which have no meaning here.
    /// </summary>
    private static Vector2 MeasureExistingCard()
    {
        GameObject old = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);

        if (old == null) { return Vector2.zero; }

        Transform wrapper = old.transform.Find("Wrapper");
        Transform background = wrapper != null ? wrapper.Find("CardBackground") : null;
        SpriteRenderer renderer = background != null ? background.GetComponent<SpriteRenderer>() : null;

        if (renderer == null || renderer.sprite == null) { return Vector2.zero; }

        Vector2 native = renderer.sprite.bounds.size;
        Vector3 scale = renderer.transform.localScale;

        // Times the Wrapper's own scale, since that is between the card art and the prefab root.
        float wrapperScale = wrapper != null ? wrapper.localScale.x : 1f;

        return new Vector2(native.x * scale.x * wrapperScale, native.y * scale.y * wrapperScale);
    }

    // ================================================================
    // Composition
    // ================================================================

    private static GameObject Compose(Vector2 card)
    {
        float halfW = card.x * 0.5f;
        float halfH = card.y * 0.5f;
        float pad = card.y * Pad;
        float gap = card.y * Gap;
        float plateW = card.x - card.x * PlateInsetX * 2f;

        GameObject root = new("CardFaceV2");

        SortingGroup group = root.AddComponent<SortingGroup>();
        group.sortingLayerName = SortingLayers.Cards;
        group.sortingOrder = 0;

        BoxCollider2D hitbox = root.AddComponent<BoxCollider2D>();
        hitbox.size = card;

        CardViewer viewer = root.AddComponent<CardViewer>();

        // One unscaled parent for everything on the face. CardViewer.FaceRoot finds it through
        // RangeIndicator's parent, and every offset it writes is measured in this space - which only
        // works because nothing between here and the card art carries a scale of its own.
        GameObject face = Child(root.transform, "Face");

        Sprite bodySprite = Load<Sprite>(BodyPath);
        Sprite plateSprite = Load<Sprite>(PlatePath);
        Sprite pipSprite = Load<Sprite>(PipPath);
        Sprite hatchSprite = Load<Sprite>(HatchPath);
        TMP_FontAsset font = Load<TMP_FontAsset>(FontPath);

        // The frame is a slightly larger copy of the body behind it, so only its margin shows. Same
        // trick CardViewer's playable ring uses, and the reason both want a solid silhouette rather
        // than a hollow one - a hollow shape scaled up draws a second ring instead of a thicker edge.
        float frame = card.y * FrameWidth;

        SpriteRenderer border = Plate9(face.transform, "Border", bodySprite,
            new Vector2(card.x + frame * 2f, card.y + frame * 2f), Vector2.zero, Frame, 0);

        Plate9(face.transform, "Body", bodySprite, card, Vector2.zero, Body, 1);

        // ---- top row ----
        float pipD = card.y * TopRow;
        float rowY = halfH - pad - pipD * 0.5f;
        float pipX = -halfW + pad + pipD * 0.5f;

        Simple(face.transform, "PipRim", pipSprite, pipD + frame * 1.6f, new Vector2(pipX, rowY), Frame, 3);
        Simple(face.transform, "Pip", pipSprite, pipD, new Vector2(pipX, rowY), Pip, 4);

        float textScale = card.y * TextScale;

        TMP_Text costText = Text(face.transform, "Cost", font, new Vector2(pipX, rowY),
            new Vector2(pipD, pipD), textScale, CostFont, PipInk, 6);

        float glyphD = card.y * GlyphSize;
        float digitW = card.x * 0.11f;
        float digitX = halfW - pad - digitW * 0.5f;
        float glyphX = digitX - digitW * 0.5f - glyphD * 0.5f;
        float markerX = glyphX - glyphD - card.x * 0.02f;

        SpriteRenderer rangeIcon = Simple(face.transform, "RangeIndicator", Load<Sprite>(BowPath),
            glyphD, new Vector2(glyphX, rowY), GlyphInk, 4);

        // ---- plates, stacked down ----
        float cursor = rowY - pipD * 0.5f - gap;

        float nameH = card.y * NameRow;
        float nameY = cursor - nameH * 0.5f;
        cursor = nameY - nameH * 0.5f - gap;

        Plate9(face.transform, "NamePlate", plateSprite, new Vector2(plateW, nameH),
            new Vector2(0f, nameY), Plate, 3);

        TMP_Text nameText = Text(face.transform, "Name", font, new Vector2(0f, nameY),
            new Vector2(plateW * 0.92f, nameH), textScale, NameFont, Ink, 6);

        float artH = card.y * ArtRow;
        float artY = cursor - artH * 0.5f;
        cursor = artY - artH * 0.5f - gap;

        SpriteRenderer art = Plate9(face.transform, "Image", hatchSprite, new Vector2(plateW, artH),
            new Vector2(0f, artY), Hatch, 2);

        float bottom = -halfH + pad;
        float descH = Mathf.Max(cursor - bottom, card.y * 0.14f);
        float descY = bottom + descH * 0.5f;

        Plate9(face.transform, "DescriptionPlate", plateSprite, new Vector2(plateW, descH),
            new Vector2(0f, descY), Plate, 3);

        // Flush with the plate's left edge and the full height of it, so the bar is the coloured end
        // of the description rather than a block laid over it. Inset by even a little, or stopped
        // short of the plate's height, and the eye reads two objects instead of one.
        float stripeW = card.x * 0.045f;
        float stripeX = -plateW * 0.5f + stripeW * 0.5f;

        // The text then starts past the bar rather than being centred on the whole plate, which is
        // what stops a short description from sitting half on top of it.
        float textPad = card.x * 0.03f;
        float textLeft = -plateW * 0.5f + stripeW + textPad;
        float textRight = plateW * 0.5f - textPad;

        TMP_Text descText = Text(face.transform, "Description", font,
            new Vector2((textLeft + textRight) * 0.5f, descY),
            new Vector2(textRight - textLeft, descH - card.y * 0.014f),
            textScale, DescriptionFont, Ink, 6);

        TooltipLinkText links = descText.gameObject.AddComponent<TooltipLinkText>();

        // ---- lock badge, hidden until a Cooldown or Dormant card needs it ----
        float lockD = card.y * 0.075f;
        float lockX = halfW - pad - lockD * 0.5f;
        float lockY = -halfH + pad + lockD * 0.5f;

        SpriteRenderer lockBadge = Simple(face.transform, "LockBadge", Load<Sprite>(LockPath),
            lockD, new Vector2(lockX, lockY), Ink, 7);

        TMP_Text lockCounter = Text(face.transform, "LockCounter", font, new Vector2(lockX, lockY),
            new Vector2(lockD, lockD), textScale, LockFont, Frame, 8);

        lockBadge.gameObject.SetActive(false);
        lockCounter.gameObject.SetActive(false);

        WireViewer(viewer, group, nameText, descText, cost: costText, art, rangeIcon, border, hitbox,
            links, lockBadge, lockCounter, bodySprite,
            new Vector3(markerX, rowY, 0f), glyphD,
            new Vector3(digitX, rowY, 0f), textScale,
            new Vector3(stripeX, descY, 0f), new Vector2(stripeW, descH));

        return root;
    }

    /// Fills in every serialized reference CardViewer needs. Through SerializedObject because they are
    /// private [SerializeField]s - the same route CardSheetImporter takes into a CardData.
    private static void WireViewer(CardViewer viewer, SortingGroup group, TMP_Text name, TMP_Text description,
        TMP_Text cost, SpriteRenderer image, SpriteRenderer rangeIcon, SpriteRenderer border,
        Collider2D hitbox, TooltipLinkText links, SpriteRenderer lockBadge, TMP_Text lockCounter,
        Sprite bodySprite, Vector3 markerPosition, float markerSize, Vector3 digitPosition,
        float textScale, Vector3 stripePosition, Vector2 stripeSize)
    {
        SerializedObject so = new(viewer);

        Ref(so, "sortingGroup", group);
        Ref(so, "cardName", name);
        Ref(so, "description", description);
        Ref(so, "cost", cost);
        Ref(so, "image", image);
        Ref(so, "rangeIndicator", rangeIcon);
        Ref(so, "border", border);
        Ref(so, "hitbox", hitbox);
        Ref(so, "descriptionLinks", links);
        Ref(so, "lockBadge", lockBadge);
        Ref(so, "lockCounter", lockCounter);

        Ref(so, "bowIcon", Load<Sprite>(BowPath));
        Ref(so, "selfIcon", Load<Sprite>(SelfPath));
        Ref(so, "anywhereIcon", Load<Sprite>(AnywherePath));
        Ref(so, "outlineSprite", bodySprite);
        Ref(so, "stripeSprite", Load<Sprite>(StripePath));
        Ref(so, "glossary", Load<Glossary>(GlossaryPath));

        Vec3(so, "areaIconLocalPosition", markerPosition);
        Vec3(so, "rangeDigitLocalPosition", digitPosition);
        Vec3(so, "stripeLocalPosition", stripePosition);

        Float(so, "areaIconSize", markerSize);
        Float(so, "rangeDigitScale", textScale);
        Float(so, "rangeDigitFontSize", DigitFont);
        Float(so, "outlineScale", 1.06f);
        Float(so, "hoverScale", 1.7f);

        SerializedProperty stripe = so.FindProperty("stripeSize");
        if (stripe != null) { stripe.vector2Value = stripeSize; }

        // Matched to the glyph, not to the card's other text: "bow 5" is one statement, and two
        // colours across it would read as two.
        SerializedProperty digitColor = so.FindProperty("rangeDigitColor");
        if (digitColor != null) { digitColor.colorValue = GlyphInk; }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Ref(SerializedObject so, string field, Object value)
    {
        SerializedProperty p = so.FindProperty(field);

        if (p != null) { p.objectReferenceValue = value; }
        else { Debug.LogWarning($"Card face V2: CardViewer has no field '{field}'."); }
    }

    private static void Vec3(SerializedObject so, string field, Vector3 value)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p != null) { p.vector3Value = value; }
    }

    private static void Float(SerializedObject so, string field, float value)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p != null) { p.floatValue = value; }
    }

    // ================================================================
    // Element helpers
    // ================================================================

    private static GameObject Child(Transform parent, string name)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);

        return go;
    }

    /// A 9-sliced rounded rectangle at an explicit world size - the shape every plate on this card is.
    private static SpriteRenderer Plate9(Transform parent, string name, Sprite sprite, Vector2 size,
        Vector2 position, Color color, int order)
    {
        GameObject go = Child(parent, name);
        go.transform.localPosition = position;

        SpriteRenderer r = go.AddComponent<SpriteRenderer>();
        r.sprite = sprite;
        r.color = color;
        r.drawMode = SpriteDrawMode.Sliced;
        r.size = size;
        r.sortingLayerName = SortingLayers.Cards;
        r.sortingOrder = order;

        return r;
    }

    /// A sprite drawn at a uniform size, scaled rather than sliced - for the round and glyph pieces,
    /// which have no border to stretch and must not distort.
    private static SpriteRenderer Simple(Transform parent, string name, Sprite sprite, float size,
        Vector2 position, Color color, int order)
    {
        GameObject go = Child(parent, name);
        go.transform.localPosition = position;

        SpriteRenderer r = go.AddComponent<SpriteRenderer>();
        r.sprite = sprite;
        r.color = color;
        r.sortingLayerName = SortingLayers.Cards;
        r.sortingOrder = order;

        if (sprite != null && sprite.bounds.size.x > 0.0001f)
        {
            go.transform.localScale = Vector3.one * (size / sprite.bounds.size.x);
        }

        return r;
    }

    /// <summary>
    /// A world-space TextMeshPro sized to a box on the card.
    ///
    /// Left at scale 1 with the point size doing the work, rather than the 0.15-scale-plus-small-font
    /// arrangement the old prefab uses. One less multiplier between an authored number and what shows
    /// up on screen, and it is what lets CardViewer write the range digit's position in plain face
    /// units.
    /// </summary>
    private static TMP_Text Text(Transform parent, string name, TMP_FontAsset font, Vector2 position,
        Vector2 box, float scale, float fontSize, Color color, int order)
    {
        GameObject go = new(name, typeof(RectTransform), typeof(TextMeshPro));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = Vector3.one * scale;

        TextMeshPro text = go.GetComponent<TextMeshPro>();

        if (font != null) { text.font = font; }

        // The box arrives in face units; sizeDelta is read in the text's own scaled space, so the
        // scale has to be divided back out or the rect ends up as oversized as the glyphs were.
        text.rectTransform.sizeDelta = scale > 0.0001f ? box / scale : box;

        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;

        // Auto-size shrinks a long description to fit and never grows past the authored size, so a
        // two-word card and a four-line one still read as the same typeface at the same weight.
        text.enableAutoSizing = true;
        text.fontSizeMin = fontSize * 0.3f;
        text.fontSizeMax = fontSize;
        text.raycastTarget = false;

        Renderer r = go.GetComponent<Renderer>();
        r.sortingLayerName = SortingLayers.Cards;
        r.sortingOrder = order;

        return text;
    }

    /// An empty path means "deliberately none" - see AnywherePath - so it answers null without the
    /// warning a genuinely missing asset earns.
    private static T Load<T>(string path) where T : Object
    {
        if (string.IsNullOrEmpty(path)) { return null; }

        T asset = AssetDatabase.LoadAssetAtPath<T>(path);

        if (asset == null) { Debug.LogWarning($"Card face V2: missing {typeof(T).Name} at {path}"); }

        return asset;
    }

    // ================================================================
    // Sprite baking
    // ================================================================

    /// <summary>
    /// Writes the four shapes this face is built from as real .png assets.
    ///
    /// Files rather than textures generated at runtime, so they are inspectable, re-importable and
    /// visible to anyone opening the prefab - a procedural sprite that only exists during Play Mode
    /// makes a prefab that cannot be judged in the Editor.
    /// </summary>
    private static void BakeSprites()
    {
        Directory.CreateDirectory(SpriteFolder);

        Write(BodyPath, RoundedRect(412, 564, 22), 24);
        Write(PlatePath, RoundedRect(256, 128, 10), 12);
        Write(PipPath, Disc(128), 0);
        Write(HatchPath, HatchedRect(256, 128, 8, 10), 12);

        // Rounded on the left only, and 9-sliced with a small margin, so the audience bar can sit
        // flush inside the description plate's left edge and curve with it instead of squaring off
        // across the plate's own corner. Its right edge stays straight - that edge is where the bar
        // meets the text, not an outside edge of anything.
        Write(StripePath, LeftRoundedRect(24, 96, 3), new Vector4(3, 3, 1, 3));

        AssetDatabase.Refresh();
    }

    private static void Write(string path, Texture2D texture, int border) =>
        Write(path, texture, new Vector4(border, border, border, border));

    private static void Write(string path, Texture2D texture, Vector4 border)
    {
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        if (AssetImporter.GetAtPath(path) is not TextureImporter importer) { return; }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.filterMode = FilterMode.Bilinear;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;

        // The 9-slice margins, as (left, bottom, right, top). All zero for the disc, which must scale
        // as a whole or stop being round.
        importer.spriteBorder = border;

        importer.SaveAndReimport();
    }

    /// White rounded rectangle on transparent, edges anti-aliased by sampling the distance field
    /// rather than by supersampling - one evaluation per pixel and no visible stair-stepping.
    private static Texture2D RoundedRect(int width, int height, int radius)
    {
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixels[y * width + x] = new Color(1f, 1f, 1f, RoundedAlpha(x, y, width, height, radius));
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return texture;
    }

    /// The same rounded rectangle, filled with 45-degree stripes - the art slot's placeholder hatch.
    private static Texture2D HatchedRect(int width, int height, int radius, int stripe)
    {
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float shape = RoundedAlpha(x, y, width, height, radius);

                // Two alternating densities, matching the page's two-stop repeating gradient.
                bool dense = ((x + y) / stripe) % 2 == 0;

                pixels[y * width + x] = new Color(1f, 1f, 1f, shape * (dense ? 1f : 0.38f));
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return texture;
    }

    /// <summary>
    /// A rectangle rounded down its left edge and square down its right.
    ///
    /// The audience bar's shape. Only the left corners are struck, because that edge is an outside
    /// edge of the description plate and has to follow its curve; the right edge is an interior
    /// join against the text and should not be rounded off away from it.
    /// </summary>
    private static Texture2D LeftRoundedRect(int width, int height, int radius)
    {
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // No right-hand term, so a pixel near the right edge never counts as being in a
                // corner and that whole edge stays straight.
                float dx = Mathf.Max(radius - x, 0f);
                float dy = Mathf.Max(Mathf.Max(radius - y, y - (height - 1 - radius)), 0f);

                float alpha = dx <= 0f && dy <= 0f
                    ? 1f
                    : Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);

                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return texture;
    }

    private static Texture2D Disc(int size)
    {
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];

        float centre = (size - 1) * 0.5f;
        float radius = size * 0.5f - 1f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));

                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance + 0.5f));
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return texture;
    }

    /// Coverage of a rounded rectangle at one pixel: 1 inside, 0 outside, a soft step across the edge.
    private static float RoundedAlpha(int x, int y, int width, int height, int radius)
    {
        // Distance outside the inner rectangle the corner arcs are struck from, per axis.
        float dx = Mathf.Max(Mathf.Max(radius - x, x - (width - 1 - radius)), 0f);
        float dy = Mathf.Max(Mathf.Max(radius - y, y - (height - 1 - radius)), 0f);

        // Both zero means the pixel is in the straight body of the shape, where there is no edge.
        if (dx <= 0f && dy <= 0f) { return 1f; }

        float distance = Mathf.Sqrt(dx * dx + dy * dy);

        return Mathf.Clamp01(radius - distance + 0.5f);
    }
}
