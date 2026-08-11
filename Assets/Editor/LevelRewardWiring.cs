using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-shot wiring for the between-level reward feature: builds the CardRemovalPanel hierarchy in
/// Game.unity, fills in the fields RewardPanel and LootManager gained, and points each LevelData at a
/// clear-reward LootTable.
///
/// A menu command rather than hand-edited scene YAML because the Editor holds Game.unity in memory
/// while it is open - anything written to that file underneath it is discarded the next time the scene
/// is saved. Going through SerializedObject also means private [SerializeField] fields are set the
/// same way the Inspector sets them, dirty flags and undo included.
///
/// Idempotent: every step checks whether it has already been done, so re-running after tweaking one
/// thing by hand will not duplicate objects or stomp what you changed.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 - that script drives
/// Assembly-CSharp.csproj, which never lists Assets/Editor. Verify by focusing the Editor and checking
/// the console, per CLAUDE.md.
/// </summary>
public static class LevelRewardWiring
{
    private const string ScenePath = "Assets/Scenes/Game.unity";
    private const string ClearTablePath = "Assets/Data/LootTable/EarlyLevelReward.asset";
    private const string SkipRewardPath = "Assets/Data/SkipRewards/Skip.asset";
    private const string RemoveCardRewardPath = "Assets/Data/SkipRewards/RemoveCard.asset";

    private const string RemovalPanelName = "CardRemovalPanel";
    private const string RemovalBackdropName = "RemovalBackdrop";
    private const string RemovalTitleName = "RemovalTitle";
    private const string RemovalCancelName = "CancelButton";
    private const string RemovalAnchorName = "RemovalGridAnchor";
    private const string RewardTitleName = "RewardTitle";

    /// How many cards a level-clear offer presents. The whole point of the feature.
    private const int ClearRewardChoiceCount = 5;

    [MenuItem("Tools/Level Rewards/Wire Level Reward UI")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Level reward wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenGameScene()) { return; }

        WireLootTables();
        WireLevelData();

        RewardPanel rewardPanel = Object.FindAnyObjectByType<RewardPanel>(FindObjectsInactive.Include);

        if (rewardPanel == null)
        {
            Debug.LogError($"Level reward wiring: no RewardPanel in {ScenePath} - nothing to wire against.");
            return;
        }

        WireRewardPanel(rewardPanel);
        WireRemovalPanel(rewardPanel);
        WireLootManager();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log("Level reward wiring: done - scene and assets saved.");
    }

    private static bool OpenGameScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);
        return true;
    }

    /// The clear-reward table is a duplicate of the drop table, so it still carries the drop table's
    /// choiceCount of 3 - the one value that has to differ for a level clear to offer five cards.
    private static void WireLootTables()
    {
        LootTable table = AssetDatabase.LoadAssetAtPath<LootTable>(ClearTablePath);

        if (table == null)
        {
            Debug.LogWarning($"Level reward wiring: {ClearTablePath} not found - skipped.");
            return;
        }

        SerializedObject so = new(table);
        SerializedProperty choiceCount = so.FindProperty("choiceCount");

        if (choiceCount.intValue != ClearRewardChoiceCount)
        {
            choiceCount.intValue = ClearRewardChoiceCount;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(table);

            Debug.Log($"Level reward wiring: {table.name} choiceCount -> {ClearRewardChoiceCount}");
        }
    }

    /// <summary>
    /// Points every LevelData at the clear-reward table. All of them get the same one for now - split
    /// it per level by duplicating the asset and dragging the copy onto that level's Clear Reward Table
    /// field, which is the whole reason the field is per-level rather than on RunData.
    /// </summary>
    private static void WireLevelData()
    {
        LootTable table = AssetDatabase.LoadAssetAtPath<LootTable>(ClearTablePath);

        if (table == null) { return; }

        foreach (string guid in AssetDatabase.FindAssets("t:LevelData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(path);

            if (level == null) { continue; }

            SerializedObject so = new(level);
            SerializedProperty clearTable = so.FindProperty("clearRewardTable");

            if (clearTable.objectReferenceValue != null) { continue; }

            clearTable.objectReferenceValue = table;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(level);

            Debug.Log($"Level reward wiring: {level.name} clearRewardTable -> {table.name}");
        }
    }

    private static void WireRewardPanel(RewardPanel panel)
    {
        SerializedObject so = new(panel);
        SerializedProperty titleLabel = so.FindProperty("titleLabel");
        SerializedProperty maxWidth = so.FindProperty("maxWidth");
        SerializedProperty root = so.FindProperty("root");

        if (titleLabel.objectReferenceValue == null)
        {
            Transform parent = root.objectReferenceValue is GameObject backdrop
                ? backdrop.transform
                : panel.transform;

            titleLabel.objectReferenceValue = CreateTitle(parent, RewardTitleName, "Reward");
        }
        else if (titleLabel.objectReferenceValue is TMP_Text existingTitle
                 && root.objectReferenceValue is GameObject backdrop
                 && existingTitle.transform.parent != backdrop.transform)
        {
            // Title was authored as a sibling of Backdrop, not a child, so root.SetActive(false) in
            // RewardPanel.Hide() never hid it - the label just sat there with stale text after the
            // panel closed. Reparenting it here is what fixes that, not a runtime clear-text call.
            existingTitle.transform.SetParent(backdrop.transform, worldPositionStays: true);
            Debug.Log("Level reward wiring: reparented RewardPanel's title under its backdrop.");
        }

        // 0 is what this panel deserialized to - the field did not exist when the scene was authored -
        // and SpawnCards reads that as "no clamp". 15 is what actually keeps five cards on screen.
        if (Mathf.Approximately(maxWidth.floatValue, 0f)) { maxWidth.floatValue = 15f; }

        so.ApplyModifiedProperties();

        Debug.Log("Level reward wiring: RewardPanel titleLabel + maxWidth set.");
    }

    private static void WireRemovalPanel(RewardPanel rewardPanel)
    {
        CardRemovalPanel panel = Object.FindAnyObjectByType<CardRemovalPanel>(FindObjectsInactive.Include);

        if (panel == null)
        {
            // Under the same canvas as RewardPanel so it inherits the reward UI's scaler and camera -
            // a second canvas would have to be kept in step with CameraFrame by hand.
            GameObject host = new(RemovalPanelName, typeof(RectTransform));
            host.transform.SetParent(rewardPanel.transform.parent, false);
            host.layer = rewardPanel.gameObject.layer;

            Stretch(host.GetComponent<RectTransform>());

            panel = host.AddComponent<CardRemovalPanel>();

            Debug.Log($"Level reward wiring: created {RemovalPanelName}.");
        }

        SerializedObject so = new(panel);

        GameObject backdrop = EnsureBackdrop(panel.transform);

        SetIfEmpty(so, "root", backdrop);
        SetIfEmpty(so, "titleLabel", EnsureRemovalTitle(backdrop.transform));
        SetIfEmpty(so, "cardPrefab", ReadObject(rewardPanel, "cardPrefab"));
        SetIfEmpty(so, "gridAnchor", EnsureGridAnchor());
        SetIfEmpty(so, "cancelButton", EnsureCancelButton(backdrop.transform, rewardPanel));

        // Authored as 0.78 - CardViewer.OnMouseEnter computes rest * hover, and 0.7 * 0.78 is smaller
        // than 0.7, so hovering a card in the deck-thinning grid shrank it instead of popping it up.
        // Force-corrected rather than SetIfEmpty above: the field already holds a wrong value, not an
        // empty one.
        SerializedProperty cardHoverScale = so.FindProperty("cardHoverScale");

        if (Mathf.Approximately(cardHoverScale.floatValue, 0.78f))
        {
            cardHoverScale.floatValue = 1.08f;
            Debug.Log("Level reward wiring: CardRemovalPanel cardHoverScale 0.78 -> 1.08 (was shrinking on hover).");
        }

        so.ApplyModifiedProperties();

        Debug.Log("Level reward wiring: CardRemovalPanel fields set.");
    }

    /// <summary>
    /// The dim sheet the grid sits on, and the only thing Show/Hide toggles. Deliberately a child, not
    /// the component's own GameObject: CardRemovalPanel is a Singleton, so it has to stay active for
    /// Awake to claim Instance - RemoveCardSkipReward reads that static and would find nothing if the
    /// whole object were switched off between uses. Same split RewardPanel already uses.
    /// </summary>
    private static GameObject EnsureBackdrop(Transform parent)
    {
        Transform existing = parent.Find(RemovalBackdropName);

        if (existing != null) { return existing.gameObject; }

        GameObject backdrop = new(RemovalBackdropName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        backdrop.transform.SetParent(parent, false);
        backdrop.layer = parent.gameObject.layer;

        Stretch(backdrop.GetComponent<RectTransform>());
        backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

        // Awake forces this off anyway; doing it here too keeps the Editor view uncluttered.
        backdrop.SetActive(false);

        return backdrop;
    }

    private static Object EnsureRemovalTitle(Transform parent)
    {
        Transform existing = parent.Find(RemovalTitleName);

        if (existing != null) { return existing.GetComponent<TMP_Text>(); }

        return CreateTitle(parent, RemovalTitleName, "Choose a card to remove");
    }

    private static TMP_Text CreateTitle(Transform parent, string name, string placeholder)
    {
        Transform existing = parent.Find(name);

        if (existing != null) { return existing.GetComponent<TMP_Text>(); }

        GameObject go = new(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -60f);
        rect.sizeDelta = new Vector2(1200f, 90f);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = placeholder;
        text.fontSize = 56f;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;

        // Match whatever font the rest of the UI already uses rather than leaving TMP's default.
        TMP_Text sample = Object.FindAnyObjectByType<TMP_Text>(FindObjectsInactive.Include);

        if (sample != null && sample.font != null) { text.font = sample.font; }

        return text;
    }

    /// <summary>
    /// A root-level plain Transform, not a canvas child - CardViewers are world-space sprite objects,
    /// and gridAnchor.position is read as a world position.
    ///
    /// Position always re-applied, not just set on creation: the camera sits at world (0.81334, 5.4,
    /// -1), and CameraFrame's fixed 10.8-tall design frame means the visible vertical range is y in
    /// [0, 10.8] - not [-5.4, 5.4], which "camera size 5.4" suggests on its own. The old y (0.5) sat
    /// almost on the frame's bottom edge, so every row past the first fell off-screen; 5.4 is the
    /// frame's true vertical centre, giving the grid the most symmetric room to grow in both
    /// directions. x stays 0, matching RewardCardAnchor's own world x once its parent offset is
    /// worked through - that is this design's centre line, not the camera's own x.
    /// </summary>
    private static Object EnsureGridAnchor()
    {
        GameObject anchor = GameObject.Find(RemovalAnchorName);

        if (anchor == null) { anchor = new GameObject(RemovalAnchorName); }

        anchor.transform.position = new Vector3(0f, 5.4f, -0.67f);

        return anchor.transform;
    }

    /// Reuses RewardPanel's own skip button prefab so Cancel matches the buttons beside it.
    private static Object EnsureCancelButton(Transform parent, RewardPanel rewardPanel)
    {
        Transform existing = parent.Find(RemovalCancelName);

        if (existing != null) { return existing.GetComponent<Button>(); }

        if (ReadObject(rewardPanel, "skipButtonPrefab") is not Button prefab)
        {
            Debug.LogWarning("Level reward wiring: RewardPanel has no skipButtonPrefab - Cancel button "
                             + "not created, assign one by hand.");
            return null;
        }

        Button button = (Button)PrefabUtility.InstantiatePrefab(prefab, parent);
        button.name = RemovalCancelName;

        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 120f);

        TMP_Text label = button.GetComponentInChildren<TMP_Text>();

        if (label != null) { label.text = "Cancel"; }

        return button;
    }

    private static void WireLootManager()
    {
        LootManager loot = Object.FindAnyObjectByType<LootManager>(FindObjectsInactive.Include);

        if (loot == null)
        {
            Debug.LogWarning("Level reward wiring: no LootManager in the scene - skipped.");
            return;
        }

        SerializedObject so = new(loot);
        SerializedProperty list = so.FindProperty("levelClearSkipRewards");

        if (list.arraySize > 0) { return; }

        SkipReward skip = AssetDatabase.LoadAssetAtPath<SkipReward>(SkipRewardPath);
        SkipReward remove = AssetDatabase.LoadAssetAtPath<SkipReward>(RemoveCardRewardPath);

        if (skip == null || remove == null)
        {
            Debug.LogWarning("Level reward wiring: Skip/RemoveCard SkipReward assets not found - "
                             + "levelClearSkipRewards left empty.");
            return;
        }

        list.arraySize = 2;
        list.GetArrayElementAtIndex(0).objectReferenceValue = skip;
        list.GetArrayElementAtIndex(1).objectReferenceValue = remove;

        so.ApplyModifiedProperties();

        Debug.Log("Level reward wiring: LootManager levelClearSkipRewards -> [Skip, RemoveCard].");
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
