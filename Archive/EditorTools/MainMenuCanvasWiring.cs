using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Normalises MainMenu.unity's Canvas, then lays out and skins the title and the three menu buttons.
///
/// The canvas was the odd one out in the project: World Space, hand-scaled to 7.1058497, a 1980x1080
/// rect (1920 with a typo) and Constant Pixel Size, while every canvas in Game.unity is Scale With
/// Screen Size at 1920x1080 on Expand. Nothing about the menu needed world space - the scene holds
/// nineteen CanvasRenderers and not one SpriteRenderer - so the whole arrangement was a way to make a
/// world-space canvas fill the screen, which is what Screen Space does by itself.
///
/// Screen Space - OVERLAY rather than the Camera mode Game.unity uses. Game.unity needs Camera because
/// its board is world-space sprites that have to interleave with the UI; this scene has no world
/// content to interleave with, and Overlay drops the camera reference, the plane distance and the
/// camera's clip planes out of the arrangement entirely. The CanvasScaler settings - the half that has
/// to match Game.unity - are identical either way.
///
/// A menu command rather than hand-edited scene YAML for the reason every wiring script here repeats:
/// the Editor holds the scene in memory while it is open and silently discards file edits on its next
/// save. Idempotent, and re-applies every value on every run, so a changed constant is one re-run away
/// from shipping - see TooltipPanelWiring for the pattern.
///
/// Deliberately does NOT touch the three toggles. They keep their authored row at y -430 until the
/// Settings panel replaces them, so no setting is unreachable between one change and the next;
/// Tools/Main Menu/Wire Debug Run Toggle still owns that row's layout.
/// </summary>
public static class MainMenuCanvasWiring
{
    private const string ScenePath = "Assets/Scenes/MainMenu.unity";

    private const string CanvasName = "Canvas";

    /// The full-screen backdrop. Named "Image" in the scene, not "Background" - the three objects that
    /// ARE called Background all belong to toggles, and Find would never have reached them anyway.
    private const string BackdropName = "Image";

    /// Deliberately not "Title": CharacterSelectPanel/Backdrop already owns an object by that name, and
    /// two objects called Title in one scene is the kind of ambiguity that makes the next Find pick the
    /// wrong one.
    private const string TitleName = "TitleLabel";

    /// The buttons, top to bottom, with the y each sits at. Above the toggle row at -430.
    private static readonly (string Name, float Y)[] Column =
    {
        ("PlayButton", 60f),
        ("SettingsButton", -40f),
        ("ExitButton", -140f),
    };

    private static readonly Vector2 ButtonSize = new(360f, 76f);

    private static readonly Vector2 TitleSize = new(1400f, 200f);

    private const float TitleY = 280f;

    private const float TitleFontSize = 96f;

    private const float ButtonLabelSize = 30f;

    [MenuItem("Tools/Main Menu/Normalise Canvas")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Main menu canvas wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenMenuScene()) { return; }

        Canvas canvas = FindCanvas();

        if (canvas == null)
        {
            Debug.LogError($"Main menu canvas wiring: no GameObject named {CanvasName} in {ScenePath}.");
            return;
        }

        NormaliseCanvas(canvas);
        NormaliseCamera();

        RectTransform canvasRect = (RectTransform)canvas.transform;

        StretchBackdrop(canvasRect);
        RegisterInMenuButtons(EnsureTitle(canvasRect));
        LayOutButtons(canvasRect);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log("Main menu canvas wiring: done - scene saved.");
    }

    /// <summary>
    /// Screen Space - Overlay at 1920x1080 on Expand, with the world-space scale undone.
    ///
    /// The localScale reset is the part that is easy to miss: switching render mode leaves the
    /// 7.1058497 scale on the RectTransform, and a Screen Space canvas scaled seven times over renders
    /// its children seven times too large with no error to explain it.
    /// </summary>
    private static void NormaliseCanvas(Canvas canvas)
    {
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        canvas.pixelPerfect = false;

        RectTransform rect = (RectTransform)canvas.transform;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.localPosition = Vector3.zero;

        CanvasScaler scaler = SharpSkin.Ensure<CanvasScaler>(canvas.gameObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // Expand, matching every canvas in Game.unity. CLAUDE.md explains why that pairing is
        // load-bearing there: CameraFrame keeps a fixed 19.2 x 10.8 world frame visible and Expand is
        // what makes the canvas scale by the identical factor. This scene has no board to stay in step
        // with, but a menu that letterboxes differently from the game it opens is its own bug.
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.referencePixelsPerUnit = 100f;

        SharpSkin.Ensure<GraphicRaycaster>(canvas.gameObject);

        EditorUtility.SetDirty(canvas);
        EditorUtility.SetDirty(scaler);
    }

    /// <summary>
    /// Brings the camera back to something ordinary now that nothing depends on it.
    ///
    /// It was orthographic at size 3800 and clearing to Nothing - both only ever made sense as a way to
    /// frame a canvas floating in world space. With an Overlay canvas the camera paints nothing but the
    /// ground behind it, so it clears to a solid colour instead: "Nothing" leaves whatever was in the
    /// buffer last frame, which smears the moment anything fails to cover the full screen.
    /// </summary>
    private static void NormaliseCamera()
    {
        Camera camera = Object.FindAnyObjectByType<Camera>(FindObjectsInactive.Include);

        if (camera == null)
        {
            Debug.LogWarning("Main menu canvas wiring: no Camera in the scene - skipped camera normalisation.");
            return;
        }

        camera.orthographic = true;
        camera.orthographicSize = 5.4f;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 1000f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = PanelPalette.PanelFill;
        camera.transform.position = new Vector3(0f, 0f, -10f);

        EditorUtility.SetDirty(camera);
    }

    /// <summary>
    /// The backdrop stretches to the canvas rather than carrying a hand-set size, so it stays correct at
    /// every aspect ratio Expand can hand it.
    ///
    /// Re-tinted from pure black to PanelPalette.PanelFill: black reads as a hole next to the warm
    /// near-black the Sharp GUI panels use, and the menu is the first thing the game shows.
    /// </summary>
    private static void StretchBackdrop(RectTransform canvasRect)
    {
        Transform found = canvasRect.Find(BackdropName);

        if (found == null)
        {
            Debug.LogWarning($"Main menu canvas wiring: no {BackdropName} under the Canvas - skipped backdrop.");
            return;
        }

        RectTransform rect = (RectTransform)found;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;

        Image image = found.GetComponent<Image>();

        if (image != null)
        {
            image.color = PanelPalette.PanelFill;
            EditorUtility.SetDirty(image);
        }

        EditorUtility.SetDirty(rect);
    }

    /// <summary>
    /// Creates the menu's title if it has none, and restyles it either way.
    ///
    /// The scene genuinely had no title - what looked like one in the hierarchy is
    /// CharacterSelectPanel/Backdrop/Title, the select screen's "Choose Your Party" heading. Created
    /// here rather than left to be authored by hand so a fresh checkout gets the same menu, and named
    /// from Application.productName so it cannot drift from what the built game calls itself.
    /// </summary>
    private static RectTransform EnsureTitle(RectTransform canvasRect)
    {
        RectTransform rect = SharpSkin.EnsureChild(canvasRect, TitleName);

        Centre(rect, new Vector2(0f, TitleY), TitleSize);

        TextMeshProUGUI label = SharpSkin.Ensure<TextMeshProUGUI>(rect.gameObject);

        TMP_FontAsset font = SharpSkin.LoadFont();

        if (font != null) { label.font = font; }

        label.text = Application.productName;
        label.fontSize = TitleFontSize;
        label.color = PanelPalette.Gold;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;

        EditorUtility.SetDirty(label);

        return rect;
    }

    /// <summary>
    /// Centres the three buttons in a column and skins each one.
    ///
    /// A button that is missing is warned about and skipped rather than closing the gap, matching
    /// the rule the retired toggle-row wiring used: a hole is a far clearer symptom than a
    /// silently re-centred pair.
    ///
    /// SettingsButton is force-activated. It was authored inactive with an empty onClick, so it was
    /// invisible in the menu and invisible in any screenshot of it - the button is skinned and placed
    /// here, and Tools/Main Menu/Wire Settings Panel gives it something to open.
    /// </summary>
    private static void LayOutButtons(RectTransform canvasRect)
    {
        foreach ((string name, float y) in Column)
        {
            Transform found = canvasRect.Find(name);

            if (found == null)
            {
                Debug.LogWarning($"Main menu canvas wiring: no {name} under the Canvas - skipped.");
                continue;
            }

            if (!found.gameObject.activeSelf)
            {
                found.gameObject.SetActive(true);
                Debug.Log($"Main menu canvas wiring: {name} was inactive in the scene - enabled it.");
            }

            Centre((RectTransform)found, new Vector2(0f, y), ButtonSize);

            Button button = found.GetComponent<Button>();

            if (button == null)
            {
                Debug.LogWarning($"Main menu canvas wiring: {name} has no Button component - not skinned.");
                continue;
            }

            SharpSkin.ApplyButton(button);

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(includeInactive: true);

            if (label == null) { continue; }

            label.fontSize = ButtonLabelSize;
            label.alignment = TextAlignmentOptions.Center;

            // The label fills the button, so centring is the button's own geometry doing the work
            // rather than a hand-set offset that would drift the moment ButtonSize changes.
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            labelRect.localScale = Vector3.one;

            EditorUtility.SetDirty(label);
        }
    }

    /// Anchors to the canvas centre and sets the size, so a rect authored against the old 1980x1080
    /// world-space canvas stops carrying stretched anchors into the new one.
    private static void Centre(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        EditorUtility.SetDirty(rect);
    }

    /// <summary>
    /// Adds the title to MainMenu's menuButtons list, which is what Play hides when it opens the
    /// character-select screen.
    ///
    /// Without this the title stayed up over the select screen, sitting on top of "Choose Your Party".
    /// menuButtons is already how Play/Settings/Exit and the three toggles get hidden - the list is a
    /// set of loose objects rather than a container precisely so a new one can join without reparenting
    /// anything - the list is a set of loose objects precisely so a new one can join without one.
    ///
    /// Idempotent: a second run finds the title already in the list and leaves it alone.
    /// </summary>
    private static void RegisterInMenuButtons(RectTransform title)
    {
        MainMenu menu = Object.FindAnyObjectByType<MainMenu>(FindObjectsInactive.Include);

        if (menu == null)
        {
            Debug.LogWarning("Main menu canvas wiring: no MainMenu component - the title will not hide "
                             + "when the character-select screen opens.");
            return;
        }

        SerializedObject so = new(menu);
        SerializedProperty list = so.FindProperty("menuButtons");

        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == title.gameObject) { return; }
        }

        list.InsertArrayElementAtIndex(list.arraySize);
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = title.gameObject;
        so.ApplyModifiedProperties();
    }

    private static Canvas FindCanvas()
    {
        foreach (Canvas candidate in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (candidate.name == CanvasName) { return candidate; }
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
