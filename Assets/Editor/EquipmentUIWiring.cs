using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-shot wiring for the equipment feature's UI: builds the EquipmentViewer prefab (a plain UGUI tile
/// - icon, name, description, click to choose), points RewardPanel at it, creates the EquipmentLibrary
/// asset and points LootManager at it, and creates+wires the "Upgrade a card" skip reward.
///
/// A menu command rather than hand-edited scene YAML or a hand-built prefab, for the same reason
/// LevelRewardWiring is one - see that class's header. Idempotent: every step checks whether it has
/// already been done, so re-running after tweaking one thing by hand will not duplicate objects or stomp
/// what you changed.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 without -IncludeEditor - that
/// script drives Assembly-CSharp.csproj, which never lists Assets/Editor. Verify by focusing the Editor
/// and checking the console, per CLAUDE.md.
/// </summary>
public static class EquipmentUIWiring
{
    private const string ScenePath = "Assets/Scenes/Game.unity";
    private const string PrefabPath = "Assets/Prefabs/UI/EquipmentViewer.prefab";
    private const string LibraryFolder = "Assets/Data/EquipmentLibrary";
    private const string LibraryPath = LibraryFolder + "/AllEquipment.asset";
    private const string SkipRewardFolder = "Assets/Data/SkipRewards";
    private const string UpgradeSkipRewardPath = SkipRewardFolder + "/UpgradeCard.asset";

    private const string EquipmentAnchorName = "EquipmentAnchor";

    [MenuItem("Tools/Equipment/Wire Equipment UI")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Equipment UI wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenGameScene()) { return; }

        EquipmentViewer prefab = EnsureEquipmentPrefab();
        EquipmentLibrary library = EnsureEquipmentLibrary();
        UpgradeCardSkipReward upgradeSkip = EnsureUpgradeSkipReward();

        RewardPanel rewardPanel = Object.FindAnyObjectByType<RewardPanel>(FindObjectsInactive.Include);

        if (rewardPanel == null)
        {
            Debug.LogError($"Equipment UI wiring: no RewardPanel in {ScenePath} - nothing to wire against.");
            return;
        }

        if (prefab != null) { WireRewardPanel(rewardPanel, prefab); }

        WireLootManager(library, upgradeSkip);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log("Equipment UI wiring: done - scene and assets saved.");
    }

    private static bool OpenGameScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);
        return true;
    }

    /// <summary>
    /// Builds a plain UGUI tile from scratch and saves it as a prefab: a background/button, an icon
    /// Image, and two TMP_Text labels. Not a world-space CardViewer clone - equipment has no board
    /// presence and no hand to sit in, so there is nothing to gain from sharing that machinery.
    /// </summary>
    private static EquipmentViewer EnsureEquipmentPrefab()
    {
        EquipmentViewer existing = AssetDatabase.LoadAssetAtPath<EquipmentViewer>(PrefabPath);
        if (existing != null) { return existing; }

        GameObject root = new("EquipmentViewer", typeof(RectTransform));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(300f, 420f);

        Image background = root.AddComponent<Image>();
        background.color = new Color(0.12f, 0.12f, 0.16f, 0.95f);

        Button button = root.AddComponent<Button>();
        button.targetGraphic = background;

        GameObject iconGo = new("Icon", typeof(RectTransform));
        iconGo.transform.SetParent(root.transform, false);
        RectTransform iconRect = iconGo.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 1f);
        iconRect.anchorMax = new Vector2(0.5f, 1f);
        iconRect.pivot = new Vector2(0.5f, 1f);
        iconRect.anchoredPosition = new Vector2(0f, -24f);
        iconRect.sizeDelta = new Vector2(160f, 160f);
        Image icon = iconGo.AddComponent<Image>();
        icon.preserveAspect = true;

        TextMeshProUGUI nameLabel =
            CreateLabel(root.transform, "NameLabel", new Vector2(0f, -204f), new Vector2(280f, 50f), 28f);

        TextMeshProUGUI descriptionLabel =
            CreateLabel(root.transform, "DescriptionLabel", new Vector2(0f, -262f), new Vector2(280f, 140f), 20f);
        descriptionLabel.textWrappingMode = TextWrappingModes.Normal;

        EquipmentViewer viewer = root.AddComponent<EquipmentViewer>();

        SerializedObject so = new(viewer);
        so.FindProperty("icon").objectReferenceValue = icon;
        so.FindProperty("nameLabel").objectReferenceValue = nameLabel;
        so.FindProperty("descriptionLabel").objectReferenceValue = descriptionLabel;
        so.FindProperty("button").objectReferenceValue = button;
        so.ApplyModifiedProperties();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
        Object.DestroyImmediate(root);

        if (!success)
        {
            Debug.LogError($"Equipment UI wiring: failed to save {PrefabPath}");
            return null;
        }

        Debug.Log($"Equipment UI wiring: created {PrefabPath}");

        return saved.GetComponent<EquipmentViewer>();
    }

    private static TextMeshProUGUI CreateLabel(
        Transform parent, string labelName, Vector2 anchoredPosition, Vector2 size, float fontSize)
    {
        GameObject go = new(labelName, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;

        // Match whatever font the rest of the UI already uses rather than leaving TMP's default -
        // same trick LevelRewardWiring.CreateTitle uses.
        TMP_Text sample = Object.FindAnyObjectByType<TMP_Text>(FindObjectsInactive.Include);

        if (sample != null && sample.font != null) { text.font = sample.font; }

        return text;
    }

    private static EquipmentLibrary EnsureEquipmentLibrary()
    {
        EquipmentLibrary existing = AssetDatabase.LoadAssetAtPath<EquipmentLibrary>(LibraryPath);
        if (existing != null) { return existing; }

        EnsureFolder(LibraryFolder);

        EquipmentLibrary library = ScriptableObject.CreateInstance<EquipmentLibrary>();
        AssetDatabase.CreateAsset(library, LibraryPath);

        Debug.Log($"Equipment UI wiring: created {LibraryPath}");

        return library;
    }

    private static UpgradeCardSkipReward EnsureUpgradeSkipReward()
    {
        UpgradeCardSkipReward existing = AssetDatabase.LoadAssetAtPath<UpgradeCardSkipReward>(UpgradeSkipRewardPath);
        if (existing != null) { return existing; }

        EnsureFolder(SkipRewardFolder);

        UpgradeCardSkipReward skip = ScriptableObject.CreateInstance<UpgradeCardSkipReward>();
        AssetDatabase.CreateAsset(skip, UpgradeSkipRewardPath);

        Debug.Log($"Equipment UI wiring: created {UpgradeSkipRewardPath}");

        return skip;
    }

    private static void WireRewardPanel(RewardPanel panel, EquipmentViewer prefab)
    {
        SerializedObject so = new(panel);

        SetIfEmpty(so, "equipmentPrefab", prefab);
        SetIfEmpty(so, "equipmentAnchor", EnsureEquipmentAnchor(panel));

        so.ApplyModifiedProperties();

        Debug.Log("Equipment UI wiring: RewardPanel equipmentPrefab + equipmentAnchor set.");
    }

    /// <summary>
    /// A RectTransform centred on the panel, parented under the panel itself so the equipment row
    /// disappears with everything else on Hide - CardViewer's cardAnchor cannot be reused here, since
    /// that one is deliberately a bare world-space position (see OfferedCard's header comment), never a
    /// UGUI parent, and EquipmentViewer is a UGUI element start to finish.
    /// </summary>
    private static Object EnsureEquipmentAnchor(RewardPanel panel)
    {
        Transform existing = panel.transform.Find(EquipmentAnchorName);
        if (existing != null) { return existing.GetComponent<RectTransform>(); }

        GameObject anchor = new(EquipmentAnchorName, typeof(RectTransform));
        anchor.transform.SetParent(panel.transform, false);
        anchor.layer = panel.gameObject.layer;

        RectTransform rect = anchor.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;

        return rect;
    }

    private static void WireLootManager(EquipmentLibrary library, UpgradeCardSkipReward upgradeSkip)
    {
        LootManager loot = Object.FindAnyObjectByType<LootManager>(FindObjectsInactive.Include);

        if (loot == null)
        {
            Debug.LogWarning("Equipment UI wiring: no LootManager in the scene - skipped.");
            return;
        }

        SerializedObject so = new(loot);

        SetIfEmpty(so, "equipmentLibrary", library);

        // Appended, not overwritten - levelClearSkipRewards may already hold [Skip, RemoveCard] from
        // LevelRewardWiring, and this should add to that list rather than replace it.
        SerializedProperty list = so.FindProperty("levelClearSkipRewards");
        bool alreadyPresent = false;

        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == upgradeSkip) { alreadyPresent = true; break; }
        }

        if (!alreadyPresent && upgradeSkip != null)
        {
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = upgradeSkip;
        }

        so.ApplyModifiedProperties();

        Debug.Log("Equipment UI wiring: LootManager equipmentLibrary set, UpgradeCard skip reward appended.");
    }

    private static void SetIfEmpty(SerializedObject so, string property, Object value)
    {
        SerializedProperty field = so.FindProperty(property);

        if (field.objectReferenceValue == null && value != null) { field.objectReferenceValue = value; }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) { return; }

        string parent = path.Substring(0, path.LastIndexOf('/'));
        string leaf = path.Substring(path.LastIndexOf('/') + 1);

        if (!AssetDatabase.IsValidFolder(parent)) { EnsureFolder(parent); }

        AssetDatabase.CreateFolder(parent, leaf);
    }
}
