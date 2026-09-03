using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the pause menu and its settings sub-panel in Game.unity, wires BattleManager's battle track,
/// and removes the script-less AudioManager object AudioManager now replaces.
///
/// On its own Screen Space - OVERLAY canvas at sorting order 800, not on RewardCanvas. RewardCanvas is
/// order -1, which is BEHIND the main HUD at 0 - a pause menu placed there would be covered by the very
/// board it is pausing. Overlay also puts it above every Screen Space - Camera canvas in the scene
/// regardless of their orders, and 800 clears the tutorial canvases at 500 and 600.
///
/// Both components sit on the always-active canvas object with their roots as CHILDREN, which matters
/// more than it looks: PauseMenu polls Escape in Update, and a component on the object it hides can
/// never run Update to un-hide itself. Putting the component on the root it deactivates is a pause menu
/// that opens exactly once and then cannot be reopened.
/// </summary>
public static class PauseMenuWiring
{
    private const string ScenePath = "Assets/Scenes/Game.unity";

    private const string CanvasName = "PauseCanvas";
    private const string PauseRootName = "PauseRoot";
    private const string SettingsRootName = "SettingsRoot";
    private const string DebugRootName = "DebugRoot";

    private const string LegacyAudioObjectName = "AudioManager";

    private const string BattleMusicPath = "Assets/Sounds/Action 3 (Loop).wav";
    private const string SfxPreviewPath = "Assets/Sounds/Magic Spell_Coins_2.wav";

    private const int SortingOrder = 800;

    private static readonly Vector2 MenuButtonSize = new(320f, 60f);

    [MenuItem("Tools/UI/Wire Pause Menu")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Pause menu wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenGameScene()) { return; }

        Canvas canvas = EnsureCanvas();

        RectTransform canvasRect = (RectTransform)canvas.transform;

        PauseSettingsPanel settings = BuildSettingsPanel(canvasRect);
        PauseMenu menu = BuildPauseMenu(canvasRect, settings);

        OrderLayers(canvasRect);

        // See SharpSkin.RebuildLayout: RectTransform sizes are serialized, so the layout has to
        // settle before the scene is written or half-built numbers are what get saved.
        SharpSkin.RebuildLayout(canvasRect);

        WireBattleMusic();
        RemoveLegacyAudioObject();

        EditorUtility.SetDirty(menu);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log("Pause menu wiring: done - scene saved.");
    }

    /// <summary>
    /// Puts the settings panel in front of the pause menu.
    ///
    /// uGUI draws siblings in hierarchy order, so the LAST child is the one on top. Wire builds the
    /// settings panel first because the pause menu needs a reference to it, which left it behind the
    /// pause menu's own full-screen backdrop - visible only as a dimmed strip, and completely
    /// unclickable, since that backdrop takes raycasts. Ordered explicitly here rather than by
    /// juggling the build order, so the layering is stated rather than implied by a call sequence.
    /// </summary>
    private static void OrderLayers(RectTransform canvasRect)
    {
        Transform pause = canvasRect.Find(PauseRootName);
        Transform settings = canvasRect.Find(SettingsRootName);
        Transform debug = canvasRect.Find(DebugRootName);

        if (pause != null) { pause.SetSiblingIndex(0); }

        if (settings != null) { settings.SetAsLastSibling(); }

        // Debug last of all, so it draws over the settings panel for the same reason settings
        // draws over the pause menu: a backdrop that takes raycasts hides and disables anything
        // built before it.
        if (debug != null) { debug.SetAsLastSibling(); }
    }

    private static Canvas EnsureCanvas()
    {
        // FindObjectsByType with Include rather than GameObject.Find, which skips inactive objects -
        // a canvas someone had disabled would otherwise be missed and a second one built beside it.
        GameObject existing = null;

        foreach (Canvas candidate in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (candidate.name != CanvasName) { continue; }

            existing = candidate.gameObject;
            break;
        }

        if (existing == null)
        {
            existing = new GameObject(CanvasName, typeof(RectTransform));
        }

        Canvas canvas = SharpSkin.Ensure<Canvas>(existing);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        CanvasScaler scaler = SharpSkin.Ensure<CanvasScaler>(existing);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        SharpSkin.Ensure<GraphicRaycaster>(existing);

        EditorUtility.SetDirty(canvas);

        return canvas;
    }

    private static PauseMenu BuildPauseMenu(RectTransform canvasRect, PauseSettingsPanel settings)
    {
        PauseMenu menu = SharpSkin.Ensure<PauseMenu>(canvasRect.gameObject);

        RectTransform root = SharpSkin.EnsureChild(canvasRect, PauseRootName);
        Stretch(root);

        BuildBackdrop(root);

        RectTransform window = BuildWindow(root, "PAUSED", 420f);

        RectTransform body = SharpSkin.EnsureChild(window, "Body");
        VerticalLayoutGroup bodyLayout = SharpSkin.Ensure<VerticalLayoutGroup>(body.gameObject);
        bodyLayout.spacing = 10f;
        bodyLayout.childAlignment = TextAnchor.UpperCenter;
        bodyLayout.childForceExpandWidth = false;
        bodyLayout.childForceExpandHeight = false;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;

        ContentSizeFitter bodyFitter = SharpSkin.Ensure<ContentSizeFitter>(body.gameObject);
        bodyFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        bodyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Button resume = MenuButton(body, "ResumeButton", "Resume");
        Button settingsButton = MenuButton(body, "SettingsButton", "Settings");

        // The Debug section is built and left empty. Its contents - grant a card, add or remove
        // equipment, spawn and kill enemies - are a separate piece of work; what this reserves is the
        // place they go, so filling it in later is not also a re-layout of this menu.
        RectTransform debugSection = SharpSkin.EnsureChild(body, "DebugSection");
        VerticalLayoutGroup debugLayout = SharpSkin.Ensure<VerticalLayoutGroup>(debugSection.gameObject);
        debugLayout.spacing = 10f;
        debugLayout.childForceExpandWidth = false;
        debugLayout.childControlWidth = true;
        debugLayout.childControlHeight = true;

        ContentSizeFitter debugFitter = SharpSkin.Ensure<ContentSizeFitter>(debugSection.gameObject);
        debugFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        debugFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // A hint, not a button. The debug tools open on backquote and deliberately do NOT freeze
        // time - opening them from here would inherit this menu's freeze, which is the opposite
        // of what they are for. The section still exists so the tools are discoverable at all.
        DebugHint(debugSection);

        Button abandon = MenuButton(body, "AbandonButton", "Abandon Run");
        Button quit = MenuButton(body, "QuitButton", "Quit to Menu");

        SerializedObject so = new(menu);
        so.FindProperty("root").objectReferenceValue = root.gameObject;
        so.FindProperty("resumeButton").objectReferenceValue = resume;
        so.FindProperty("settingsButton").objectReferenceValue = settingsButton;
        so.FindProperty("abandonButton").objectReferenceValue = abandon;
        so.FindProperty("quitButton").objectReferenceValue = quit;
        so.FindProperty("debugSection").objectReferenceValue = debugSection.gameObject;
        so.FindProperty("settingsPanel").objectReferenceValue = settings;
        so.ApplyModifiedProperties();

        root.gameObject.SetActive(true);

        return menu;
    }

    private static PauseSettingsPanel BuildSettingsPanel(RectTransform canvasRect)
    {
        PauseSettingsPanel panel = SharpSkin.Ensure<PauseSettingsPanel>(canvasRect.gameObject);

        RectTransform root = SharpSkin.EnsureChild(canvasRect, SettingsRootName);
        Stretch(root);

        BuildBackdrop(root);

        RectTransform window = BuildWindow(root, "SETTINGS", PanelPalette.SettingsPanelWidth);

        RectTransform body = SharpSkin.EnsureChild(window, "Body");
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

        // No Debug Run row here: that setting only decides what the menu's Play button does, so it has
        // nothing to change in the middle of a battle.
        SettingsRowBuilder.GroupLabel(body, "GameGroup", "Game");
        Toggle tutorial = SettingsRowBuilder.ToggleRow(body, "TutorialRow", "Tutorial");
        Toggle intent = SettingsRowBuilder.ToggleRow(body, "IntentRow", "Show Intent Damage");

        Button back = BuildFooterButton(window, "BackButton", "Back");

        Transform tutorialLabel = body.Find("TutorialRow/Label");

        SerializedObject so = new(panel);
        so.FindProperty("root").objectReferenceValue = root.gameObject;
        so.FindProperty("backButton").objectReferenceValue = back;
        so.FindProperty("fullscreenToggle").objectReferenceValue = fullscreen;
        so.FindProperty("resolutionDropdown").objectReferenceValue = resolution;
        so.FindProperty("masterSlider").objectReferenceValue = master;
        so.FindProperty("musicSlider").objectReferenceValue = music;
        so.FindProperty("sfxSlider").objectReferenceValue = sfx;
        so.FindProperty("tutorialToggle").objectReferenceValue = tutorial;
        so.FindProperty("intentDamageToggle").objectReferenceValue = intent;
        so.FindProperty("sfxPreview").objectReferenceValue = LoadClip(SfxPreviewPath);

        if (tutorialLabel != null)
        {
            so.FindProperty("tutorialLabel").objectReferenceValue = tutorialLabel.GetComponent<TMP_Text>();
        }

        so.ApplyModifiedProperties();

        root.gameObject.SetActive(true);

        return panel;
    }

    private static void BuildBackdrop(RectTransform root)
    {
        RectTransform backdrop = SharpSkin.EnsureChild(root, "Backdrop");
        Stretch(backdrop);

        Image image = SharpSkin.Ensure<Image>(backdrop.gameObject);
        image.sprite = null;
        image.color = new Color(0f, 0f, 0f, 0.78f);

        // Takes raycasts on purpose. It does not stop a board click - those are OnMouseDown physics
        // raycasts that no Canvas intercepts, which is what BattleManager.InputLocked is for - but it
        // does stop clicks reaching the HUD buttons underneath.
        image.raycastTarget = true;
    }

    private static RectTransform BuildWindow(RectTransform root, string title, float width)
    {
        RectTransform window = SharpSkin.EnsureChild(root, "Window");
        window.anchorMin = new Vector2(0.5f, 0.5f);
        window.anchorMax = new Vector2(0.5f, 0.5f);
        window.pivot = new Vector2(0.5f, 0.5f);
        window.anchoredPosition = Vector2.zero;
        window.sizeDelta = new Vector2(width, 0f);
        window.localScale = Vector3.one;

        SharpSkin.ApplySliced(SharpSkin.Ensure<Image>(window.gameObject), SharpSkin.Panel);

        VerticalLayoutGroup layout = SharpSkin.Ensure<VerticalLayoutGroup>(window.gameObject);
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        ContentSizeFitter fitter = SharpSkin.Ensure<ContentSizeFitter>(window.gameObject);
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform header = SharpSkin.EnsureChild(window, "Header");
        SharpSkin.ApplySliced(SharpSkin.Ensure<Image>(header.gameObject), SharpSkin.Header);

        LayoutElement headerLayout = SharpSkin.Ensure<LayoutElement>(header.gameObject);
        headerLayout.minHeight = 64f;
        headerLayout.preferredHeight = 64f;

        RectTransform titleRect = SharpSkin.EnsureChild(header, "Title");
        Stretch(titleRect);

        TextMeshProUGUI label = SharpSkin.Ensure<TextMeshProUGUI>(titleRect.gameObject);

        TMP_FontAsset font = SharpSkin.LoadFont();

        if (font != null) { label.font = font; }

        label.text = title;
        label.fontSize = 28f;
        label.characterSpacing = PanelPalette.HeaderTitleSpacing;
        label.color = PanelPalette.Gold;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        EditorUtility.SetDirty(label);

        return window;
    }

    /// <summary>
    /// A one-line note in the pause menu saying the debug tools exist and which key opens them.
    ///
    /// Discoverability only: nothing here opens the panel, because a panel opened from a menu that
    /// holds TimeFreeze would come up with the world stopped, and watching the world run is the
    /// entire reason those tools do not freeze it.
    /// </summary>
    private static void DebugHint(RectTransform section)
    {
        RectTransform rect = SharpSkin.EnsureChild(section, "DebugHint");

        LayoutElement layout = SharpSkin.Ensure<LayoutElement>(rect.gameObject);
        layout.preferredHeight = PanelPalette.ActionRowHeight;
        layout.flexibleWidth = 1f;

        TextMeshProUGUI label = SharpSkin.Ensure<TextMeshProUGUI>(rect.gameObject);

        TMP_FontAsset font = SharpSkin.LoadFont();

        if (font != null) { label.font = font; }

        label.text = "press  `  for debug tools";
        label.fontSize = PanelPalette.SettingsGroupLabelSize;
        label.color = PanelPalette.LabelGrey;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        EditorUtility.SetDirty(label);
    }

    private static Button MenuButton(RectTransform parent, string objectName, string text)
    {
        RectTransform rect = SharpSkin.EnsureChild(parent, objectName);

        LayoutElement layout = SharpSkin.Ensure<LayoutElement>(rect.gameObject);
        layout.preferredWidth = MenuButtonSize.x;
        layout.preferredHeight = MenuButtonSize.y;
        layout.flexibleWidth = 0f;

        return FinishButton(rect, text, 26f);
    }

    private static Button BuildFooterButton(RectTransform window, string objectName, string text)
    {
        RectTransform footer = SharpSkin.EnsureChild(window, "Footer");

        HorizontalLayoutGroup layout = SharpSkin.Ensure<HorizontalLayoutGroup>(footer.gameObject);
        layout.padding = new RectOffset(0, 0, 12, 0);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        LayoutElement footerLayout = SharpSkin.Ensure<LayoutElement>(footer.gameObject);
        footerLayout.minHeight = 76f;
        footerLayout.preferredHeight = 76f;

        RectTransform rect = SharpSkin.EnsureChild(footer, objectName);

        LayoutElement buttonLayout = SharpSkin.Ensure<LayoutElement>(rect.gameObject);
        buttonLayout.preferredWidth = 260f;
        buttonLayout.preferredHeight = 56f;

        return FinishButton(rect, text, 26f);
    }

    private static Button FinishButton(RectTransform rect, string text, float fontSize)
    {
        Button button = SharpSkin.Ensure<Button>(rect.gameObject);

        RectTransform labelRect = SharpSkin.EnsureChild(rect, "Label");
        Stretch(labelRect);

        TextMeshProUGUI label = SharpSkin.Ensure<TextMeshProUGUI>(labelRect.gameObject);

        TMP_FontAsset font = SharpSkin.LoadFont();

        if (font != null) { label.font = font; }

        label.text = text;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        SharpSkin.ApplyButton(button);

        return button;
    }

    /// Only filled when empty, so a track chosen by hand in the Inspector survives a re-run.
    private static void WireBattleMusic()
    {
        BattleManager battle = Object.FindAnyObjectByType<BattleManager>(FindObjectsInactive.Include);

        if (battle == null)
        {
            Debug.LogWarning("Pause menu wiring: no BattleManager in the scene - battle music not wired.");
            return;
        }

        SerializedObject so = new(battle);
        SerializedProperty music = so.FindProperty("battleMusic");

        if (music.objectReferenceValue == null) { music.objectReferenceValue = LoadClip(BattleMusicPath); }

        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Deletes the script-less AudioManager object under ---AUDIO.
    ///
    /// It was a Transform plus an AudioSource on Play On Awake, with no script and no mixer group, which
    /// meant its track restarted from the top on every level load and its volume answered to nothing.
    /// The AudioManager component now does that job across scene loads, so leaving this in place would
    /// be two tracks playing at once.
    ///
    /// Matched by name AND by having no MonoBehaviour on it, so this cannot eat a real AudioManager if
    /// one is ever placed in the scene by hand.
    /// </summary>
    private static void RemoveLegacyAudioObject()
    {
        foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
        {
            if (source.gameObject.name != LegacyAudioObjectName) { continue; }

            if (source.GetComponent<MonoBehaviour>() != null) { continue; }

            Debug.Log("Pause menu wiring: removed the script-less AudioManager object - AudioManager now "
                      + "owns music across scene loads.");

            Object.DestroyImmediate(source.gameObject);

            return;
        }
    }

    private static AudioClip LoadClip(string path)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

        if (clip == null) { Debug.LogWarning($"Pause menu wiring: no AudioClip at {path}."); }

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

    private static bool OpenGameScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);

        return true;
    }
}
