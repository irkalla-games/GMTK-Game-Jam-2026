using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Repositions the character-select screen's slot row and shrinks CharacterSelectSlot.prefab's portrait
/// to 30% of its authored size (280 -> 84). The portrait shrink opens up ~98 units of dead space above
/// each slot's now-smaller portrait; pushing SlotParent from y -20 to y -170 does not close that gap
/// (NameLabel/DeckLabel/the arrow buttons all stay exactly where they were relative to the slot, so the
/// shrunken portrait still floats above them with the same relative spacing) but does put the whole
/// composition back where a shrunken portrait actually reads well on screen - the -170 value was chosen
/// specifically because it lands the new, smaller portrait dead centre of the 1080-tall canvas rather
/// than in the upper-middle band it needed when it was 3.3x larger. See the plan this was authored from.
///
/// A menu command rather than hand-edited scene YAML or a change to CharacterSelectWiring.EnsureSlotParent
/// itself, for the same two reasons HeroPortraitResizeWiring is its own command rather than a change to
/// PartyPortraitWiring: the Editor holds MainMenu.unity in memory while it is open, so anything written to
/// that file underneath it is discarded on the next scene save - and EnsureSlotParent (like
/// EnsureHeroPortraitPrefab) early-returns once the object already exists, which answers "does this exist
/// at all" and not "does this match the current spec."
///
/// Idempotent: every value is re-applied unconditionally on every run.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class CharacterSelectResizeWiring
{
    private const string ScenePath = "Assets/Scenes/MainMenu.unity";
    private const string SlotPrefabPath = "Assets/Prefabs/UI/CharacterSelectSlot.prefab";
    private const string PortraitChildName = "Portrait";

    private static readonly Vector2 SlotParentAnchoredPosition = new(0f, -170f);
    private static readonly Vector2 PortraitSizeDelta = new(84f, 84f);

    [MenuItem("Tools/Main Menu/Resize Character Select")]
    public static void Resize()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Character select resize: exit Play Mode first - scene/prefab edits made in play do not persist.");
            return;
        }

        if (!OpenMenuScene()) { return; }

        if (!MoveSlotParent())
        {
            Debug.LogError("Character select resize: no CharacterSelectPanel.slotParent found - run Tools > "
                + "Main Menu > Wire Character Select first.");
            return;
        }

        if (!ResizePortrait())
        {
            Debug.LogError($"Character select resize: {SlotPrefabPath} not found - run Tools > Main Menu > "
                + "Wire Character Select first to create it.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log("Character select resize: done - scene and assets saved.");
    }

    private static bool OpenMenuScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);
        return true;
    }

    private static bool MoveSlotParent()
    {
        CharacterSelectPanel panel = Object.FindAnyObjectByType<CharacterSelectPanel>(FindObjectsInactive.Include);

        if (panel == null) { return false; }

        SerializedObject so = new(panel);
        Transform slotParent = so.FindProperty("slotParent").objectReferenceValue as Transform;

        if (slotParent == null) { return false; }

        ((RectTransform)slotParent).anchoredPosition = SlotParentAnchoredPosition;

        return true;
    }

    private static bool ResizePortrait()
    {
        CharacterSelectSlot existing = AssetDatabase.LoadAssetAtPath<CharacterSelectSlot>(SlotPrefabPath);

        if (existing == null) { return false; }

        GameObject root = PrefabUtility.LoadPrefabContents(SlotPrefabPath);
        Transform portrait = root.transform.Find(PortraitChildName);

        if (portrait == null)
        {
            Debug.LogError($"Character select resize: {PortraitChildName} not found under {SlotPrefabPath} - skipping it.");
            PrefabUtility.UnloadPrefabContents(root);
            return true;
        }

        ((RectTransform)portrait).sizeDelta = PortraitSizeDelta;

        PrefabUtility.SaveAsPrefabAsset(root, SlotPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        return true;
    }
}
