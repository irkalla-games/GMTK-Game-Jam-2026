using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-shot wiring for the Deck/Discard pile feature: two count buttons on the main HUD canvas, the
/// CardPileHud component that drives them, the read-only CardPilePanel browse screen (CardRemovalPanel's
/// sibling), and pointing ActiveHandViewer's drawAnchor/discardAnchor at the two new buttons so a drawn
/// card visibly grows out of the deck icon and a discarded one flies to the discard icon.
///
/// A menu command rather than hand-edited scene YAML because the Editor holds Game.unity in memory while
/// it is open - anything written to that file underneath it is discarded the next time the scene is
/// saved. Going through SerializedObject also means private [SerializeField] fields are set the same way
/// the Inspector sets them, dirty flags and undo included. Same shape as BattleHudWiring and
/// LevelRewardWiring.
///
/// Idempotent: every object is found by name (or by component type for the singletons) before being
/// created, so a re-run re-applies layout and wiring rather than duplicating anything.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 - that script drives
/// Assembly-CSharp.csproj, which never lists Assets/Editor. Verify with the -IncludeEditor switch, or by
/// focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class CardPileWiring
{
    private const string ScenePath = "Assets/Scenes/Game.unity";
    private const string CardPrefabPath = "Assets/Prefabs/UI/CardPrefab.prefab";
    private const string SkipButtonPrefabPath = "Assets/Prefabs/UI/SkipButtonPrefab.prefab";

    private const string CanvasName = "Canvas";
    private const string DeckButtonName = "DeckPileButton";
    private const string DiscardButtonName = "DiscardPileButton";
    private const string CountLabelName = "Count";

    private const string PilePanelName = "CardPilePanel";
    private const string PileBackdropName = "PileBackdrop";
    private const string PileTitleName = "PileTitle";
    private const string PileCloseName = "PileCloseButton";
    private const string PileGridAnchorName = "PileGridAnchor";

    // The removal grid's own anchor - CardRemovalPanel and CardPilePanel never show at once (opening
    // either sets BattleManager.InputLocked, and the other refuses to open while that is true), so
    // sharing the same world position is safe and keeps every card grid centred identically.
    private static readonly Vector3 GridAnchorPosition = new(0f, 5.4f, -0.67f);

    private static readonly Vector2 BottomLeft = Vector2.zero;
    private static readonly Vector2 BottomRight = new(1f, 0f);

    [MenuItem("Tools/Battle HUD/Wire Card Piles")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Card pile wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenGameScene()) { return; }

        ActiveHandViewer handViewer = Object.FindAnyObjectByType<ActiveHandViewer>(FindObjectsInactive.Include);

        if (handViewer == null)
        {
            Debug.LogError($"Card pile wiring: no ActiveHandViewer in {ScenePath} - nothing to wire against.");
            return;
        }

        GameObject canvas = GameObject.Find(CanvasName);

        if (canvas == null)
        {
            Debug.LogError($"Card pile wiring: no {CanvasName} in {ScenePath} - the pile buttons need a parent.");
            return;
        }

        Sprite cardBack = LoadCardBackSprite();

        Button deckButton = EnsurePileButton(canvas.transform, DeckButtonName, BottomLeft, new Vector2(520f, 66f), cardBack);
        Button discardButton = EnsurePileButton(canvas.transform, DiscardButtonName, BottomRight, new Vector2(-150f, 66f), cardBack);

        WireCardPileHud(canvas, deckButton, discardButton, cardBack);

        CardRemovalPanel removalPanel = Object.FindAnyObjectByType<CardRemovalPanel>(FindObjectsInactive.Include);

        if (removalPanel == null)
        {
            Debug.LogError($"Card pile wiring: no CardRemovalPanel in {ScenePath} - run Level Reward wiring first.");
            return;
        }

        WirePilePanel(removalPanel);

        SerializedObject handSo = new(handViewer);
        handSo.FindProperty("discardAnchor").objectReferenceValue = discardButton.transform;
        handSo.FindProperty("drawAnchor").objectReferenceValue = deckButton.transform;
        handSo.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log("Card pile wiring: done - scene and assets saved.");
    }

    private static bool OpenGameScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);
        return true;
    }

    // ------------------------------------------------------------------------------------------
    // The two HUD buttons
    // ------------------------------------------------------------------------------------------

    /// Built from SkipButtonPrefab (a plain Image+Button with a TMP child) rather than from scratch, so
    /// it inherits the rest of the HUD's button look. Resized to a square icon and re-tinted with the
    /// card-back placeholder rather than left as a text button.
    private static Button EnsurePileButton(Transform canvas, string name, Vector2 anchor, Vector2 anchoredPosition, Sprite icon)
    {
        Transform existing = canvas.Find(name);

        if (existing != null) { return existing.GetComponent<Button>(); }

        Button prefab = AssetDatabase.LoadAssetAtPath<Button>(SkipButtonPrefabPath);

        if (prefab == null)
        {
            Debug.LogWarning($"Card pile wiring: {SkipButtonPrefabPath} not found - {name} not created.");
            return null;
        }

        Button button = (Button)PrefabUtility.InstantiatePrefab(prefab, canvas);
        button.name = name;

        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(96f, 96f);

        Image image = button.GetComponent<Image>();

        if (image != null && icon != null)
        {
            image.sprite = icon;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        }

        TMP_Text label = button.GetComponentInChildren<TMP_Text>();

        if (label != null)
        {
            label.name = CountLabelName;
            label.text = "0";
            label.fontSize *= 1.4f;
        }

        Debug.Log($"Card pile wiring: created {name}.");

        return button;
    }

    private static void WireCardPileHud(GameObject canvas, Button deckButton, Button discardButton, Sprite cardBack)
    {
        CardPileHud hud = Object.FindAnyObjectByType<CardPileHud>(FindObjectsInactive.Include);
        bool created = hud == null;

        // A manager component, not a visual object - lives on the Canvas itself rather than a bespoke
        // child, the same way CardPlayManager and other Singletons sit on a plain scene object.
        if (created) { hud = canvas.AddComponent<CardPileHud>(); }

        SerializedObject so = new(hud);
        so.FindProperty("deckButton").objectReferenceValue = deckButton;
        so.FindProperty("deckCountLabel").objectReferenceValue = deckButton != null ? deckButton.GetComponentInChildren<TMP_Text>() : null;
        so.FindProperty("discardButton").objectReferenceValue = discardButton;
        so.FindProperty("discardCountLabel").objectReferenceValue = discardButton != null ? discardButton.GetComponentInChildren<TMP_Text>() : null;

        if (cardBack != null) { so.FindProperty("chipSprite").objectReferenceValue = cardBack; }

        so.ApplyModifiedProperties();

        Debug.Log(created ? "Card pile wiring: created CardPileHud." : "Card pile wiring: CardPileHud fields refreshed.");
    }

    // ------------------------------------------------------------------------------------------
    // CardPilePanel - CardRemovalPanel's read-only sibling
    // ------------------------------------------------------------------------------------------

    private static void WirePilePanel(CardRemovalPanel removalPanel)
    {
        CardPilePanel panel = Object.FindAnyObjectByType<CardPilePanel>(FindObjectsInactive.Include);

        if (panel == null)
        {
            // Under the same canvas as CardRemovalPanel so it inherits the reward UI's scaler and
            // camera - a second canvas would have to be kept in step with CameraFrame by hand.
            GameObject host = new(PilePanelName, typeof(RectTransform));
            host.transform.SetParent(removalPanel.transform.parent, false);
            host.layer = removalPanel.gameObject.layer;

            Stretch(host.GetComponent<RectTransform>());

            panel = host.AddComponent<CardPilePanel>();

            Debug.Log($"Card pile wiring: created {PilePanelName}.");
        }

        SerializedObject so = new(panel);

        GameObject backdrop = EnsureBackdrop(panel.transform);

        SetIfEmpty(so, "root", backdrop);
        SetIfEmpty(so, "titleLabel", EnsureTitle(backdrop.transform));
        SetIfEmpty(so, "cardPrefab", ReadObject(removalPanel, "cardPrefab"));
        SetIfEmpty(so, "gridAnchor", EnsureGridAnchor());
        SetIfEmpty(so, "closeButton", EnsureCloseButton(backdrop.transform));

        so.ApplyModifiedProperties();

        Debug.Log("Card pile wiring: CardPilePanel fields set.");
    }

    /// Same bargain CardRemovalPanel's own backdrop strikes - a child, not the component's own
    /// GameObject, because CardPilePanel is a Singleton and has to stay active for Awake to claim
    /// Instance.
    private static GameObject EnsureBackdrop(Transform parent)
    {
        Transform existing = parent.Find(PileBackdropName);

        if (existing != null) { return existing.gameObject; }

        GameObject backdrop = new(PileBackdropName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        backdrop.transform.SetParent(parent, false);
        backdrop.layer = parent.gameObject.layer;

        Stretch(backdrop.GetComponent<RectTransform>());
        backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

        // Awake forces this off anyway; doing it here too keeps the Editor view uncluttered.
        backdrop.SetActive(false);

        return backdrop;
    }

    private static Object EnsureTitle(Transform parent)
    {
        Transform existing = parent.Find(PileTitleName);

        if (existing != null) { return existing.GetComponent<TMP_Text>(); }

        GameObject go = new(PileTitleName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -60f);
        rect.sizeDelta = new Vector2(1200f, 90f);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = "Draw pile";
        text.fontSize = 56f;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;

        // Match whatever font the rest of the UI already uses rather than leaving TMP's default.
        TMP_Text sample = Object.FindAnyObjectByType<TMP_Text>(FindObjectsInactive.Include);

        if (sample != null && sample.font != null) { text.font = sample.font; }

        return text;
    }

    /// A root-level plain Transform, not a canvas child - CardViewers are world-space sprite objects,
    /// and gridAnchor.position is read as a world position. Same position as RemovalGridAnchor - see
    /// GridAnchorPosition.
    private static Object EnsureGridAnchor()
    {
        GameObject anchor = GameObject.Find(PileGridAnchorName);

        if (anchor == null) { anchor = new GameObject(PileGridAnchorName); }

        anchor.transform.position = GridAnchorPosition;

        return anchor.transform;
    }

    /// Reuses the same skip button prefab CardRemovalPanel's own Cancel button is built from, so Close
    /// matches the rest of the reward UI's chrome.
    private static Object EnsureCloseButton(Transform parent)
    {
        Transform existing = parent.Find(PileCloseName);

        if (existing != null) { return existing.GetComponent<Button>(); }

        Button prefab = AssetDatabase.LoadAssetAtPath<Button>(SkipButtonPrefabPath);

        if (prefab == null)
        {
            Debug.LogWarning($"Card pile wiring: {SkipButtonPrefabPath} not found - Close button not "
                             + "created, assign one by hand.");
            return null;
        }

        Button button = (Button)PrefabUtility.InstantiatePrefab(prefab, parent);
        button.name = PileCloseName;

        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 120f);

        TMP_Text label = button.GetComponentInChildren<TMP_Text>();

        if (label != null) { label.text = "Close"; }

        return button;
    }

    // ------------------------------------------------------------------------------------------
    // Shared helpers
    // ------------------------------------------------------------------------------------------

    private static Sprite LoadCardBackSprite()
    {
        GameObject cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefabPath);

        if (cardPrefab == null)
        {
            Debug.LogWarning($"Card pile wiring: {CardPrefabPath} not found - pile icons left without art.");
            return null;
        }

        Transform background = cardPrefab.transform.Find("CardBackground");
        SpriteRenderer renderer = background != null ? background.GetComponent<SpriteRenderer>() : null;

        if (renderer == null || renderer.sprite == null)
        {
            Debug.LogWarning("Card pile wiring: CardPrefab has no CardBackground sprite - pile icons "
                             + "left without art.");
            return null;
        }

        return renderer.sprite;
    }

    private static Object ReadObject(Object owner, string property)
    {
        return new SerializedObject(owner).FindProperty(property).objectReferenceValue;
    }

    /// Assigns only an unset field, so a value tuned by hand survives a re-run.
    private static void SetIfEmpty(SerializedObject so, string property, Object value)
    {
        SerializedProperty field = so.FindProperty(property);

        if (field.objectReferenceValue == null && value != null) { field.objectReferenceValue = value; }
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
