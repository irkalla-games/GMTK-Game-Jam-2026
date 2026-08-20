using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Splits the battle scene into two cameras and puts the board on its own layer.
///
/// ## Why two cameras
///
/// The board has to resize to fit whatever size the level asked for; the cards, hand and HUD must not
/// resize at all. Those cards are world-space SpriteRenderers driven by OnMouseDown - not UGUI - so a
/// single camera pulling back to show a 10x10 board shrinks the hand along with it, and every
/// world-space HUD element would need its own counter-scale kept in step forever.
///
/// A URP camera stack removes the problem instead of managing it:
///
///     BoardCamera   Base      renders the Board layer only, zooms and pans freely
///     UiCamera      Overlay   renders everything else, fixed at the 19.2 x 10.8 design frame
///
/// The UI camera is the *existing* Main Camera, kept exactly where it is with its CameraFrame intact,
/// so nothing it draws moves by a pixel. The board camera is new. An Overlay camera clears depth but
/// not colour, which is why the board camera owns the background colour now.
///
/// ## Run order
///
///     1 - Import Block Sprites     the art the floor is built from
///     2 - Wire Cameras             this: layers, the two cameras, the canvases
///     3 - Wire Board Objects       the board root, prefab layers, per-body SortingGroups
///
/// Idempotent throughout, and repair-not-skip: every field these tools own is re-written on every run,
/// so a half-wired scene is fixed by running them again rather than by hand. Scene changes are marked
/// dirty but **not saved** - look at the result, then Ctrl+S.
/// </summary>
public static class BoardCameraWiring
{
    /// Read from the runtime constant rather than spelled again here. GameLayers.PutOnBoard resolves
    /// the same name at runtime for the objects that have no board parent to inherit from, and two
    /// spellings of a layer name is a bug that only shows up as art drawn by the wrong camera.
    private const string BoardLayerName = GameLayers.Board;

    private const string BoardRootName = "---BOARD";
    private const string UiCameraName = "UiCamera";
    private const string BoardCameraName = "BoardCamera";
    private const string SceneCamerasName = "SceneCameras";

    /// Prefabs whose whole hierarchy belongs to the board camera. Anything drawn in world space that
    /// is part of the board rather than part of the UI has to be here, or the board camera will not
    /// render it at all.
    private static readonly string[] BoardPrefabFolders =
    {
        "Assets/Prefabs/Player",
        "Assets/Prefabs/Enemies",
        "Assets/Prefabs/Allies",
        "Assets/Prefabs/Totem",
        "Assets/Prefabs/ItemDrops",
    };

    /// Single-sprite bodies that never had a SortingGroup, unlike the heroes and totems. They need one
    /// to be depth-sorted against the rest of the board - see Character.RefreshSortingDepth.
    private static readonly string[] NeedSortingGroup =
    {
        "Assets/Prefabs/Enemies/EnemyRanger.prefab",
        "Assets/Prefabs/Enemies/SkeletonWarrior.prefab",
        "Assets/Prefabs/Allies/SkeletonAlly.prefab",
    };

    // ---------------------------------------------------------------------------------------------
    // 2 - Wire Cameras
    // ---------------------------------------------------------------------------------------------

    [MenuItem("Tools/Board/2 - Wire Cameras")]
    static void WireCameras()
    {
        int boardLayer = EnsureBoardLayer();

        if (boardLayer < 0) { return; }

        Camera uiCamera = FindUiCamera();

        if (uiCamera == null)
        {
            Debug.LogError("BoardCameraWiring: no camera found in the open scene. Open "
                           + "Assets/Scenes/Game.unity and run this again.");
            return;
        }

        Camera boardCamera = EnsureBoardCamera(uiCamera, boardLayer);

        ConfigureUiCamera(uiCamera, boardCamera, boardLayer);
        RepointCanvases(uiCamera);
        WireSceneCameras(boardCamera, uiCamera);
        WireBattleManager(boardCamera);
        MoveBackdropToCamera(boardCamera);

        EditorSceneManager.MarkAllScenesDirty();

        Debug.Log($"BoardCameraWiring: {BoardCameraName} (Base, Board layer {boardLayer}) now renders "
                  + $"the board with {UiCameraName} (Overlay) stacked on top for the cards and HUD. "
                  + "Run 3 - Wire Board Objects next, then save the scene (Ctrl+S).");
    }

    /// <summary>
    /// Adds the Board layer if it is not already there, and returns its index.
    ///
    /// Searches from index 8 because 0-7 are Unity's own - Default, TransparentFX, Ignore Raycast,
    /// Water, UI and three reserved slots. Those reserved slots read as empty strings in the
    /// TagManager and can technically be written to, but Unity's own layer editor refuses to, and
    /// anything relying on that is a trap for whoever looks at the Inspector next.
    ///
    /// Appending only. A layer's *index* is what every prefab and scene stores, so renaming or
    /// reordering one silently repoints every object that used it - the same hazard the sorting layer
    /// list carries.
    /// </summary>
    private static int EnsureBoardLayer()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");

        if (assets == null || assets.Length == 0)
        {
            Debug.LogError("BoardCameraWiring: could not open ProjectSettings/TagManager.asset.");
            return -1;
        }

        SerializedObject tagManager = new(assets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 0; i < layers.arraySize; i++)
        {
            if (layers.GetArrayElementAtIndex(i).stringValue == BoardLayerName) { return i; }
        }

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);

            if (!string.IsNullOrEmpty(slot.stringValue)) { continue; }

            slot.stringValue = BoardLayerName;
            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            Debug.Log($"BoardCameraWiring: added user layer {i} \"{BoardLayerName}\".");

            return i;
        }

        Debug.LogError("BoardCameraWiring: every user layer slot is taken - free one and run again.");

        return -1;
    }

    /// The camera that will become the UI camera: the one already carrying CameraFrame, since that is
    /// the component defining the fixed 19.2 x 10.8 frame the HUD is authored against. Falls back to
    /// whatever is tagged MainCamera on a scene that has already been wired once.
    private static Camera FindUiCamera()
    {
        foreach (CameraFrame frame in Object.FindObjectsByType<CameraFrame>())
        {
            Camera camera = frame.GetComponent<Camera>();

            if (camera != null) { return camera; }
        }

        return Camera.main;
    }

    private static Camera EnsureBoardCamera(Camera uiCamera, int boardLayer)
    {
        GameObject existing = GameObject.Find(BoardCameraName);

        GameObject go = existing != null ? existing : new GameObject(BoardCameraName);

        Camera camera = Ensure<Camera>(go);

        camera.orthographic = true;
        camera.cullingMask = 1 << boardLayer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.nearClipPlane = uiCamera.nearClipPlane;
        camera.farClipPlane = uiCamera.farClipPlane;

        // Base, and the depth that puts it under the overlay. An Overlay camera does not render on its
        // own at all - it only renders as part of a Base camera's stack - so the stack membership
        // below is what actually draws the UI.
        UniversalAdditionalCameraData data = Ensure<UniversalAdditionalCameraData>(go);
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = true;

        data.cameraStack.Clear();
        data.cameraStack.Add(uiCamera);

        // Camera.main has to be the board camera: it is the gameplay camera, and it is the one whose
        // culling mask covers the tiles that OnMouseDown picking runs against.
        go.tag = "MainCamera";

        Ensure<BoardCamera>(go);

        // Only positioned on first creation. After that BoardCamera owns the transform, and stamping a
        // default here on a re-run would undo a framing the designer had just tuned.
        if (existing == null)
        {
            go.transform.position = new Vector3(0f, 0f, uiCamera.transform.position.z);
        }

        return camera;
    }

    private static void ConfigureUiCamera(Camera uiCamera, Camera boardCamera, int boardLayer)
    {
        uiCamera.gameObject.name = UiCameraName;

        // Everything except the board. An Overlay camera clears depth but never colour, so whatever
        // the board camera drew stays visible underneath.
        uiCamera.cullingMask = ~(1 << boardLayer);

        UniversalAdditionalCameraData data = Ensure<UniversalAdditionalCameraData>(uiCamera.gameObject);
        data.renderType = CameraRenderType.Overlay;

        // Unity's built-in mouse pass does not walk overlay cameras, so it sends this camera's
        // colliders nothing at all - which is why cards stopped hovering and clicking the moment they
        // moved here, while tiles on the Base camera kept working. WorldSpaceMouseInput dispatches
        // those messages instead; zeroing the mask means a future Unity that *does* walk overlay
        // cameras cannot also deliver them and double every click.
        uiCamera.eventMask = 0;

        // The AudioListener follows Camera.main by convention, and two in one scene is a warning every
        // frame. The board camera is the one tagged MainCamera now.
        AudioListener listener = uiCamera.GetComponent<AudioListener>();

        if (listener != null)
        {
            Object.DestroyImmediate(listener);
            Ensure<AudioListener>(boardCamera.gameObject);
        }

        // Untagged rather than left as MainCamera: two cameras claiming that tag makes Camera.main
        // return whichever Unity finds first, which is exactly the coin toss SceneCameras exists to
        // remove.
        if (uiCamera.CompareTag("MainCamera")) { uiCamera.gameObject.tag = "Untagged"; }
    }

    /// <summary>
    /// Points every Screen Space - Camera canvas at the UI camera.
    ///
    /// They currently reference the same camera object, which is about to stop rendering them - an
    /// Overlay camera still projects a canvas correctly, but only if the canvas actually names it.
    /// Overlay-mode canvases are skipped: they have no camera and want none.
    /// </summary>
    private static void RepointCanvases(Camera uiCamera)
    {
        int repointed = 0;

        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>())
        {
            if (canvas.renderMode != RenderMode.ScreenSpaceCamera) { continue; }

            canvas.worldCamera = uiCamera;
            EditorUtility.SetDirty(canvas);
            repointed++;
        }

        Debug.Log($"BoardCameraWiring: repointed {repointed} screen-space canvas(es) at {UiCameraName}.");
    }

    private static void WireSceneCameras(Camera boardCamera, Camera uiCamera)
    {
        SceneCameras existing = Object.FindAnyObjectByType<SceneCameras>();

        GameObject go = existing != null
            ? existing.gameObject
            : new GameObject(SceneCamerasName);

        SceneCameras cameras = Ensure<SceneCameras>(go);

        SerializedObject so = new(cameras);
        so.FindProperty("boardCamera").objectReferenceValue = boardCamera;
        so.FindProperty("uiCamera").objectReferenceValue = uiCamera;
        so.ApplyModifiedProperties();

        // The overlay camera's stand-in for Unity's own mouse pass - see ConfigureUiCamera. Lives on
        // the same object as SceneCameras because it is the same concern: which camera answers for
        // what.
        WorldSpaceMouseInput input = Ensure<WorldSpaceMouseInput>(go);

        SerializedObject inputSo = new(input);
        inputSo.FindProperty("dispatchFor").objectReferenceValue = uiCamera;
        inputSo.ApplyModifiedProperties();
    }

    private static void WireBattleManager(Camera boardCamera)
    {
        BattleManager battle = Object.FindAnyObjectByType<BattleManager>();

        if (battle == null)
        {
            Debug.LogWarning("BoardCameraWiring: no BattleManager in the scene, so nothing will call "
                             + "BoardCamera.Frame - the board will build but never be framed.");
            return;
        }

        SerializedObject so = new(battle);
        so.FindProperty("boardCamera").objectReferenceValue = boardCamera.GetComponent<BoardCamera>();
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Hands the flat backdrop over to the board camera.
    ///
    /// It is currently a full-screen UGUI Image forced onto the Background sorting layer - which
    /// worked while one camera drew everything in sorting-layer order. With a stack it would be drawn
    /// by the *overlay* camera, on top of the board, painting the whole thing out. The colour becomes
    /// the board camera's clear colour instead.
    ///
    /// Disabled rather than destroyed: it is a scene object somebody may want back, and a tool that
    /// deletes hand-authored scene content on a re-run is not one you can run twice with confidence.
    /// </summary>
    private static void MoveBackdropToCamera(Camera boardCamera)
    {
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>())
        {
            if (!canvas.overrideSorting) { continue; }

            if (SortingLayer.IDToName(canvas.sortingLayerID) != SortingLayers.Background) { continue; }

            UnityEngine.UI.Image image = canvas.GetComponent<UnityEngine.UI.Image>();

            if (image == null) { continue; }

            boardCamera.backgroundColor = image.color;

            if (canvas.gameObject.activeSelf)
            {
                canvas.gameObject.SetActive(false);
                Debug.Log($"BoardCameraWiring: moved the backdrop colour onto {BoardCameraName} and "
                          + $"disabled \"{canvas.gameObject.name}\" - it would otherwise be drawn by "
                          + "the overlay camera, on top of the board.");
            }

            return;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 3 - Wire Board Objects
    // ---------------------------------------------------------------------------------------------

    [MenuItem("Tools/Board/3 - Wire Board Objects")]
    static void WireBoardObjects()
    {
        int boardLayer = LayerMask.NameToLayer(BoardLayerName);

        if (boardLayer < 0)
        {
            Debug.LogError("BoardCameraWiring: no Board layer - run 2 - Wire Cameras first.");
            return;
        }

        GridManager grid = Object.FindAnyObjectByType<GridManager>();

        if (grid == null)
        {
            Debug.LogError("BoardCameraWiring: no GridManager in the open scene. Open "
                           + "Assets/Scenes/Game.unity and run this again.");
            return;
        }

        GameObject root = EnsureBoardRoot(boardLayer);
        BoardVisuals visuals = Ensure<BoardVisuals>(root);

        SerializedObject so = new(grid);
        so.FindProperty("tileParent").objectReferenceValue = root.transform;
        so.FindProperty("boardVisuals").objectReferenceValue = visuals;
        so.ApplyModifiedProperties();

        int prefabs = 0;

        foreach (string path in CollectBoardPrefabs())
        {
            if (ApplyToPrefab(path, boardLayer)) { prefabs++; }
        }

        // The tile prefab is under Assets/Prefabs/UI rather than with the bodies, because it started
        // life as a UI element. It is board geometry regardless of where it is filed.
        if (ApplyToPrefab("Assets/Prefabs/UI/GridTilePrefab.prefab", boardLayer)) { prefabs++; }

        RetireOldTilemap();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkAllScenesDirty();

        Debug.Log($"BoardCameraWiring: board root \"{BoardRootName}\" wired into GridManager, and "
                  + $"{prefabs} prefab(s) moved onto the {BoardLayerName} layer. Save the scene (Ctrl+S).");
    }

    private static GameObject EnsureBoardRoot(int boardLayer)
    {
        GameObject existing = GameObject.Find(BoardRootName);
        GameObject root = existing != null ? existing : new GameObject(BoardRootName);

        // Identity, and left that way. Nothing scales this root - the camera does the fitting - so a
        // stray scale here would silently desynchronise the tiles from the blocks under them.
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.transform.localScale = Vector3.one;

        SetLayerRecursively(root, boardLayer);

        return root;
    }

    private static List<string> CollectBoardPrefabs()
    {
        List<string> paths = new();

        foreach (string folder in BoardPrefabFolders)
        {
            if (!AssetDatabase.IsValidFolder(folder)) { continue; }

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        return paths;
    }

    /// <summary>
    /// Puts one prefab's whole hierarchy on the board layer, and gives it a SortingGroup if it is one
    /// of the bodies that never had one.
    ///
    /// The *whole* hierarchy matters: a character's overhead health bar is a World Space Canvas child
    /// sitting on Unity's UI layer, which the board camera would cull. Leaving it behind would put
    /// every health bar on the fixed UI camera, where it would neither move nor scale with the body
    /// it belongs to.
    /// </summary>
    private static bool ApplyToPrefab(string path, int boardLayer)
    {
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab")) { return false; }

        GameObject contents = PrefabUtility.LoadPrefabContents(path);

        if (contents == null) { return false; }

        try
        {
            SetLayerRecursively(contents, boardLayer);

            foreach (string needs in NeedSortingGroup)
            {
                if (needs != path) { continue; }

                SortingGroup group = Ensure<SortingGroup>(contents);
                group.sortingLayerName = SortingLayers.Characters;
                break;
            }

            PrefabUtility.SaveAsPrefabAsset(contents, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        return true;
    }

    /// <summary>
    /// Disables the painted Tilemap the block floor replaces.
    ///
    /// It is a fixed 48-cell plate on a 2.5 x 1.25 pitch, laid down when the logical board ran on
    /// 3.0 x 1.5 - so it never lined up, and it never changed with board size. BoardVisuals builds the
    /// real floor now.
    ///
    /// Disabled rather than deleted, same reasoning as the backdrop: this is hand-authored scene
    /// content, and a tool that destroys it on a re-run is one you hesitate to run.
    /// </summary>
    private static void RetireOldTilemap()
    {
        foreach (UnityEngine.Tilemaps.TilemapRenderer tilemap
                 in Object.FindObjectsByType<UnityEngine.Tilemaps.TilemapRenderer>())
        {
            Transform root = tilemap.transform.parent != null ? tilemap.transform.parent : tilemap.transform;

            if (!root.gameObject.activeSelf) { continue; }

            root.gameObject.SetActive(false);

            Debug.Log($"BoardCameraWiring: disabled the old painted tilemap \"{root.name}\" - "
                      + "BoardVisuals builds the floor now. Delete it once you are happy.");
        }
    }

    // ---------------------------------------------------------------------------------------------

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;

        foreach (Transform child in go.transform) { SetLayerRecursively(child.gameObject, layer); }
    }

    /// Local copy of the same two-line helper TutorialWiring carries - that one is private. Written out
    /// rather than as `GetComponent<T>() ?? AddComponent<T>()`, which never adds anything: GetComponent
    /// returns a fake-null that `??` hands straight back. See CLAUDE.md.
    private static T Ensure<T>(GameObject go) where T : Component
    {
        T existing = go.GetComponent<T>();

        if (existing == null) { existing = go.AddComponent<T>(); }

        return existing;
    }
}
