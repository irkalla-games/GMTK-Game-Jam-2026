using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds WaveCircle.prefab the first time this runs, wires a NextWavePanel onto the main HUD Canvas at
/// the top-left corner TurnCounter used to occupy, and moves TurnCounter itself to sit left of
/// EndTurnButton - freeing that corner is the whole point of the move.
///
/// A menu command rather than hand-edited scene YAML because the Editor holds Game.unity in memory while
/// it is open - anything written to that file underneath it is discarded the next time the scene is
/// saved. Going through SerializedObject also means private [SerializeField] fields are set the same way
/// the Inspector sets them, dirty flags and undo included. Same shape as PartyPortraitWiring/BattleHudWiring.
///
/// Idempotent: TurnCounter's position and the panel's row position are read live off EndTurnButton and
/// re-applied unconditionally on every run - the whole point of a "move this" tool - while the prefab
/// itself is only ever built once and then reused.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class NextWaveWiring
{
    private const string ScenePath = "Assets/Scenes/Game.unity";
    private const string WaveCirclePrefabPath = "Assets/Prefabs/UI/WaveCircle.prefab";
    private const string CircleSpritePath = "Assets/Extra Assets/Dark UI/Free/CIRCLE4PXSMA.png";

    private const string CanvasName = "Canvas";
    private const string TurnCounterName = "TurnCounter";
    private const string EndTurnButtonName = "EndTurnButton";
    private const string NextWavePanelName = "NextWavePanel";
    private const string RowName = "Row";

    private const float CircleSize = 72f;
    private const float CircleSpacing = 84f;

    /// Gap kept between TurnCounter's moved position and EndTurnButton's left edge, and between the
    /// screen's own top-left corner and the first wave circle.
    private const float Gap = 16f;

    // Row's anchor point is where circle 0's centre lands (WaveCircle.prefab is centre-pivoted, the
    // same as every other Image CreateImage below builds) - so it has to sit half a circle in from both
    // screen edges for that first circle to land fully on screen, not just Gap in.
    private static readonly Vector2 RowAnchoredPosition =
        new(Gap + (CircleSize / 2f), -(Gap + (CircleSize / 2f)));

    [MenuItem("Tools/Battle HUD/Wire Next Wave Preview")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Next wave wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenGameScene()) { return; }

        GameObject canvas = GameObject.Find(CanvasName);

        if (canvas == null)
        {
            Debug.LogError($"Next wave wiring: no {CanvasName} in {ScenePath} - nowhere to put the panel.");
            return;
        }

        MoveTurnCounter(canvas.transform);

        WaveCircle circlePrefab = EnsureWaveCirclePrefab();

        if (circlePrefab != null) { WireNextWavePanel(canvas.transform, circlePrefab); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log("Next wave wiring: done - scene and assets saved.");
    }

    private static bool OpenGameScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);
        return true;
    }

    // ------------------------------------------------------------------------------------------
    // TurnCounter - moved to sit left of EndTurnButton, read live off that button's own rect so a
    // re-run always tracks wherever the button currently is.
    // ------------------------------------------------------------------------------------------

    private static void MoveTurnCounter(Transform canvas)
    {
        Transform counter = canvas.Find(TurnCounterName);

        if (counter == null)
        {
            Debug.LogWarning($"Next wave wiring: no {TurnCounterName} in {ScenePath} - not moved.");
            return;
        }

        Transform button = canvas.Find(EndTurnButtonName);

        if (button == null)
        {
            Debug.LogWarning($"Next wave wiring: no {EndTurnButtonName} in {ScenePath} - "
                + $"{TurnCounterName} left where it was; re-run once the button exists.");
            return;
        }

        RectTransform counterRect = (RectTransform)counter;
        RectTransform buttonRect = (RectTransform)button;

        counterRect.anchorMin = new Vector2(1f, 1f);
        counterRect.anchorMax = new Vector2(1f, 1f);
        counterRect.pivot = new Vector2(0.5f, 0.5f);

        float buttonLeftEdge = buttonRect.anchoredPosition.x - (buttonRect.sizeDelta.x / 2f);
        float x = buttonLeftEdge - Gap - (counterRect.sizeDelta.x / 2f);

        counterRect.anchoredPosition = new Vector2(x, buttonRect.anchoredPosition.y);
    }

    // ------------------------------------------------------------------------------------------
    // WaveCircle prefab - built once, the first time this ever runs
    // ------------------------------------------------------------------------------------------

    private static WaveCircle EnsureWaveCirclePrefab()
    {
        WaveCircle existing = AssetDatabase.LoadAssetAtPath<WaveCircle>(WaveCirclePrefabPath);

        if (existing != null) { return existing; }

        Sprite circleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpritePath);

        if (circleSprite == null)
        {
            Debug.LogError($"Next wave wiring: circle sprite not found at {CircleSpritePath} - "
                + "cannot build WaveCircle.prefab.");
            return null;
        }

        GameObject host = new("WaveCircle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform hostRect = host.GetComponent<RectTransform>();
        Centre(hostRect);
        hostRect.sizeDelta = new Vector2(CircleSize, CircleSize);

        // The circle frame itself is what receives the pointer events WaveCircle listens for - it keeps
        // raycastTarget at Image's own default of true, so hovering anywhere inside the circle counts,
        // not just the portrait art inside it.
        Image frame = host.GetComponent<Image>();
        frame.sprite = circleSprite;
        frame.preserveAspect = true;

        Image portraitImage = CreateImage(
            host.transform, "PortraitImage", Vector2.zero, new Vector2(CircleSize * 0.7f, CircleSize * 0.7f));
        portraitImage.preserveAspect = true;
        portraitImage.raycastTarget = false;

        WaveCircle circle = host.AddComponent<WaveCircle>();

        SerializedObject so = new(circle);
        so.FindProperty("portraitImage").objectReferenceValue = portraitImage;
        so.ApplyModifiedProperties();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(host, WaveCirclePrefabPath);
        Object.DestroyImmediate(host);

        Debug.Log($"Next wave wiring: created {WaveCirclePrefabPath}.");

        return saved.GetComponent<WaveCircle>();
    }

    // ------------------------------------------------------------------------------------------
    // NextWavePanel - the strip itself
    // ------------------------------------------------------------------------------------------

    private static void WireNextWavePanel(Transform canvas, WaveCircle circlePrefab)
    {
        Transform existing = canvas.Find(NextWavePanelName);
        NextWavePanel panel;
        RectTransform row;

        if (existing != null)
        {
            panel = existing.GetComponent<NextWavePanel>();
            row = (RectTransform)existing.Find(RowName);
        }
        else
        {
            GameObject host = new(NextWavePanelName, typeof(RectTransform));
            host.transform.SetParent(canvas, false);
            host.layer = canvas.gameObject.layer;

            RectTransform hostRect = host.GetComponent<RectTransform>();
            hostRect.anchorMin = Vector2.zero;
            hostRect.anchorMax = Vector2.one;
            hostRect.offsetMin = Vector2.zero;
            hostRect.offsetMax = Vector2.zero;

            panel = host.AddComponent<NextWavePanel>();

            GameObject rowGo = new(RowName, typeof(RectTransform));
            rowGo.transform.SetParent(host.transform, false);
            rowGo.layer = host.layer;

            row = rowGo.GetComponent<RectTransform>();

            Debug.Log($"Next wave wiring: created {NextWavePanelName}.");
        }

        // Re-applied every run, existing row or not - a prior run's position is exactly what a re-run
        // after tuning the constants above is meant to fix, the same reasoning
        // PartyPortraitWiring.WirePartyPortraitPanel gives for its own Row.
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(0f, 1f);
        row.pivot = new Vector2(0f, 1f);
        row.anchoredPosition = RowAnchoredPosition;
        row.sizeDelta = Vector2.zero;

        SerializedObject so = new(panel);
        so.FindProperty("circlePrefab").objectReferenceValue = circlePrefab;
        so.FindProperty("row").objectReferenceValue = row;
        so.FindProperty("spacing").floatValue = CircleSpacing;
        so.ApplyModifiedProperties();

        Debug.Log($"Next wave wiring: {NextWavePanelName} fields set.");
    }

    // ------------------------------------------------------------------------------------------
    // Shared helpers - deliberately duplicated rather than shared with the other *Wiring scripts,
    // matching how PartyPortraitWiring documents that same choice for its own Centre/CreateImage pair.
    // ------------------------------------------------------------------------------------------

    private static Image CreateImage(Transform parent, string objectName, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject go = new(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rect = go.GetComponent<RectTransform>();
        Centre(rect);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        return go.GetComponent<Image>();
    }

    private static void Centre(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }
}
