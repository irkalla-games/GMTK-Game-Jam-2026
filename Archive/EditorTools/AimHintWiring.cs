using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the "Press E to rotate" prompt into Game.unity's HUD Canvas, next to EndTurnButton, and points
/// AimHintLabel.root/label at it - CardPlayManager.Select/Deselect drive it from there, see
/// AimHintLabel's own header for why it needs no other wiring.
///
/// A menu command rather than hand-edited scene YAML for the same reason every other *Wiring script in
/// this project is one - see BattleHudWiring's header: the Editor holds Game.unity in memory while it is
/// open, so a hand edit to the .unity file underneath it would be discarded the next time the scene is
/// saved.
///
/// Idempotent: finds the label by name before creating it, and re-applies its position and style
/// unconditionally on every run, the same "repair, not skip" contract every content generator in this
/// project follows - see CLAUDE.md. Starts inactive; AimHintLabel.Show/Hide are the only things that
/// ever toggle it after this runs.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class AimHintWiring
{
    private const string ScenePath = "Assets/Scenes/Game.unity";
    private const string FontPath = "Assets/Extra Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    private const string CanvasName = "Canvas";
    private const string EndTurnButtonName = "EndTurnButton";
    private const string LabelName = "AimHintLabel";

    private const float Width = 220f;
    private const float Height = 32f;

    /// Gap kept above EndTurnButton's top edge.
    private const float Gap = 12f;

    [MenuItem("Tools/Battle HUD/Wire Aim Hint")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Aim hint wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenGameScene()) { return; }

        GameObject canvas = GameObject.Find(CanvasName);

        if (canvas == null)
        {
            Debug.LogError($"Aim hint wiring: no {CanvasName} in {ScenePath} - nowhere to put the label.");
            return;
        }

        Transform button = canvas.transform.Find(EndTurnButtonName);
        RectTransform buttonRect = button != null ? (RectTransform)button : null;

        if (buttonRect == null)
        {
            Debug.LogWarning($"Aim hint wiring: no {EndTurnButtonName} in {ScenePath} - the label will be "
                + "placed at a default position; re-run once the button exists to anchor it properly.");
        }

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        if (font == null)
        {
            Debug.LogError($"Aim hint wiring: font not found at {FontPath} - check the path still matches "
                + "the project's TMP install.");
            return;
        }

        WireLabel(canvas.transform, buttonRect, font);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log("Aim hint wiring: done - scene and assets saved.");
    }

    private static bool OpenGameScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);
        return true;
    }

    private static void WireLabel(Transform canvas, RectTransform buttonRect, TMP_FontAsset font)
    {
        Transform existing = canvas.Find(LabelName);
        GameObject root;
        AimHintLabel aimHint;
        Image backdrop;
        TMP_Text text;

        if (existing != null)
        {
            root = existing.gameObject;
            aimHint = root.GetComponent<AimHintLabel>();
            if (aimHint == null) { aimHint = root.AddComponent<AimHintLabel>(); }
            backdrop = root.GetComponent<Image>();
            text = root.GetComponentInChildren<TMP_Text>(includeInactive: true);
        }
        else
        {
            root = new GameObject(LabelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            root.transform.SetParent(canvas, false);
            root.layer = canvas.gameObject.layer;

            backdrop = root.GetComponent<Image>();
            aimHint = root.AddComponent<AimHintLabel>();

            GameObject textGo = new("Label", typeof(RectTransform));
            textGo.transform.SetParent(root.transform, false);
            textGo.layer = root.layer;

            text = textGo.AddComponent<TextMeshProUGUI>();

            Debug.Log($"Aim hint wiring: created {LabelName}.");
        }

        RectTransform rect = (RectTransform)root.transform;

        // Anchored above EndTurnButton, same corner it lives in, so it reads as belonging to the same
        // control rather than floating loose - re-derived from the button's own rect every run rather
        // than a literal position, the same "repair, not skip" reasoning NextWaveWiring's TurnCounter
        // move already documents for the identical shape.
        if (buttonRect != null)
        {
            rect.anchorMin = buttonRect.anchorMin;
            rect.anchorMax = buttonRect.anchorMax;
            rect.pivot = new Vector2(0.5f, 0f);

            float y = buttonRect.anchoredPosition.y + (buttonRect.sizeDelta.y / 2f) + Gap;
            rect.anchoredPosition = new Vector2(buttonRect.anchoredPosition.x, y);
        }
        else
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(-120f, 140f);
        }

        rect.sizeDelta = new Vector2(Width, Height);

        backdrop.color = PanelPalette.HeaderFill;
        backdrop.raycastTarget = false;

        RectTransform textRect = (RectTransform)text.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        text.font = font;
        text.fontSize = 20f;
        text.color = PanelPalette.Gold;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.text = "Press E to rotate";

        SerializedObject so = new(aimHint);
        so.FindProperty("root").objectReferenceValue = root;
        so.FindProperty("label").objectReferenceValue = text;
        so.ApplyModifiedProperties();

        // Hidden until CardPlayManager.Select actually arms a rotatable card - never visible at rest.
        root.SetActive(false);

        Debug.Log($"Aim hint wiring: {LabelName} positioned and wired.");
    }
}
