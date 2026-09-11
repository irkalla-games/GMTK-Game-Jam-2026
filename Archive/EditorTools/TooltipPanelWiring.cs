using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the tooltip panel's static chrome - the bordered outer rect, the fill, the header bar and
/// rule, and the Body it hands off to TooltipManager for its own runtime-pooled sections - and wires
/// TooltipManager's skeleton fields to it.
///
/// A menu command rather than hand-edited scene YAML because the Editor holds Game.unity in memory
/// while it is open - see TooltipCanvasSortingFix's doc comment for the full reasoning. Same shape as
/// that script and PartyPortraitWiring: idempotent by finding each object by name before creating it,
/// and re-applies every style value unconditionally on every run so a changed PanelPalette constant is
/// one re-run away from shipping, not a hand-edit away.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class TooltipPanelWiring
{
    private const string ScenePath = "Assets/Scenes/Game.unity";
    private const string FontPath = "Assets/Extra Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    private const string LegacyTextName = "Text";
    private const string PanelFillName = "PanelFill";
    private const string HeaderName = "Header";
    private const string TitleName = "Title";
    private const string HeaderRuleName = "HeaderRule";
    private const string BodyName = "Body";

    [MenuItem("Tools/UI/Wire Tooltip Panel")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Tooltip panel wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenGameScene()) { return; }

        TooltipManager manager = Object.FindAnyObjectByType<TooltipManager>(FindObjectsInactive.Include);

        if (manager == null)
        {
            Debug.LogError($"Tooltip panel wiring: no TooltipManager in {ScenePath} - nothing to wire.");
            return;
        }

        SerializedObject managerSo = new(manager);
        SerializedProperty panelProp = managerSo.FindProperty("panel");

        if (panelProp.objectReferenceValue is not RectTransform panel)
        {
            Debug.LogError("Tooltip panel wiring: TooltipManager has no panel assigned - nothing to build on.");
            return;
        }

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        if (font == null)
        {
            Debug.LogError($"Tooltip panel wiring: font not found at {FontPath} - check the path still matches " +
                "the project's TMP install.");
            return;
        }

        // tooltip_box carries its own copper border and dark fill, so the panel is tinted WHITE to
        // show the art as authored rather than recoloured through PanelPalette.
        Sprite chrome = SharpSkin.Load(SharpSkin.PanelTight);
        Sprite headerChrome = SharpSkin.Load(SharpSkin.Header);

        // The outer rect carries the whole panel: tooltip_box is a dark fill inside a one-pixel
        // copper edge, sliced, so it is the border AND the surface. PanelFill survives only as the
        // layout container it always also was, inset by BorderThickness on every side.
        Centre(panel);
        panel.sizeDelta = new Vector2(PanelPalette.PanelWidth, panel.sizeDelta.y);
        ConfigureImage(panel.gameObject, chrome, Color.white);
        ConfigureVerticalLayout(panel.gameObject, PanelPalette.BorderThickness, PanelPalette.BorderThickness, 0f,
            expandHeight: false);
        ConfigureFitter(panel.gameObject);

        RectTransform panelFill = EnsureChild(panel, PanelFillName);
        // Now purely a layout container. The nested-Image trick that used to fake a border out of a
        // flat sprite is redundant against art that already has one, and drawing a second copy of
        // tooltip_box inside the first would put a copper line through the middle of the panel.
        ConfigureImage(panelFill.gameObject, null, Color.clear);
        ConfigureVerticalLayout(panelFill.gameObject, 0f, 0f, 0f, expandHeight: false);

        RectTransform header = EnsureChild(panelFill, HeaderName);
        ConfigureImage(header.gameObject, headerChrome, Color.white);
        ConfigureVerticalLayout(header.gameObject, PanelPalette.HeaderPaddingH, PanelPalette.HeaderPaddingV,
            0f, expandHeight: false);

        TMP_Text title = EnsureTitle(header, panel);
        title.font = font;
        title.fontSize = PanelPalette.HeaderTitleSize;
        title.characterSpacing = PanelPalette.HeaderTitleSpacing;
        title.color = PanelPalette.Gold;
        title.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        title.alignment = TextAlignmentOptions.TopLeft;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.raycastTarget = false;

        RectTransform headerRule = EnsureChild(panelFill, HeaderRuleName);
        ConfigureImage(headerRule.gameObject, null, PanelPalette.PanelBorder);
        ConfigureRuleHeight(headerRule.gameObject);

        RectTransform body = EnsureChild(panelFill, BodyName);
        ConfigureVerticalLayout(body.gameObject, 0f, 0f, 0f, expandHeight: false);

        managerSo.FindProperty("panelFill").objectReferenceValue = panelFill;
        managerSo.FindProperty("headerRoot").objectReferenceValue = header;
        managerSo.FindProperty("headerTitle").objectReferenceValue = title;
        managerSo.FindProperty("headerRule").objectReferenceValue = headerRule;
        managerSo.FindProperty("body").objectReferenceValue = body;
        managerSo.FindProperty("font").objectReferenceValue = font;
        managerSo.ApplyModifiedProperties();

        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log("Tooltip panel wiring: done - scene saved.");
    }

    private static bool OpenGameScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);
        return true;
    }

    /// The pre-restyle box had one child, "Text", holding the TMP_Text every hover used to render into.
    /// Relocated into Header rather than destroyed and rebuilt - a reference nothing else in the scene
    /// points at, but deleting a live TMP object for no reason a re-run could not also achieve is not a
    /// trade this command needs to make. A second run finds it already parented under Header as "Title"
    /// and does nothing here.
    private static TMP_Text EnsureTitle(RectTransform header, RectTransform panel)
    {
        Transform existing = header.Find(TitleName);

        if (existing != null) { return existing.GetComponent<TMP_Text>() ?? existing.gameObject.AddComponent<TextMeshProUGUI>(); }

        Transform legacy = panel.Find(LegacyTextName);

        if (legacy != null)
        {
            legacy.SetParent(header, false);
            legacy.name = TitleName;
            legacy.gameObject.SetActive(true);

            return legacy.GetComponent<TMP_Text>() ?? legacy.gameObject.AddComponent<TextMeshProUGUI>();
        }

        GameObject go = new(TitleName, typeof(RectTransform));
        go.transform.SetParent(header, false);
        go.layer = header.gameObject.layer;

        return go.AddComponent<TextMeshProUGUI>();
    }

    private static RectTransform EnsureChild(RectTransform parent, string childName)
    {
        Transform existing = parent.Find(childName);

        if (existing != null) { return (RectTransform)existing; }

        GameObject go = new(childName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        return (RectTransform)go.transform;
    }

    /// `sprite` null keeps whatever Image the object already has (or none) - Header and HeaderRule are
    /// flat colour, not sliced chrome, so they never need one.
    private static void ConfigureImage(GameObject go, Sprite sprite, Color color)
    {
        Image image = go.GetComponent<Image>();

        if (image == null) { image = go.AddComponent<Image>(); }

        // A null sprite CLEARS whatever was there and returns the Image to a flat colour, rather
        // than leaving the previous sprite in place. Idempotency cuts both ways: this command has
        // to be able to un-set what an earlier version of itself set - PanelFill is exactly that
        // case, having carried the chrome sprite until the Sharp GUI art replaced it.
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }
        else
        {
            image.sprite = null;
            image.type = Image.Type.Simple;
        }

        image.color = color;
        image.raycastTarget = false;
    }

    private static void ConfigureVerticalLayout(GameObject go, float paddingH, float paddingV, float spacing,
        bool expandHeight)
    {
        VerticalLayoutGroup layout = go.GetComponent<VerticalLayoutGroup>();

        if (layout == null) { layout = go.AddComponent<VerticalLayoutGroup>(); }

        layout.padding = new RectOffset((int)paddingH, (int)paddingH, (int)paddingV, (int)paddingV);
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = expandHeight;
        layout.childAlignment = TextAnchor.UpperLeft;
    }

    /// Only the panel needs this - PanelFill and its children report their own preferred height back up
    /// through their own layout groups, which is exactly what lets a single fitter on the outermost rect
    /// be enough. See TooltipManager.Render's own comment for why that rebuild still runs twice.
    private static void ConfigureFitter(GameObject go)
    {
        ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();

        if (fitter == null) { fitter = go.AddComponent<ContentSizeFitter>(); }

        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    private static void ConfigureRuleHeight(GameObject go)
    {
        LayoutElement element = go.GetComponent<LayoutElement>();

        if (element == null) { element = go.AddComponent<LayoutElement>(); }

        element.preferredHeight = PanelPalette.RuleHeight;
        element.flexibleHeight = 0f;
    }

    private static void Centre(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }
}
