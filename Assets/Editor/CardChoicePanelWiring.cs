using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot scene wiring for CardChoicePanel (DiscardAction's picker - see that class). Duplicates the
/// CardRemovalPanel GameObject already sitting in the loaded scene rather than hand-building a new UI
/// hierarchy, so the backdrop, title and grid layout match every other card screen for free.
///
/// A menu command rather than hand-edited .unity YAML: Temp/UnityLockfile means the Editor has this
/// project open, and writing scene YAML underneath a live Editor session is how a session's unsaved
/// state gets silently clobbered on its next autosave or domain reload - see CLAUDE.md's "Unity Editor
/// open blocks scene edits". Operating on the *loaded* scene through EditorSceneManager is the
/// sanctioned path; it is exactly what the user's own Ctrl+S would do, just triggered from code.
///
/// GameObject.Instantiate remaps a cloned hierarchy's internal object references to point at the new
/// copies rather than the originals - the same behaviour that keeps a Ctrl+D duplicate's own scripts
/// wired to its own children instead of the object it was copied from. That is what lets `root` and
/// `titleLabel` come back already pointing at the clone with no manual re-parenting here. The grid
/// anchor does not get this treatment: RemovalGridAnchor is a scene-root sibling of CardRemovalPanel,
/// not a descendant, so it is cloned in its own separate Instantiate call and wired in by hand.
/// </summary>
public static class CardChoicePanelWiring
{
    [MenuItem("Tools/UI/Wire Card Choice Panel")]
    public static void Wire()
    {
        CardRemovalPanel original = Object.FindAnyObjectByType<CardRemovalPanel>(FindObjectsInactive.Include);
        if (original == null)
        {
            Debug.LogError("Card sheet: no CardRemovalPanel found in the loaded scene - open Game.unity first.");
            return;
        }

        if (Object.FindAnyObjectByType<CardChoicePanel>(FindObjectsInactive.Include) != null)
        {
            Debug.Log("Card sheet: CardChoicePanel is already wired in this scene - skipping.");
            return;
        }

        SerializedObject originalSO = new(original);
        Transform originalGridAnchor = originalSO.FindProperty("gridAnchor").objectReferenceValue as Transform;
        CardViewer cardPrefab = originalSO.FindProperty("cardPrefab").objectReferenceValue as CardViewer;
        int columns = originalSO.FindProperty("columns").intValue;
        float cellWidth = originalSO.FindProperty("cellWidth").floatValue;
        float cellHeight = originalSO.FindProperty("cellHeight").floatValue;
        float maxGridHeight = originalSO.FindProperty("maxGridHeight").floatValue;
        float cardScale = originalSO.FindProperty("cardScale").floatValue;
        float cardHoverScale = originalSO.FindProperty("cardHoverScale").floatValue;

        // The whole backdrop/title hierarchy, cloned in one call so their cross-references land on
        // each other rather than on the original panel - see the class doc comment.
        GameObject panelClone = Object.Instantiate(original.gameObject, original.transform.parent);
        panelClone.name = "CardChoicePanel";

        // A scene-root sibling, not a descendant of CardRemovalPanel, so it needs its own Instantiate
        // call and its own explicit wiring below.
        Transform anchorClone = null;
        if (originalGridAnchor != null)
        {
            GameObject anchorObject = Object.Instantiate(originalGridAnchor.gameObject);
            anchorObject.name = "ChoiceGridAnchor";
            anchorClone = anchorObject.transform;
        }
        else
        {
            Debug.LogWarning("Card sheet: CardRemovalPanel has no gridAnchor set - CardChoicePanel will "
                              + "need one assigned by hand in the Inspector.");
        }

        CardRemovalPanel clonedOld = panelClone.GetComponent<CardRemovalPanel>();
        SerializedObject clonedOldSO = new(clonedOld);

        GameObject clonedRoot = clonedOldSO.FindProperty("root").objectReferenceValue as GameObject;
        TMP_Text clonedTitle = clonedOldSO.FindProperty("titleLabel").objectReferenceValue as TMP_Text;
        Button clonedCancelButton = clonedOldSO.FindProperty("cancelButton").objectReferenceValue as Button;

        if (clonedRoot == null || clonedTitle == null)
        {
            Debug.LogWarning("Card sheet: root/titleLabel did not remap onto the clone as expected - "
                              + "check CardChoicePanel's Inspector fields by hand after this runs.");
        }

        // CardChoicePanel never offers a way to back out - DiscardAction is the cost half of a card
        // already committed to, so there is nothing to cancel. See CardChoicePanel's own doc comment.
        if (clonedCancelButton != null) { Object.DestroyImmediate(clonedCancelButton.gameObject); }

        Object.DestroyImmediate(clonedOld);

        CardChoicePanel newPanel = panelClone.AddComponent<CardChoicePanel>();
        SerializedObject newSO = new(newPanel);
        newSO.FindProperty("root").objectReferenceValue = clonedRoot;
        newSO.FindProperty("titleLabel").objectReferenceValue = clonedTitle;
        newSO.FindProperty("cardPrefab").objectReferenceValue = cardPrefab;
        newSO.FindProperty("gridAnchor").objectReferenceValue = anchorClone;
        newSO.FindProperty("columns").intValue = columns;
        newSO.FindProperty("cellWidth").floatValue = cellWidth;
        newSO.FindProperty("cellHeight").floatValue = cellHeight;
        newSO.FindProperty("maxGridHeight").floatValue = maxGridHeight;
        newSO.FindProperty("cardScale").floatValue = cardScale;
        newSO.FindProperty("cardHoverScale").floatValue = cardHoverScale;
        newSO.ApplyModifiedProperties();

        EditorUtility.SetDirty(panelClone);
        EditorSceneManager.MarkSceneDirty(panelClone.scene);

        Debug.Log("Card sheet: wired CardChoicePanel - save the scene (Ctrl+S) to keep it.");
    }
}
