using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot fix for tooltips drawing behind the reward screen: TooltipCanvas sat at Sorting Order -10
/// on the Overlay layer, below RewardCanvas (-1), NotificationCanvas (0) and the offered CardViewers
/// spawned by OfferedCard.Spawn (0, 1, 2... one per card). Everything else sharing that layer has grown
/// since TooltipManager's doc comment was written, which only promises the canvas beats the CardHover
/// *layer* - it says nothing about Order within Overlay itself.
///
/// A menu command rather than hand-edited scene YAML because the Editor holds Game.unity in memory
/// while it is open - see LevelRewardWiring's doc comment for the full reasoning.
///
/// Idempotent: only writes when the current value is not already high enough.
/// </summary>
public static class TooltipCanvasSortingFix
{
    private const string ScenePath = "Assets/Scenes/Game.unity";

    // Clear of every other Overlay-layer order in the scene (reward/removal cards run one per card in
    // an offer or deck, comfortably under this) so the tooltip always wins the tie within its layer.
    private const int TargetSortingOrder = 1000;

    [MenuItem("Tools/UI/Fix Tooltip Canvas Sorting Order")]
    public static void Fix()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Tooltip sorting fix: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenGameScene()) { return; }

        TooltipManager tooltipManager = Object.FindAnyObjectByType<TooltipManager>(FindObjectsInactive.Include);

        if (tooltipManager == null)
        {
            Debug.LogError($"Tooltip sorting fix: no TooltipManager in {ScenePath} - nothing to fix.");
            return;
        }

        SerializedObject managerSo = new(tooltipManager);
        SerializedProperty canvasProp = managerSo.FindProperty("canvas");

        if (canvasProp.objectReferenceValue is not Canvas canvas)
        {
            Debug.LogError("Tooltip sorting fix: TooltipManager has no canvas assigned - nothing to fix.");
            return;
        }

        SerializedObject canvasSo = new(canvas);
        SerializedProperty sortingOrder = canvasSo.FindProperty("m_SortingOrder");

        if (sortingOrder.intValue >= TargetSortingOrder)
        {
            Debug.Log("Tooltip sorting fix: already applied - nothing to do.");
            return;
        }

        int previous = sortingOrder.intValue;
        sortingOrder.intValue = TargetSortingOrder;
        canvasSo.ApplyModifiedProperties();
        EditorUtility.SetDirty(canvas);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log($"Tooltip sorting fix: {canvas.name} Sorting Order {previous} -> {TargetSortingOrder}. Scene saved.");
    }

    private static bool OpenGameScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);
        return true;
    }
}
