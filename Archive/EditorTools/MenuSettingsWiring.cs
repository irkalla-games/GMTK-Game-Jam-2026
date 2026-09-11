using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the main menu's Settings screen and hands its widgets to MenuSettingsPanel, then removes the
/// three loose toggles it replaces.
///
/// The toggles are destroyed here rather than in MainMenuCanvasWiring on purpose: this is the first
/// moment those three settings are reachable somewhere else, so it is the first moment removing them
/// costs the player nothing. Run this before expecting Tutorial, Show Intent Damage or Debug Run to be
/// changeable at all.
///
/// Same shape as every wiring command here - refuse in Play Mode, open the scene through
/// EditorSceneManager rather than touching YAML, find-by-name-else-create, re-apply every style value
/// on every run, write private fields through SerializedObject.
/// </summary>
public static class MenuSettingsWiring
{
    private const string ScenePath = "Assets/Scenes/MainMenu.unity";

    private const string PanelName = "SettingsPanel";
    private const string BackdropName = "Backdrop";
    private const string WindowName = "Window";
    private const string HeaderName = "Header";
    private const string BodyName = "Body";
    private const string FooterName = "Footer";
    private const string BackButtonName = "BackButton";

    /// Menu track and the blip the Sound Effects slider previews with. Assigned here rather than
    /// authored by hand so a fresh checkout has audible sliders without a wiring step nobody wrote
    /// down.
    private const string MenuMusicPath = "Assets/Sounds/Action 1 (Loop).wav";
    private const string SfxPreviewPath = "Assets/Sounds/Magic Spell_Coins_2.wav";

    /// The toggles this screen replaces. Removed once their settings live here instead.
    private static readonly string[] RetiredToggles =
    {
        "TutorialToggle", "IntentDamageToggle", "DebugRunToggle",
    };

    [MenuItem("Tools/Main Menu/Wire Settings Panel")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Menu settings wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenMenuScene()) { return; }

        MainMenu menu = Object.FindAnyObjectByType<MainMenu>(FindObjectsInactive.Include);

        if (menu == null)
        {
            Debug.LogError($"Menu settings wiring: no MainMenu component in {ScenePath}.");
            return;
        }

        Canvas canvas = FindCanvas();

        if (canvas == null)
        {
            Debug.LogError($"Menu settings wiring: no Canvas in {ScenePath} - nowhere to put the panel.");
            return;
        }

        RectTransform canvasRect = (RectTransform)canvas.transform;

        MenuSettingsPanel panel = BuildPanel(canvasRect);

        WireSettingsButton(canvasRect, menu);
        WireMainMenu(menu, panel);
        RemoveRetiredToggles(canvasRect, menu);

        // See SharpSkin.RebuildLayout - the layout has to settle before the scene is written.
        SharpSkin.RebuildLayout(canvasRect);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log("Menu settings wiring: done - scene saved.");
    }

    /// <summary>
    /// Builds Backdrop + Window(Header/Body/Footer) and every row inside it.
    ///
    /// The Backdrop is a full-screen dimmer that DOES take raycasts, so a click beside the window
    /// cannot reach the menu buttons underneath - the panel is modal even though nothing here is
    /// disabled while it is up.
    /// </summary>
    private static MenuSettingsPanel BuildPanel(RectTransform canvasRect)
    {
        RectTransform panelRoot = SharpSkin.EnsureChild(canvasRect, PanelName);
        Stretch(panelRoot);

        MenuSettingsPanel panel = SharpSkin.Ensure<MenuSettingsPanel>(panelRoot.gameObject);

        RectTransform backdrop = SharpSkin.EnsureChild(panelRoot, BackdropName);
        Stretch(backdrop);
        Image backdropImage = SharpSkin.Ensure<Image>(backdrop.gameObject);
        backdropImage.sprite = null;
        backdropImage.color = new Color(0f, 0f, 0f, 0.72f);
        backdropImage.raycastTarget = true;

        RectTransform window = SharpSkin.EnsureChild(panelRoot, WindowName);
        window.anchorMin = new Vector2(0.5f, 0.5f);
        window.anchorMax = new Vector2(0.5f, 0.5f);
        window.pivot = new Vector2(0.5f, 0.5f);
        window.anchoredPosition = Vector2.zero;
        window.sizeDelta = new Vector2(PanelPalette.SettingsPanelWidth, 0f);
        window.localScale = Vector3.one;

        SharpSkin.ApplySliced(SharpSkin.Ensure<Image>(window.gameObject), SharpSkin.Panel);

        VerticalLayoutGroup windowLayout = SharpSkin.Ensure<VerticalLayoutGroup>(window.gameObject);
        windowLayout.padding = new RectOffset(24, 24, 24, 24);
        windowLayout.spacing = 12f;
        windowLayout.childAlignment = TextAnchor.UpperCenter;
        windowLayout.childForceExpandWidth = true;
        windowLayout.childForceExpandHeight = false;
        windowLayout.childControlWidth = true;
        windowLayout.childControlHeight = true;

        // The window grows to whatever the rows need rather than carrying a hand-set height, so adding
        // a setting later does not also mean remembering to make the panel taller.
        ContentSizeFitter fitter = SharpSkin.Ensure<ContentSizeFitter>(window.gameObject);
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildHeader(window);

        RectTransform body = SharpSkin.EnsureChild(window, BodyName);
        VerticalLayoutGroup bodyLayout = SharpSkin.Ensure<VerticalLayoutGroup>(body.gameObject);
        bodyLayout.spacing = PanelPalette.SettingsRowSpacing;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = false;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;

        ContentSizeFitter bodyFitter = SharpSkin.Ensure<ContentSizeFitter>(body.gameObject);
        bodyFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        bodyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        SettingsRowBuilder.GroupLabel(body, "DisplayGroup", "Display");
        Toggle fullscreen = SettingsRowBuilder.ToggleRow(body, "FullscreenRow", "Fullscreen");
        TMP_Dropdown resolution = SettingsRowBuilder.DropdownRow(body, "ResolutionRow", "Resolution");

        SettingsRowBuilder.GroupLabel(body, "AudioGroup", "Audio");
        Slider master = SettingsRowBuilder.SliderRow(body, "MasterRow", "Master");
        Slider music = SettingsRowBuilder.SliderRow(body, "MusicRow", "Music");
        Slider sfx = SettingsRowBuilder.SliderRow(body, "SfxRow", "Sound Effects");

        SettingsRowBuilder.GroupLabel(body, "GameGroup", "Game");
        Toggle tutorial = SettingsRowBuilder.ToggleRow(body, "TutorialRow", "Tutorial");
        Toggle intent = SettingsRowBuilder.ToggleRow(body, "IntentRow", "Show Intent Damage");
        Toggle debugRun = SettingsRowBuilder.ToggleRow(body, "DebugRunRow", "Debug Run");

        Button back = BuildFooter(window);

        SerializedObject so = new(panel);
        so.FindProperty("root").objectReferenceValue = panelRoot.gameObject;
        so.FindProperty("backButton").objectReferenceValue = back;
        so.FindProperty("fullscreenToggle").objectReferenceValue = fullscreen;
        so.FindProperty("resolutionDropdown").objectReferenceValue = resolution;
        so.FindProperty("masterSlider").objectReferenceValue = master;
        so.FindProperty("musicSlider").objectReferenceValue = music;
        so.FindProperty("sfxSlider").objectReferenceValue = sfx;
        so.FindProperty("tutorialToggle").objectReferenceValue = tutorial;
        so.FindProperty("intentDamageToggle").objectReferenceValue = intent;
        so.FindProperty("debugRunToggle").objectReferenceValue = debugRun;
        so.FindProperty("sfxPreview").objectReferenceValue = LoadClip(SfxPreviewPath);
        so.ApplyModifiedProperties();

        // Left active in the scene so it can be inspected; MenuSettingsPanel.Awake hides it on load,
        // which is the same contract CardPilePanel and PartySheetPanel keep.
        panelRoot.gameObject.SetActive(true);

        return panel;
    }

    private static void BuildHeader(RectTransform window)
    {
        RectTransform header = SharpSkin.EnsureChild(window, HeaderName);

        SharpSkin.ApplySliced(SharpSkin.Ensure<Image>(header.gameObject), SharpSkin.Header);

        LayoutElement layout = SharpSkin.Ensure<LayoutElement>(header.gameObject);
        layout.minHeight = 64f;
        layout.preferredHeight = 64f;

        RectTransform titleRect = SharpSkin.EnsureChild(header, "Title");
        Stretch(titleRect);

        TextMeshProUGUI title = SharpSkin.Ensure<TextMeshProUGUI>(titleRect.gameObject);

        TMP_FontAsset font = SharpSkin.LoadFont();

        if (font != null) { title.font = font; }

        title.text = "SETTINGS";
        title.fontSize = 28f;
        title.characterSpacing = PanelPalette.HeaderTitleSpacing;
        title.color = PanelPalette.Gold;
        title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false;

        EditorUtility.SetDirty(title);
    }

    private static Button BuildFooter(RectTransform window)
    {
        RectTransform footer = SharpSkin.EnsureChild(window, FooterName);

        LayoutElement footerLayout = SharpSkin.Ensure<LayoutElement>(footer.gameObject);
        footerLayout.minHeight = 76f;
        footerLayout.preferredHeight = 76f;

        HorizontalLayoutGroup layout = SharpSkin.Ensure<HorizontalLayoutGroup>(footer.gameObject);
        layout.padding = new RectOffset(0, 0, 12, 0);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        RectTransform buttonRect = SharpSkin.EnsureChild(footer, BackButtonName);

        LayoutElement buttonLayout = SharpSkin.Ensure<LayoutElement>(buttonRect.gameObject);
        buttonLayout.preferredWidth = 260f;
        buttonLayout.preferredHeight = 56f;

        Button button = SharpSkin.Ensure<Button>(buttonRect.gameObject);

        RectTransform labelRect = SharpSkin.EnsureChild(buttonRect, "Label");
        Stretch(labelRect);

        TextMeshProUGUI label = SharpSkin.Ensure<TextMeshProUGUI>(labelRect.gameObject);

        TMP_FontAsset font = SharpSkin.LoadFont();

        if (font != null) { label.font = font; }

        label.text = "Back";
        label.fontSize = 26f;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        SharpSkin.ApplyButton(button);

        return button;
    }

    /// <summary>
    /// Points the menu's Settings button at MainMenu.settingsButton.
    ///
    /// The button was authored with an empty onClick and stayed that way through every previous pass -
    /// this is the call that finally gives it something to do. Cleared first so a re-run does not stack
    /// duplicate calls that would open the panel twice.
    /// </summary>
    private static void WireSettingsButton(RectTransform canvasRect, MainMenu menu)
    {
        Transform found = canvasRect.Find("SettingsButton");

        if (found == null)
        {
            Debug.LogWarning("Menu settings wiring: no SettingsButton under the Canvas - nothing to open the panel.");
            return;
        }

        Button button = found.GetComponent<Button>();

        if (button == null)
        {
            Debug.LogWarning("Menu settings wiring: SettingsButton has no Button component.");
            return;
        }

        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            UnityEditor.Events.UnityEventTools.RemovePersistentListener(button.onClick, i);
        }

        UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, menu.settingsButton);

        EditorUtility.SetDirty(button);
    }

    private static void WireMainMenu(MainMenu menu, MenuSettingsPanel panel)
    {
        SerializedObject so = new(menu);
        so.FindProperty("settingsPanel").objectReferenceValue = panel;
        SerializedProperty music = so.FindProperty("menuMusic");

        // Only filled when empty, so a track chosen by hand in the Inspector survives a re-run.
        if (music.objectReferenceValue == null) { music.objectReferenceValue = LoadClip(MenuMusicPath); }
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Destroys the three loose toggles and drops them out of MainMenu.menuButtons.
    ///
    /// The list entries have to go as well as the objects: a destroyed GameObject leaves a null in the
    /// list, and while SetMenuButtonsActive already skips nulls, leaving them there means the next
    /// person to read the Inspector sees three empty slots and cannot tell whether that is a bug.
    ///
    /// Idempotent - a second run finds nothing to remove.
    /// </summary>
    private static void RemoveRetiredToggles(RectTransform canvasRect, MainMenu menu)
    {
        int removed = 0;

        foreach (string name in RetiredToggles)
        {
            Transform found = canvasRect.Find(name);

            if (found == null) { continue; }

            Object.DestroyImmediate(found.gameObject);
            removed++;
        }

        SerializedObject so = new(menu);
        SerializedProperty list = so.FindProperty("menuButtons");

        for (int i = list.arraySize - 1; i >= 0; i--)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == null)
            {
                list.DeleteArrayElementAtIndex(i);
            }
        }

        so.ApplyModifiedProperties();

        if (removed > 0)
        {
            Debug.Log($"Menu settings wiring: removed {removed} loose toggle(s) - those settings now live "
                      + "in the Settings panel.");
        }
    }

    private static AudioClip LoadClip(string path)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

        if (clip == null) { Debug.LogWarning($"Menu settings wiring: no AudioClip at {path}."); }

        return clip;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;

        EditorUtility.SetDirty(rect);
    }

    private static Canvas FindCanvas()
    {
        foreach (Canvas candidate in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (candidate.name == "Canvas") { return candidate; }
        }

        return null;
    }

    private static bool OpenMenuScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);

        return true;
    }
}
