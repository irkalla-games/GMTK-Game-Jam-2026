using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the tutorial overlay into Game.unity: the spotlight's dim canvas, the popup's bordered box
/// with its Continue and Skip buttons, and the TutorialDirector that drives them - then wires every
/// serialized reference the three need, including the scene anchors the script points at.
///
/// A menu command rather than hand-edited scene YAML because the Editor holds Game.unity in memory while
/// it is open - see TooltipCanvasSortingFix's doc comment for the full reasoning. Same shape as
/// TooltipPanelWiring: idempotent by finding each object by name before creating it, and re-applying
/// every style value unconditionally on every run, so a changed PanelPalette constant is one re-run away
/// from shipping rather than a hand-edit away.
///
/// The popup reads its colours from PanelPalette, the same source the tooltip box and the party sheet
/// use, so the tutorial cannot drift into looking like a different game's UI.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class TutorialWiring
{
    private const string ScenePath = "Assets/Scenes/Game.unity";
    private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string FontPath = "Assets/Extra Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    private const string DirectorName = "TutorialDirector";
    private const string SpotlightCanvasName = "TutorialSpotlightCanvas";
    private const string PopupCanvasName = "TutorialPopupCanvas";

    /// Above the battle HUD, below the popup, and both below TooltipCanvas's 1000 - see
    /// TooltipCanvasSortingFix for the orders already in use on the Overlay layer.
    private const int SpotlightSortingOrder = 500;
    private const int PopupSortingOrder = 600;

    [MenuItem("Tools/Tutorial/2 - Wire Tutorial Overlay")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Tutorial wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenGameScene()) { return; }

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        if (font == null)
        {
            Debug.LogError($"Tutorial wiring: font not found at {FontPath} - check the path still matches "
                           + "the project's TMP install.");
            return;
        }

        TutorialSpotlight spotlight = BuildSpotlight();
        TutorialPopup popup = BuildPopup(font);
        TutorialDirector director = BuildDirector();

        WireDirector(director);

        EditorUtility.SetDirty(spotlight);
        EditorUtility.SetDirty(popup);
        EditorUtility.SetDirty(director);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log("Tutorial wiring: done - scene saved. Assign the five CardData fields on "
                  + $"{DirectorName} by hand: Fireball, Shield, Slash, Teleport, Sap Totem.");
    }

    // ---- Main Menu --------------------------------------------------------------------------------

    /// <summary>
    /// Points the Main Menu at the tutorial run and at the party it hands over on the way out.
    ///
    /// A second command, and a second scene: MainMenu.unity is where the decision to play the tutorial
    /// is made, and a command cannot have two scenes open at once. Run it after 1 and 2, or the assets
    /// it wants will not exist yet.
    /// </summary>
    [MenuItem("Tools/Tutorial/3 - Wire Main Menu")]
    public static void WireMainMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Tutorial wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (SceneManager.GetActiveScene().path != MenuScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return; }

            EditorSceneManager.OpenScene(MenuScenePath);
        }

        MainMenu menu = Object.FindAnyObjectByType<MainMenu>(FindObjectsInactive.Include);

        if (menu == null)
        {
            Debug.LogError($"Tutorial wiring: no MainMenu in {MenuScenePath} - nothing to wire.");
            return;
        }

        RunData tutorialRun = AssetDatabase.LoadAssetAtPath<RunData>("Assets/Data/RunData/TutorialRun.asset");

        if (tutorialRun == null)
        {
            Debug.LogError("Tutorial wiring: TutorialRun.asset not found - run "
                           + "Tools > Tutorial > 1 - Generate Tutorial Content first.");
            return;
        }

        SerializedObject so = new(menu);
        SetIfEmpty(so, "tutorialRun", tutorialRun);

        // The party the player is handed once the tutorial is cleared: the same two heroes they just
        // played, on the starter decks that hold the four cards the tutorial taught. Written only when
        // the list is empty, so a hand-tuned follow-on survives a re-run.
        SerializedProperty party = so.FindProperty("tutorialFollowOnParty");

        if (party != null && party.arraySize == 0)
        {
            SetPartyEntry(party, 0, "Assets/Prefabs/Player/PlayerKnight.prefab",
                "Assets/Data/DeckData/KnightStarter.asset");
            SetPartyEntry(party, 1, "Assets/Prefabs/Player/PlayerMage.prefab",
                "Assets/Data/DeckData/MageStarter.asset");
        }

        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(menu);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log("Tutorial wiring: Main Menu wired - scene saved.");
    }

    private static void SetPartyEntry(SerializedProperty party, int index, string prefabPath, string deckPath)
    {
        if (party.arraySize <= index) { party.arraySize = index + 1; }

        SerializedProperty entry = party.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("prefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        entry.FindPropertyRelative("deck").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<DeckData>(deckPath);
    }

    // ---- Spotlight --------------------------------------------------------------------------------

    private static TutorialSpotlight BuildSpotlight()
    {
        GameObject root = FindOrCreateRoot<TutorialSpotlight>(SpotlightCanvasName);

        Canvas canvas = ConfigureCanvas(root, SpotlightSortingOrder, withRaycaster: false);

        TutorialSpotlight spotlight = Ensure<TutorialSpotlight>(root);

        RectTransform quadParent = EnsureChild((RectTransform)canvas.transform, "Quads");
        Stretch(quadParent);

        SerializedObject so = new(spotlight);
        so.FindProperty("canvas").objectReferenceValue = canvas;
        so.FindProperty("quadParent").objectReferenceValue = quadParent;
        so.ApplyModifiedProperties();

        return spotlight;
    }

    // ---- Popup ------------------------------------------------------------------------------------

    private static TutorialPopup BuildPopup(TMP_FontAsset font)
    {
        GameObject root = FindOrCreateRoot<TutorialPopup>(PopupCanvasName);

        Canvas canvas = ConfigureCanvas(root, PopupSortingOrder, withRaycaster: true);

        TutorialPopup popup = Ensure<TutorialPopup>(root);

        Sprite chrome = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");

        // Same two-nested-sliced-Images trick TooltipPanelWiring uses: the outer rect IS the border, and
        // its layout padding is what insets the fill by exactly BorderThickness on every side.
        RectTransform panel = EnsureChild((RectTransform)canvas.transform, "Panel");
        Centre(panel);
        panel.sizeDelta = new Vector2(PanelPalette.PanelWidth, panel.sizeDelta.y);
        ConfigureImage(panel.gameObject, chrome, PanelPalette.PanelBorder, raycast: true);
        ConfigureVerticalLayout(panel.gameObject, PanelPalette.BorderThickness, PanelPalette.BorderThickness, 0f);
        ConfigureFitter(panel.gameObject);

        CanvasGroup group = Ensure<CanvasGroup>(panel.gameObject);

        RectTransform fill = EnsureChild(panel, "PanelFill");
        ConfigureImage(fill.gameObject, chrome, PanelPalette.PanelFill, raycast: true);
        ConfigureVerticalLayout(fill.gameObject, 0f, 0f, 0f);

        RectTransform header = EnsureChild(fill, "Header");
        ConfigureImage(header.gameObject, null, PanelPalette.HeaderFill, raycast: false);
        ConfigureVerticalLayout(header.gameObject, PanelPalette.HeaderPaddingH, PanelPalette.HeaderPaddingV, 0f);

        TMP_Text title = EnsureLabel(header, "Title");
        Style(title, font, PanelPalette.HeaderTitleSize, PanelPalette.HeaderTitleSpacing,
            PanelPalette.Gold, FontStyles.Bold | FontStyles.UpperCase, wrap: false);

        RectTransform rule = EnsureChild(fill, "HeaderRule");
        ConfigureImage(rule.gameObject, null, PanelPalette.PanelBorder, raycast: false);
        ConfigureRuleHeight(rule.gameObject);

        RectTransform bodyRoot = EnsureChild(fill, "Body");
        ConfigureVerticalLayout(bodyRoot.gameObject, PanelPalette.SectionPaddingH,
            PanelPalette.SectionPaddingV, PanelPalette.RowSpacing);

        TMP_Text body = EnsureLabel(bodyRoot, "BodyText");
        Style(body, font, PanelPalette.TermBodySize, 0f, PanelPalette.BodyInk, FontStyles.Normal, wrap: true);

        RectTransform footer = EnsureChild(bodyRoot, "Footer");
        ConfigureHorizontalLayout(footer.gameObject, PanelPalette.RowSpacing);

        Button cont = EnsureButton(footer, "ContinueButton", "Continue", font,
            PanelPalette.HeaderFill, PanelPalette.Gold);

        // A scene wired before Skip Tutorial moved out of the footer still has the old child sitting
        // here, wired to nothing now that TutorialPopup's skipButton field points at the standalone one
        // below - dead, but still visible next to Continue. FindOrCreateRoot's own by-name recovery
        // covers a *missing* piece; this is the opposite case, a piece that must not be there any more.
        Transform staleFooterSkip = footer.Find("SkipButton");
        if (staleFooterSkip != null) { Object.DestroyImmediate(staleFooterSkip.gameObject); }

        // Standalone, not part of Panel: present for the whole tutorial rather than toggled per step,
        // so it needs its own visibility separate from Panel's canvasGroup - see TutorialPopup.
        // EndTurnButton anchor: exit early rather than parking Skip Tutorial at a stale default; the
        // Wiring is otherwise idempotent and re-derives this every run.
        EndTurnButton endTurn = Object.FindAnyObjectByType<EndTurnButton>(FindObjectsInactive.Include);
        RectTransform endTurnRect = endTurn != null ? (RectTransform)endTurn.transform : null;
        (GameObject skipRoot, Button skip) = BuildSkipButton((RectTransform)canvas.transform, font, endTurnRect);

        SerializedObject so = new(popup);
        so.FindProperty("canvas").objectReferenceValue = canvas;
        so.FindProperty("panel").objectReferenceValue = panel;
        so.FindProperty("canvasGroup").objectReferenceValue = group;
        so.FindProperty("titleText").objectReferenceValue = title;
        so.FindProperty("bodyText").objectReferenceValue = body;
        so.FindProperty("continueButton").objectReferenceValue = cont;
        so.FindProperty("skipRoot").objectReferenceValue = skipRoot;
        so.FindProperty("skipButton").objectReferenceValue = skip;
        so.ApplyModifiedProperties();

        return popup;
    }

    /// <summary>
    /// Skip Tutorial, standalone rather than inside Panel's footer - present for the whole tutorial, not
    /// toggled per step, so it needs its own object to show/hide independently of Panel's own cycle.
    ///
    /// Anchored a fixed offset below `belowRect` (EndTurnButton) rather than following it every frame
    /// through TooltipAnchor/AnchoredPlacement the way Panel follows a step's changing target - both
    /// EndTurnButton and this sit still for the whole battle, so there is nothing to recompute after the
    /// numbers below are derived once. Read off `belowRect`'s own anchor/position/width rather than
    /// duplicating EndTurnButton's authored numbers, so a re-run of this command re-derives the position
    /// if EndTurnButton ever moves, the same "repair, not skip" contract every content generator in this
    /// project follows.
    /// </summary>
    private static (GameObject root, Button button) BuildSkipButton(
        RectTransform canvasParent, TMP_FontAsset font, RectTransform belowRect)
    {
        const float Gap = 16f;
        const float Height = 44f;

        RectTransform rect = EnsureChild(canvasParent, "SkipButton");
        rect.anchorMin = belowRect != null ? belowRect.anchorMin : new Vector2(1f, 1f);
        rect.anchorMax = belowRect != null ? belowRect.anchorMax : new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        float width = belowRect != null ? belowRect.sizeDelta.x : PanelPalette.PanelWidth * 0.5f;
        float x = belowRect != null ? belowRect.anchoredPosition.x : 0f;
        float y = belowRect != null
            ? belowRect.anchoredPosition.y - belowRect.sizeDelta.y * 0.5f - Gap - Height * 0.5f
            : 0f;

        rect.sizeDelta = new Vector2(width, Height);
        rect.anchoredPosition = new Vector2(x, y);

        ConfigureImage(rect.gameObject, null, PanelPalette.HeaderFill, raycast: true);

        Button button = Ensure<Button>(rect.gameObject);
        button.targetGraphic = rect.GetComponent<Image>();

        TMP_Text text = EnsureLabel(rect, "Label");
        Style(text, font, PanelPalette.TermNameSize, PanelPalette.TermNameSpacing, PanelPalette.LabelGrey,
            FontStyles.Bold | FontStyles.UpperCase, wrap: false);
        text.alignment = TextAlignmentOptions.Center;
        text.text = "Skip Tutorial";
        Stretch((RectTransform)text.transform);

        return (rect.gameObject, button);
    }

    // ---- Director ---------------------------------------------------------------------------------

    private static TutorialDirector BuildDirector()
    {
        TutorialDirector existing = Object.FindAnyObjectByType<TutorialDirector>(FindObjectsInactive.Include);

        if (existing != null) { return existing; }

        return new GameObject(DirectorName).AddComponent<TutorialDirector>();
    }

    /// <summary>
    /// Points the director at the scene objects the script spotlights, and at the five cards it names.
    ///
    /// The cards are loaded by exact asset path rather than searched for by name - a name search would
    /// happily pick "Fireball Storm" or "Slash+" over the card actually meant, and the failure would only
    /// show up mid-script as a beat that silently skips itself.
    ///
    /// Every field is only written when it is currently empty, so a hand-corrected reference survives a
    /// re-run - unlike the style values above, which are re-applied unconditionally because they have a
    /// single right answer in PanelPalette.
    /// </summary>
    private static void WireDirector(TutorialDirector director)
    {
        SerializedObject so = new(director);

        EndTurnButton endTurn = Object.FindAnyObjectByType<EndTurnButton>(FindObjectsInactive.Include);
        SetIfEmpty(so, "endTurnAnchor", endTurn != null ? endTurn.transform as RectTransform : null);

        SetIfEmpty(so, "discardPileAnchor", FindDiscardPileRect());

        SetIfEmpty(so, "portraitPanel",
            Object.FindAnyObjectByType<PartyPortraitPanel>(FindObjectsInactive.Include));

        SetIfEmpty(so, "enemyPanel", FindEnemyPanel());

        SetIfEmpty(so, "fireball", Card("Mage/RangedAttack/Fireball"));
        SetIfEmpty(so, "shield", Card("Knight/Buff (Defensive)/Shield"));
        SetIfEmpty(so, "slash", Card("Knight/Melee Attack/Slash"));
        SetIfEmpty(so, "teleport", Card("Mage/Movement/Teleport"));
        SetIfEmpty(so, "sapTotem", Card("Mage/Summon/Sap Totem"));

        so.ApplyModifiedProperties();
    }

    private static CardData Card(string relativePath)
    {
        string path = $"Assets/Data/CardData/{relativePath}.asset";
        CardData card = AssetDatabase.LoadAssetAtPath<CardData>(path);

        if (card == null)
        {
            Debug.LogWarning($"Tutorial wiring: no CardData at {path} - assign it on {DirectorName} by "
                             + "hand, or the beats using it will skip themselves.");
        }

        return card;
    }

    /// The enemy-side panel is the one whose Audience is NotPlayerControlled - asked rather than matched
    /// by object name, since the two panels are otherwise identical components.
    private static SelectedCharacterPanel FindEnemyPanel()
    {
        foreach (SelectedCharacterPanel panel in
                 Object.FindObjectsByType<SelectedCharacterPanel>(FindObjectsInactive.Include))
        {
            if (panel.Audience == PanelAudience.NotPlayerControlled) { return panel; }
        }

        return null;
    }

    /// <summary>
    /// The discard pile's own button, read off the single CardPileHud rather than matched by object
    /// name - the hud owns both piles, so "which rect is the discard one" is a question only it can
    /// answer, and its field is the answer.
    ///
    /// A miss only costs the discard beat its spotlight (the box falls back to centre screen), so this
    /// warns rather than failing the whole command.
    /// </summary>
    private static RectTransform FindDiscardPileRect()
    {
        CardPileHud hud = Object.FindAnyObjectByType<CardPileHud>(FindObjectsInactive.Include);

        if (hud != null)
        {
            SerializedObject hudSo = new(hud);

            if (hudSo.FindProperty("discardButton").objectReferenceValue is Button discard)
            {
                return (RectTransform)discard.transform;
            }
        }

        Debug.LogWarning("Tutorial wiring: no CardPileHud with a discardButton assigned - set "
                         + "discardPileAnchor by hand, or that beat will show its box centre screen.");
        return null;
    }

    // ---- Shared helpers ---------------------------------------------------------------------------
    // Deliberately duplicated rather than shared with TooltipPanelWiring / CharacterSelectWiring - the
    // same call those two already make, for the same reason: a shared Editor utility that three wiring
    // commands depend on becomes the thing nobody dares change.

    private static bool OpenGameScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);
        return true;
    }

    private static Canvas ConfigureCanvas(GameObject go, int sortingOrder, bool withRaycaster)
    {
        Canvas canvas = Ensure<Canvas>(go);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingLayerName = SortingLayers.Overlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = Ensure<CanvasScaler>(go);

        // Scale With Screen Size / 1920x1080 / Expand, matching every other screen-space canvas in the
        // project - CameraFrame keeps a fixed 19.2 x 10.8 world frame visible and the canvases have to
        // scale by the identical factor. See CLAUDE.md.
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        GraphicRaycaster raycaster = go.GetComponent<GraphicRaycaster>();

        if (withRaycaster && raycaster == null) { go.AddComponent<GraphicRaycaster>(); }
        else if (!withRaycaster && raycaster != null) { Object.DestroyImmediate(raycaster); }

        return canvas;
    }

    /// <summary>
    /// The root object for one of the overlay pieces: the one already carrying the component, else one
    /// already carrying the name, else a fresh one.
    ///
    /// The by-name step is what makes a *failed* run recoverable. This command builds the object first
    /// and adds its component several lines later, so anything that throws in between leaves a correctly
    /// named root with no component on it - and a by-component search alone would then sail past it and
    /// create a second object with the same name on every re-run.
    /// </summary>
    private static GameObject FindOrCreateRoot<T>(string rootName) where T : Component
    {
        T existing = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);

        if (existing != null) { return existing.gameObject; }

        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == rootName) { return root; }
        }

        return new GameObject(rootName);
    }

    /// <summary>
    /// GetComponent-or-AddComponent, written longhand rather than with `??`.
    ///
    /// **`??` and `?.` are wrong on any UnityEngine.Object.** They test *reference* null, which bypasses
    /// Unity's overloaded `==` - and that overload is the only thing that reports a missing or destroyed
    /// object as null. `GetComponent<Canvas>()` on an object with no Canvas hands back a *fake-null*:
    /// null by Unity's `==`, but a real reference as far as `??` is concerned. So
    /// `GetComponent&lt;Canvas&gt;() ?? AddComponent&lt;Canvas&gt;()` keeps the fake-null, never adds
    /// anything, and throws MissingComponentException on the first property set.
    ///
    /// TooltipPanelWiring and CharacterSelectWiring both write this out longhand for exactly this
    /// reason; this is that same rule with a name.
    /// </summary>
    private static T Ensure<T>(GameObject go) where T : Component
    {
        T existing = go.GetComponent<T>();

        if (existing == null) { existing = go.AddComponent<T>(); }

        return existing;
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

    private static TMP_Text EnsureLabel(RectTransform parent, string childName)
    {
        RectTransform rect = EnsureChild(parent, childName);

        // Not Ensure<T>: the field is the abstract TMP_Text but the component to add is the concrete
        // TextMeshProUGUI, so the get and the add are different types.
        TMP_Text text = rect.GetComponent<TMP_Text>();

        if (text == null) { text = rect.gameObject.AddComponent<TextMeshProUGUI>(); }

        return text;
    }

    private static void Style(TMP_Text text, TMP_FontAsset font, float size, float spacing, Color color,
        FontStyles style, bool wrap)
    {
        text.font = font;
        text.fontSize = size;
        text.characterSpacing = spacing;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        text.raycastTarget = false;
    }

    private static Button EnsureButton(RectTransform parent, string childName, string label,
        TMP_FontAsset font, Color fill, Color ink)
    {
        RectTransform rect = EnsureChild(parent, childName);

        ConfigureImage(rect.gameObject, null, fill, raycast: true);

        Button button = Ensure<Button>(rect.gameObject);
        button.targetGraphic = rect.GetComponent<Image>();

        LayoutElement element = Ensure<LayoutElement>(rect.gameObject);
        element.preferredHeight = 44f;
        element.flexibleWidth = 1f;

        TMP_Text text = EnsureLabel(rect, "Label");
        Style(text, font, PanelPalette.TermNameSize, PanelPalette.TermNameSpacing, ink,
            FontStyles.Bold | FontStyles.UpperCase, wrap: false);
        text.alignment = TextAlignmentOptions.Center;
        text.text = label;
        Stretch((RectTransform)text.transform);

        return button;
    }

    private static void ConfigureImage(GameObject go, Sprite sprite, Color color, bool raycast)
    {
        Image image = Ensure<Image>(go);

        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }

        image.color = color;
        image.raycastTarget = raycast;
    }

    private static void ConfigureVerticalLayout(GameObject go, float paddingH, float paddingV, float spacing)
    {
        VerticalLayoutGroup layout = Ensure<VerticalLayoutGroup>(go);

        layout.padding = new RectOffset((int)paddingH, (int)paddingH, (int)paddingV, (int)paddingV);
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperLeft;
    }

    private static void ConfigureHorizontalLayout(GameObject go, float spacing)
    {
        HorizontalLayoutGroup layout = Ensure<HorizontalLayoutGroup>(go);

        layout.padding = new RectOffset(0, 0, (int)PanelPalette.RowSpacing, 0);
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleCenter;
    }

    private static void ConfigureFitter(GameObject go)
    {
        ContentSizeFitter fitter = Ensure<ContentSizeFitter>(go);

        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    private static void ConfigureRuleHeight(GameObject go)
    {
        LayoutElement element = Ensure<LayoutElement>(go);

        element.preferredHeight = PanelPalette.RuleHeight;
        element.flexibleHeight = 0f;
    }

    private static void Centre(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetIfEmpty(SerializedObject so, string propertyName, Object value)
    {
        SerializedProperty property = so.FindProperty(propertyName);

        if (property == null || value == null) { return; }
        if (property.objectReferenceValue != null) { return; }

        property.objectReferenceValue = value;
    }
}
