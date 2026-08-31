using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-shot wiring for the Main Menu's "show enemy damage" toggle: builds the Toggle hierarchy under
/// the menu's Canvas and hands it to MainMenu.intentDamageToggle, also folding it into
/// MainMenu.menuButtons so it hides and restores with the rest of the menu.
///
/// A near-copy of TutorialToggleWiring, which already is the template for this - see that file's own
/// doc comment for why a menu command is used instead of hand-edited scene YAML, and why going through
/// SerializedObject matters here too.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 - that script drives
/// Assembly-CSharp.csproj, which never lists Assets/Editor. Verify by focusing the Editor and checking
/// the console, per CLAUDE.md.
/// </summary>
public static class IntentDamageToggleWiring
{
    private const string ScenePath = "Assets/Scenes/MainMenu.unity";

    private const string ToggleName = "IntentDamageToggle";
    private const string BackgroundName = "Background";
    private const string CheckmarkName = "Checkmark";
    private const string LabelName = "Label";

    /// Below TutorialToggle, which sits at y = -450 and is 100 tall.
    private static readonly Vector2 TogglePosition = new(0f, -570f);

    private static readonly Vector2 ToggleSize = new(500f, 100f);

    private const float BoxSize = 80f;

    [MenuItem("Tools/Main Menu/Wire Intent Damage Toggle")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Intent damage toggle wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenMenuScene()) { return; }

        MainMenu menu = Object.FindAnyObjectByType<MainMenu>(FindObjectsInactive.Include);

        if (menu == null)
        {
            Debug.LogError($"Intent damage toggle wiring: no MainMenu component in {ScenePath} - nothing to wire against.");
            return;
        }

        Canvas canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);

        if (canvas == null)
        {
            Debug.LogError($"Intent damage toggle wiring: no Canvas in {ScenePath} - nowhere to put the toggle.");
            return;
        }

        Toggle toggle = EnsureToggle(canvas.transform);

        // The authored state is only what the button looks like before MainMenu.Start reads
        // GameSettings, but matching it here keeps the scene view honest.
        toggle.SetIsOnWithoutNotify(GameSettings.ShowIntentDamage);

        SerializedObject so = new(menu);
        SetIfEmpty(so, "intentDamageToggle", toggle);
        AddToMenuButtons(so, toggle.gameObject);
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log("Intent damage toggle wiring: done - scene saved.");
    }

    private static bool OpenMenuScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);
        return true;
    }

    private static Toggle EnsureToggle(Transform canvas)
    {
        Transform existing = canvas.Find(ToggleName);

        if (existing != null && existing.TryGetComponent(out Toggle found))
        {
            Debug.Log($"Intent damage toggle wiring: reusing the existing {ToggleName}.");
            return found;
        }

        GameObject host = new(ToggleName, typeof(RectTransform));
        host.transform.SetParent(canvas, false);
        host.layer = canvas.gameObject.layer;

        RectTransform rect = host.GetComponent<RectTransform>();
        Centre(rect);
        rect.anchoredPosition = TogglePosition;
        rect.sizeDelta = ToggleSize;

        GameObject background = CreateBox(host.transform);
        GameObject checkmark = CreateCheckmark(background.transform);
        TMP_Text label = CreateLabel(host.transform);

        Toggle toggle = host.AddComponent<Toggle>();

        // targetGraphic is what tints on hover; graphic is what appears when the box is ticked. Two
        // different fields, and swapping them gives a toggle that fades its own frame in and out.
        toggle.targetGraphic = background.GetComponent<Image>();
        toggle.graphic = checkmark.GetComponent<Image>();
        toggle.transition = Selectable.Transition.ColorTint;

        Debug.Log($"Intent damage toggle wiring: created {ToggleName} with label \"{label.text}\".");

        return toggle;
    }

    /// The tick box itself, pinned to the left edge so the label reads out from it.
    private static GameObject CreateBox(Transform parent)
    {
        GameObject box = new(BackgroundName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        box.transform.SetParent(parent, false);
        box.layer = parent.gameObject.layer;

        RectTransform rect = box.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(BoxSize, BoxSize);

        Image image = box.GetComponent<Image>();
        image.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
        image.type = Image.Type.Sliced;

        return box;
    }

    private static GameObject CreateCheckmark(Transform parent)
    {
        GameObject check = new(CheckmarkName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        check.transform.SetParent(parent, false);
        check.layer = parent.gameObject.layer;

        RectTransform rect = check.GetComponent<RectTransform>();
        Centre(rect);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(BoxSize * 0.8f, BoxSize * 0.8f);

        Image image = check.GetComponent<Image>();
        image.sprite = BuiltinSprite("UI/Skin/Checkmark.psd");

        // The menu's own buttons are dark-on-light; the box is the same light sprite, so the tick has
        // to be the dark half of that pair rather than the white the label uses.
        image.color = new Color(0.196f, 0.196f, 0.196f, 1f);

        return check;
    }

    /// <summary>
    /// White, unlike the button labels: those sit on a light UISprite, this sits straight on the
    /// menu's black background Image. Sized well under the buttons' 140 so it reads as an option
    /// rather than a fourth thing to press.
    /// </summary>
    private static TMP_Text CreateLabel(Transform parent)
    {
        GameObject go = new(LabelName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(BoxSize + 24f, 0f);
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = "Show enemy damage";
        text.fontSize = 70f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Left;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        // The click has to land on the Toggle, not be eaten by its own caption.
        text.raycastTarget = false;

        // Match whatever font the rest of the menu already uses rather than leaving TMP's default.
        TMP_Text sample = Object.FindAnyObjectByType<TMP_Text>(FindObjectsInactive.Include);

        if (sample != null && sample.font != null) { text.font = sample.font; }

        return text;
    }

    private static Sprite BuiltinSprite(string path)
    {
        Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(path);

        if (sprite == null)
        {
            Debug.LogWarning($"Intent damage toggle wiring: built-in sprite {path} not found - assign one by hand.");
        }

        return sprite;
    }

    private static void Centre(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    /// Assigns only an unset field, so a toggle swapped by hand survives a re-run.
    private static void SetIfEmpty(SerializedObject so, string property, Object value)
    {
        SerializedProperty field = so.FindProperty(property);

        if (field.objectReferenceValue == null && value != null) { field.objectReferenceValue = value; }
    }

    /// <summary>
    /// Appends `toggleObject` to MainMenu.menuButtons if it is not already listed, so the new toggle
    /// hides and restores alongside Play/Settings/Exit/TutorialToggle - see that field's own tooltip.
    /// Guarded so re-running this after nudging the toggle's position never duplicates the entry.
    /// </summary>
    private static void AddToMenuButtons(SerializedObject so, GameObject toggleObject)
    {
        SerializedProperty list = so.FindProperty("menuButtons");

        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == toggleObject) { return; }
        }

        list.InsertArrayElementAtIndex(list.arraySize);
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = toggleObject;
    }
}
