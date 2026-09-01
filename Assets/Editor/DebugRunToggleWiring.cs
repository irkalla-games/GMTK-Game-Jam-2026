using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Adds the Main Menu's "Debug run" toggle, and re-lays all three toggles as one horizontal row.
///
/// A near-copy of IntentDamageToggleWiring, which is itself the copy of TutorialToggleWiring - see that
/// file for why this is a menu command rather than hand-edited scene YAML.
///
/// The row is the point. Stacked vertically the toggles ran off the bottom of the canvas: the third one
/// would have sat at y = -690 against a 1080-tall canvas whose bottom edge is -540, and the second was
/// already half over it at -570. Going sideways means a fourth toggle later costs horizontal room,
/// which there is plenty of, instead of vertical room, which there is none of.
///
/// Unlike the two scripts it copies, this one also repositions toggles it did not create. That is safe
/// to re-run: those scripts only apply their own position when creating a toggle from scratch, so they
/// will not drag it back afterwards.
///
/// Editor-only, and only covered by Tools/compile-check.ps1 with -IncludeEditor.
/// </summary>
public static class DebugRunToggleWiring
{
    private const string ScenePath = "Assets/Scenes/MainMenu.unity";
    private const string DebugRunAssetPath = "Assets/Data/RunData/DebugTestbedRun.asset";

    private const string ToggleName = "DebugRunToggle";
    private const string BackgroundName = "Background";
    private const string CheckmarkName = "Checkmark";
    private const string LabelName = "Label";

    /// The three toggles, left to right, in the order they should read.
    private static readonly string[] RowOrder = { "TutorialToggle", "IntentDamageToggle", ToggleName };

    /// <summary>
    /// Where the row sits and how wide it spreads.
    ///
    /// -430 clears the Exit button, whose 200-tall rect at y = -250 reaches down to -350, and leaves
    /// the row's own scaled half-height of 40 well inside the canvas edge at -540.
    /// </summary>
    private static readonly float RowY = -430f;

    private const float ColumnSpacing = 420f;

    private static readonly Vector2 ToggleSize = new(500f, 100f);

    /// <summary>
    /// 20% smaller, applied as scale rather than a smaller rect.
    ///
    /// Shrinking sizeDelta would move the box and re-flow the label's rect while leaving the font at
    /// its authored 70pt, so the text would grow relative to its own toggle and the three would stop
    /// matching. Scale shrinks the box, the tick and the caption by the same factor, which is what "20%
    /// smaller" means when you look at it.
    /// </summary>
    private static readonly Vector3 ToggleScale = new(0.8f, 0.8f, 1f);

    private const float BoxSize = 80f;

    [MenuItem("Tools/Main Menu/Wire Debug Run Toggle")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Debug run toggle wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenMenuScene()) { return; }

        MainMenu menu = Object.FindAnyObjectByType<MainMenu>(FindObjectsInactive.Include);

        if (menu == null)
        {
            Debug.LogError($"Debug run toggle wiring: no MainMenu component in {ScenePath} - nothing to wire against.");
            return;
        }

        Canvas canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);

        if (canvas == null)
        {
            Debug.LogError($"Debug run toggle wiring: no Canvas in {ScenePath} - nowhere to put the toggle.");
            return;
        }

        Toggle toggle = EnsureToggle(canvas.transform);

        // The authored state is only what the button looks like before MainMenu.Start reads
        // GameSettings, but matching it here keeps the scene view honest.
        toggle.SetIsOnWithoutNotify(GameSettings.DebugRunEnabled);

        LayOutRow(canvas.transform);

        SerializedObject so = new(menu);
        SetIfEmpty(so, "debugRunToggle", toggle);
        SetIfEmpty(so, "debugRun", LoadDebugRun());
        AddToMenuButtons(so, toggle.gameObject);
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log("Debug run toggle wiring: done - scene saved.");
    }

    /// <summary>
    /// Puts the three toggles side by side, centred on the canvas and scaled down together.
    ///
    /// Positions every toggle it finds rather than only the new one, because the row is a property of
    /// the set: moving one without the others is what produced the off-screen stack this replaces. A
    /// toggle that is missing is skipped with a warning rather than shifting everything left to close
    /// the gap - a hole in the row is a much clearer symptom than a silently re-centred pair.
    /// </summary>
    private static void LayOutRow(Transform canvas)
    {
        float leftmost = -ColumnSpacing * (RowOrder.Length - 1) / 2f;

        for (int i = 0; i < RowOrder.Length; i++)
        {
            Transform found = canvas.Find(RowOrder[i]);

            if (found == null)
            {
                Debug.LogWarning($"Debug run toggle wiring: no {RowOrder[i]} under the Canvas - "
                                 + "leaving a gap in the row.");
                continue;
            }

            RectTransform rect = found.GetComponent<RectTransform>();
            if (rect == null) { continue; }

            Centre(rect);
            rect.anchoredPosition = new Vector2(leftmost + (ColumnSpacing * i), RowY);
            rect.sizeDelta = ToggleSize;
            rect.localScale = ToggleScale;
        }
    }

    private static RunData LoadDebugRun()
    {
        RunData run = AssetDatabase.LoadAssetAtPath<RunData>(DebugRunAssetPath);

        if (run == null)
        {
            Debug.LogWarning($"Debug run toggle wiring: no {DebugRunAssetPath} yet - run "
                             + "Tools/Debug/Generate Debug Testbed, then re-run this to fill the field. "
                             + "Until then the toggle is inert and Play behaves normally.");
        }

        return run;
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
            Debug.Log($"Debug run toggle wiring: reusing the existing {ToggleName}.");
            return found;
        }

        GameObject host = new(ToggleName, typeof(RectTransform));
        host.transform.SetParent(canvas, false);
        host.layer = canvas.gameObject.layer;

        RectTransform rect = host.GetComponent<RectTransform>();
        Centre(rect);
        rect.sizeDelta = ToggleSize;

        GameObject background = CreateBox(host.transform);
        GameObject checkmark = CreateCheckmark(background.transform);
        TMP_Text label = CreateLabel(host.transform);

        Toggle toggle = host.AddComponent<Toggle>();

        // targetGraphic is what tints on hover; graphic is what appears when the box is ticked.
        toggle.targetGraphic = background.GetComponent<Image>();
        toggle.graphic = checkmark.GetComponent<Image>();
        toggle.transition = Selectable.Transition.ColorTint;

        Debug.Log($"Debug run toggle wiring: created {ToggleName} with label \"{label.text}\".");

        return toggle;
    }

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
        image.color = new Color(0.196f, 0.196f, 0.196f, 1f);

        return check;
    }

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
        text.text = "Debug run";
        text.fontSize = 70f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Left;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        // The click has to land on the Toggle, not be eaten by its own caption.
        text.raycastTarget = false;

        TMP_Text sample = Object.FindAnyObjectByType<TMP_Text>(FindObjectsInactive.Include);

        if (sample != null && sample.font != null) { text.font = sample.font; }

        return text;
    }

    private static Sprite BuiltinSprite(string path)
    {
        Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(path);

        if (sprite == null)
        {
            Debug.LogWarning($"Debug run toggle wiring: built-in sprite {path} not found - assign one by hand.");
        }

        return sprite;
    }

    private static void Centre(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    /// Assigns only an unset field, so anything swapped by hand survives a re-run.
    private static void SetIfEmpty(SerializedObject so, string property, Object value)
    {
        SerializedProperty field = so.FindProperty(property);

        if (field.objectReferenceValue == null && value != null) { field.objectReferenceValue = value; }
    }

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
