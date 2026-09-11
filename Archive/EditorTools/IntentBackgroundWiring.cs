using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds a semi-transparent black IntentBg behind each brain-driven character's overhead intent icon -
/// the two enemies and the Skeleton Ally (an ally can be brain-driven too; SkeletonWarrior, EnemyRanger
/// and SkeletonAlly all share the same IntentIcons asset guid, confirming all three commit and
/// telegraph intent the same way).
///
/// IntentRoll.Build (Assets/Scripts/Character/IntentRoll.cs) reads the authored IntentIcon's parent and
/// sibling index up front, inserts a new IntentWindow at that same slot, and only afterward reparents
/// the original IntentIcon GameObject itself out of Overhead and into the window - so a new sibling
/// inserted immediately before IntentIcon, at its own current sibling index, is never touched by Build
/// (which only knows about the one Image reference it's handed) and ends up sitting directly behind the
/// reparented IntentWindow once Awake runs.
///
/// Idempotent: skips a prefab that already has an IntentBg sibling.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class IntentBackgroundWiring
{
    private const string BgName = "IntentBg";

    private static readonly string[] TargetPrefabPaths =
    {
        "Assets/Prefabs/Enemies/SkeletonWarrior.prefab",
        "Assets/Prefabs/Enemies/EnemyRanger.prefab",
        "Assets/Prefabs/Allies/SkeletonAlly.prefab",
    };

    [MenuItem("Tools/Battle HUD/Wire Intent Backgrounds")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Intent background wiring: exit Play Mode first - prefab edits made in play do not persist.");
            return;
        }

        int wired = 0;

        foreach (string path in TargetPrefabPaths)
        {
            if (WireOne(path)) { wired++; }
        }

        AssetDatabase.SaveAssets();

        Debug.Log($"Intent background wiring: done - {wired}/{TargetPrefabPaths.Length} prefab(s) wired.");
    }

    private static bool WireOne(string prefabPath)
    {
        GameObject existingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        if (existingAsset == null)
        {
            Debug.LogError($"Intent background wiring: {prefabPath} not found - skipping.");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        CharacterOverheadViewer viewer = root.GetComponentInChildren<CharacterOverheadViewer>(true);

        if (viewer == null)
        {
            Debug.LogError($"Intent background wiring: {prefabPath} has no CharacterOverheadViewer - skipping.");
            PrefabUtility.UnloadPrefabContents(root);
            return false;
        }

        SerializedObject so = new(viewer);
        Image intentIcon = so.FindProperty("intentIcon").objectReferenceValue as Image;

        if (intentIcon == null)
        {
            Debug.LogWarning($"Intent background wiring: {prefabPath}'s intentIcon is empty - nothing to back. Skipping.");
            PrefabUtility.UnloadPrefabContents(root);
            return false;
        }

        Transform overhead = intentIcon.transform.parent;

        if (overhead.Find(BgName) != null)
        {
            PrefabUtility.UnloadPrefabContents(root);
            return true;
        }

        RectTransform iconRect = intentIcon.rectTransform;
        int siblingIndex = iconRect.GetSiblingIndex();

        GameObject bgGo = new(BgName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bgGo.transform.SetParent(overhead, false);
        bgGo.layer = overhead.gameObject.layer;
        bgGo.transform.SetSiblingIndex(siblingIndex);

        RectTransform bgRect = bgGo.GetComponent<RectTransform>();
        bgRect.anchorMin = iconRect.anchorMin;
        bgRect.anchorMax = iconRect.anchorMax;
        bgRect.pivot = iconRect.pivot;
        bgRect.anchoredPosition = iconRect.anchoredPosition;
        bgRect.sizeDelta = iconRect.sizeDelta;

        Image bgImage = bgGo.GetComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.6f);
        bgImage.raycastTarget = false;

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        return true;
    }
}
