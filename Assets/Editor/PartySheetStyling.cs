using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Restyles PartySheetColumn.prefab and StatusDetailRow.prefab in place to the same bordered chrome
/// the tooltip panel uses - a status list that scrolls once it overflows, and a "None" line for a hero
/// carrying nothing.
///
/// A separate command from Tools/Battle HUD/Wire Party Portraits, not a change to it:
/// PartyPortraitWiring.EnsureSheetColumnPrefab/EnsureStatusDetailRowPrefab early-return once the asset
/// exists (PartyPortraitWiring.cs:520-524, :476-480), which is the right answer to "does this exist at
/// all" and the wrong one to "does this match the current spec" - adding a force path there would
/// duplicate this command's job. This one edits the existing assets via
/// PrefabUtility.LoadPrefabContents/SaveAsPrefabAsset rather than deleting and recreating them, which
/// is what keeps their file GUIDs - and therefore Game.unity's columnPrefab reference and
/// PartySheetColumn's own rowPrefab reference - intact.
///
/// Idempotent: every object is found by name (or relocated from its pre-restyle location) before being
/// created, and every style value is re-applied unconditionally on every run.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class PartySheetStyling
{
    private const string ColumnPrefabPath = "Assets/Prefabs/UI/PartySheetColumn.prefab";
    private const string RowPrefabPath = "Assets/Prefabs/UI/StatusDetailRow.prefab";
    private const string FontPath = "Assets/Extra Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    [MenuItem("Tools/Battle HUD/Restyle Party Sheet")]
    public static void Restyle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Party sheet styling: exit Play Mode first - prefab edits made in play do not persist.");
            return;
        }

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        if (font == null)
        {
            Debug.LogError($"Party sheet styling: font not found at {FontPath} - check the path still matches " +
                "the project's TMP install.");
            return;
        }

        Sprite chrome = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");

        if (!StyleRow(font))
        {
            Debug.LogError("Party sheet styling: StatusDetailRow.prefab not found - run Tools > Battle HUD > " +
                "Wire Party Portraits first to create it.");
            return;
        }

        if (!StyleColumn(font, chrome))
        {
            Debug.LogError("Party sheet styling: PartySheetColumn.prefab not found - run Tools > Battle HUD > " +
                "Wire Party Portraits first to create it.");
            return;
        }

        AssetDatabase.SaveAssets();

        Debug.Log("Party sheet styling: done - prefabs saved.");
    }

    // ------------------------------------------------------------------------------------------
    // StatusDetailRow.prefab - icon + a name/body stack, sized entirely by its own content now that
    // PartySheetColumn no longer calls Place() to size it by hand.
    // ------------------------------------------------------------------------------------------

    private static bool StyleRow(TMP_FontAsset font)
    {
        StatusDetailRow existing = AssetDatabase.LoadAssetAtPath<StatusDetailRow>(RowPrefabPath);

        if (existing == null) { return false; }

        GameObject root = PrefabUtility.LoadPrefabContents(RowPrefabPath);
        RectTransform rootRect = (RectTransform)root.transform;

        ConfigureHorizontalLayout(root, spacing: 12f, expandHeight: false);

        Transform icon = FindOrMove(rootRect, rootRect, "Icon");
        Image iconImage = icon.GetComponent<Image>();
        if (iconImage == null) { iconImage = icon.gameObject.AddComponent<Image>(); }
        AddFixedSize(icon.gameObject, 36f, 36f);

        Transform textStack = FindOrMove(rootRect, rootRect, "TextStack");
        ConfigureVerticalLayout(textStack.gameObject, 0f, 0f, 3f, expandWidth: true, expandHeight: false);
        AddFlexibleWidth(textStack.gameObject);

        // Pre-restyle, these two were direct children of the row's own root - relocated into
        // TextStack the first time this runs, found there on every run after.
        Transform title = FindOrMove(textStack, rootRect, "TitleLabel");
        TMP_Text titleText = GetOrAddText(title);
        titleText.font = font;
        titleText.fontSize = PanelPalette.TermNameSize;
        titleText.characterSpacing = PanelPalette.TermNameSpacing;
        titleText.color = PanelPalette.Gold;
        titleText.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        titleText.alignment = TextAlignmentOptions.TopLeft;
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.raycastTarget = false;

        Transform body = FindOrMove(textStack, rootRect, "BodyLabel");
        TMP_Text bodyText = GetOrAddText(body);
        bodyText.font = font;
        bodyText.fontSize = PanelPalette.TermBodySize;
        bodyText.characterSpacing = 0f;
        bodyText.color = PanelPalette.BodyInk;
        bodyText.fontStyle = FontStyles.Normal;
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        bodyText.raycastTarget = false;

        StatusDetailRow row = root.GetComponent<StatusDetailRow>();
        SerializedObject so = new(row);
        so.FindProperty("icon").objectReferenceValue = iconImage;
        so.FindProperty("titleLabel").objectReferenceValue = titleText;
        so.FindProperty("bodyLabel").objectReferenceValue = bodyText;
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(root, RowPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        return true;
    }

    // ------------------------------------------------------------------------------------------
    // PartySheetColumn.prefab
    // ------------------------------------------------------------------------------------------

    private static bool StyleColumn(TMP_FontAsset font, Sprite chrome)
    {
        PartySheetColumn existing = AssetDatabase.LoadAssetAtPath<PartySheetColumn>(ColumnPrefabPath);

        if (existing == null) { return false; }

        GameObject root = PrefabUtility.LoadPrefabContents(ColumnPrefabPath);
        RectTransform rootRect = (RectTransform)root.transform;

        rootRect.sizeDelta = new Vector2(PanelPalette.ColumnWidth, PanelPalette.ColumnHeight);
        ConfigureImage(root, chrome, PanelPalette.PanelBorder);
        // The whole column is one child (ColumnFill) filling a FIXED-height root, unlike the tooltip's
        // outer panel - a ScrollRect needs a bounded viewport to clip against, so the column cannot be
        // left to grow with its content the way the tooltip panel does.
        ConfigureVerticalLayout(root, PanelPalette.BorderThickness, PanelPalette.BorderThickness, 0f,
            expandWidth: true, expandHeight: true);

        Transform columnFill = FindOrMove(rootRect, rootRect, "ColumnFill");
        ConfigureImage(columnFill.gameObject, chrome, PanelPalette.PanelFill);
        ConfigureVerticalLayout(columnFill.gameObject, PanelPalette.SectionPaddingH, PanelPalette.SectionPaddingV,
            PanelPalette.RowSpacing, expandWidth: true, expandHeight: false);

        // ---- Header block: portrait, name, health, energy - all pre-existing objects, relocated
        // rather than recreated so PartySheetColumn's own serialized references to them survive.
        Transform header = FindOrMove(columnFill, rootRect, "HeaderBlock");
        ConfigureVerticalLayout(header.gameObject, 0f, 0f, PanelPalette.RowSpacing, expandWidth: true,
            expandHeight: false);
        header.gameObject.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;

        Transform portrait = FindOrMove(header, rootRect, "PortraitImage");
        Image portraitImage = portrait.GetComponent<Image>();
        if (portraitImage == null) { portraitImage = portrait.gameObject.AddComponent<Image>(); }
        AddFixedSize(portrait.gameObject, 96f, 96f);

        Transform name = FindOrMove(header, rootRect, "NameLabel");
        TMP_Text nameText = GetOrAddText(name);
        nameText.font = font;
        nameText.fontSize = PanelPalette.HeaderTitleSize;
        nameText.characterSpacing = PanelPalette.HeaderTitleSpacing;
        nameText.color = PanelPalette.Gold;
        nameText.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.textWrappingMode = TextWrappingModes.NoWrap;
        nameText.raycastTarget = false;

        Transform healthBg = FindOrMove(header, rootRect, "HealthBarBg");
        Image healthBgImage = healthBg.GetComponent<Image>();
        if (healthBgImage == null) { healthBgImage = healthBg.gameObject.AddComponent<Image>(); }
        healthBgImage.color = new Color(0f, 0f, 0f, 0.6f);
        healthBgImage.raycastTarget = false;
        AddFixedHeight(healthBg.gameObject, 14f);

        Image healthFill = FindOrCreateFill(healthBg, "HealthFill", new Color(0.2f, 0.8f, 0.3f, 1f));
        Image shieldFill = FindOrCreateFill(healthBg, "ShieldFill", new Color(0.4f, 0.7f, 1f, 1f));

        Transform healthText = FindOrMove(header, rootRect, "HealthText");
        TMP_Text healthTextTmp = GetOrAddText(healthText);
        StylePlainLabel(healthTextTmp, font);

        Transform energyText = FindOrMove(header, rootRect, "EnergyText");
        TMP_Text energyTextTmp = GetOrAddText(energyText);
        StylePlainLabel(energyTextTmp, font);

        // ---- Rule, separating the header block from the status list.
        Transform rule = FindOrMove(columnFill, rootRect, "HeaderRule");
        ConfigureImage(rule.gameObject, null, PanelPalette.SectionRule);
        AddFixedHeight(rule.gameObject, PanelPalette.RuleHeight);

        // ---- Status section: label, a scrolling row list, and the "None" line the empty state used
        // to just... not have.
        Transform statusSection = FindOrMove(columnFill, rootRect, "StatusSection");
        ConfigureVerticalLayout(statusSection.gameObject, 0f, 0f, PanelPalette.RowSpacing, expandWidth: true,
            expandHeight: false);
        AddFlexibleHeight(statusSection.gameObject);

        Transform sectionLabel = FindOrMove(statusSection, rootRect, "SectionLabel");
        TMP_Text sectionLabelText = GetOrAddText(sectionLabel);
        sectionLabelText.font = font;
        sectionLabelText.fontSize = PanelPalette.SectionLabelSize;
        sectionLabelText.characterSpacing = PanelPalette.SectionLabelSpacing;
        sectionLabelText.color = PanelPalette.LabelGrey;
        sectionLabelText.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        sectionLabelText.alignment = TextAlignmentOptions.TopLeft;
        sectionLabelText.textWrappingMode = TextWrappingModes.NoWrap;
        sectionLabelText.raycastTarget = false;
        sectionLabelText.text = "Status Effects";

        Transform noneLabel = FindOrMove(statusSection, rootRect, "NoneLabel");
        TMP_Text noneLabelText = GetOrAddText(noneLabel);
        noneLabelText.font = font;
        noneLabelText.fontSize = PanelPalette.TermBodySize;
        noneLabelText.characterSpacing = 0f;
        noneLabelText.color = PanelPalette.BodyInk;
        noneLabelText.fontStyle = FontStyles.Normal;
        noneLabelText.alignment = TextAlignmentOptions.TopLeft;
        noneLabelText.textWrappingMode = TextWrappingModes.Normal;
        noneLabelText.raycastTarget = false;
        noneLabelText.text = "None";

        // ---- The scroll view. statusParent - StatusDetailRow's pooling target since before this
        // restyle - becomes the ScrollRect's Content, relocated under a Viewport it did not used to
        // have rather than recreated, so PartySheetColumn's existing statusParent reference survives.
        Transform scrollArea = FindOrMove(statusSection, rootRect, "ScrollArea");
        AddFlexibleHeight(scrollArea.gameObject);
        ScrollRect scrollRect = scrollArea.GetComponent<ScrollRect>();
        if (scrollRect == null) { scrollRect = scrollArea.gameObject.AddComponent<ScrollRect>(); }
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        Transform viewport = FindOrMove(scrollArea, rootRect, "Viewport");
        Stretch((RectTransform)viewport);
        Image viewportImage = viewport.GetComponent<Image>();
        if (viewportImage == null) { viewportImage = viewport.gameObject.AddComponent<Image>(); }
        viewportImage.color = new Color(0f, 0f, 0f, 0f);
        viewportImage.raycastTarget = true;
        if (viewport.GetComponent<RectMask2D>() == null) { viewport.gameObject.AddComponent<RectMask2D>(); }

        Transform statusParent = FindOrMove(viewport, rootRect, "StatusParent");
        RectTransform statusParentRect = (RectTransform)statusParent;
        statusParentRect.anchorMin = new Vector2(0f, 1f);
        statusParentRect.anchorMax = new Vector2(1f, 1f);
        statusParentRect.pivot = new Vector2(0.5f, 1f);
        ConfigureVerticalLayout(statusParent.gameObject, 0f, 0f, PanelPalette.RowSpacing, expandWidth: true,
            expandHeight: false);
        ConfigureFitter(statusParent.gameObject);

        scrollRect.viewport = (RectTransform)viewport;
        scrollRect.content = statusParentRect;

        PartySheetColumn column = root.GetComponent<PartySheetColumn>();
        SerializedObject so = new(column);
        so.FindProperty("portraitImage").objectReferenceValue = portraitImage;
        so.FindProperty("nameLabel").objectReferenceValue = nameText;
        so.FindProperty("healthFill").objectReferenceValue = healthFill;
        so.FindProperty("shieldFill").objectReferenceValue = shieldFill;
        so.FindProperty("healthText").objectReferenceValue = healthTextTmp;
        so.FindProperty("energyText").objectReferenceValue = energyTextTmp;
        so.FindProperty("statusParent").objectReferenceValue = statusParentRect;
        so.FindProperty("scrollRoot").objectReferenceValue = scrollRect;
        so.FindProperty("noneLabel").objectReferenceValue = noneLabel.gameObject;
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(root, ColumnPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        return true;
    }

    private static void StylePlainLabel(TMP_Text text, TMP_FontAsset font)
    {
        text.font = font;
        text.fontSize = PanelPalette.StatValueSize;
        text.characterSpacing = 0f;
        text.color = PanelPalette.Ink;
        text.fontStyle = FontStyles.Normal;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
    }

    private static Image FindOrCreateFill(Transform parent, string childName, Color color)
    {
        Transform existing = parent.Find(childName);
        GameObject go = existing != null ? existing.gameObject : new GameObject(childName, typeof(RectTransform));

        if (existing == null)
        {
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
        }

        Stretch((RectTransform)go.transform);

        Image image = go.GetComponent<Image>();
        if (image == null) { image = go.AddComponent<Image>(); }

        image.color = color;
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = (int)Image.OriginHorizontal.Left;
        image.raycastTarget = false;

        return image;
    }

    // ------------------------------------------------------------------------------------------
    // Shared helpers - deliberately duplicated rather than shared with the other *Wiring scripts,
    // matching how PartyPortraitWiring documents that same choice for its own CreateLabel/Centre pair.
    // ------------------------------------------------------------------------------------------

    /// Finds `childName` under `preferredParent`; if not there, relocates it from `legacyParent` (its
    /// pre-restyle location, often the same object this whole method is being called on); if it exists
    /// nowhere, creates it fresh. This is what makes every restyle here idempotent without destroying
    /// and recreating an object something else already holds a serialized reference to.
    private static Transform FindOrMove(Transform preferredParent, Transform legacyParent, string childName)
    {
        Transform existing = preferredParent.Find(childName);

        if (existing != null) { return existing; }

        Transform legacy = legacyParent != preferredParent ? legacyParent.Find(childName) : null;

        if (legacy != null)
        {
            legacy.SetParent(preferredParent, false);
            return legacy;
        }

        GameObject go = new(childName, typeof(RectTransform));
        go.transform.SetParent(preferredParent, false);
        go.layer = preferredParent.gameObject.layer;

        return go.transform;
    }

    private static TMP_Text GetOrAddText(Transform target)
    {
        return target.GetComponent<TMP_Text>() as TMP_Text ?? target.gameObject.AddComponent<TextMeshProUGUI>();
    }

    private static void ConfigureImage(GameObject go, Sprite sprite, Color color)
    {
        Image image = go.GetComponent<Image>();
        if (image == null) { image = go.AddComponent<Image>(); }

        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }

        image.color = color;
        image.raycastTarget = false;
    }

    private static void ConfigureVerticalLayout(GameObject go, float paddingH, float paddingV, float spacing,
        bool expandWidth, bool expandHeight)
    {
        VerticalLayoutGroup layout = go.GetComponent<VerticalLayoutGroup>();
        if (layout == null) { layout = go.AddComponent<VerticalLayoutGroup>(); }

        layout.padding = new RectOffset((int)paddingH, (int)paddingH, (int)paddingV, (int)paddingV);
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = expandWidth;
        layout.childForceExpandHeight = expandHeight;
        layout.childAlignment = TextAnchor.UpperLeft;
    }

    private static void ConfigureHorizontalLayout(GameObject go, float spacing, bool expandHeight)
    {
        HorizontalLayoutGroup layout = go.GetComponent<HorizontalLayoutGroup>();
        if (layout == null) { layout = go.AddComponent<HorizontalLayoutGroup>(); }

        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = expandHeight;
        layout.childAlignment = TextAnchor.UpperLeft;
    }

    private static void ConfigureFitter(GameObject go)
    {
        ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();
        if (fitter == null) { fitter = go.AddComponent<ContentSizeFitter>(); }

        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    private static void AddFixedSize(GameObject go, float width, float height)
    {
        LayoutElement element = go.GetComponent<LayoutElement>();
        if (element == null) { element = go.AddComponent<LayoutElement>(); }

        element.preferredWidth = width;
        element.preferredHeight = height;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;
    }

    private static void AddFixedHeight(GameObject go, float height)
    {
        LayoutElement element = go.GetComponent<LayoutElement>();
        if (element == null) { element = go.AddComponent<LayoutElement>(); }

        element.preferredHeight = height;
        element.flexibleHeight = 0f;
    }

    private static void AddFlexibleWidth(GameObject go)
    {
        LayoutElement element = go.GetComponent<LayoutElement>();
        if (element == null) { element = go.AddComponent<LayoutElement>(); }

        element.flexibleWidth = 1f;
    }

    private static void AddFlexibleHeight(GameObject go)
    {
        LayoutElement element = go.GetComponent<LayoutElement>();
        if (element == null) { element = go.AddComponent<LayoutElement>(); }

        element.flexibleHeight = 1f;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
