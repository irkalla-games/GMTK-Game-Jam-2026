using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stamps the settled board numbers onto the scene and the prefabs.
///
/// The framing knobs live on the scene's BoardCamera and the body sizes on the character prefabs, so
/// neither can be changed by editing a script default - a serialized value already written wins. This
/// is how a tuned number actually lands.
///
/// **The constants below are the tuning.** Adjust one, re-run, look. That is deliberately the loop
/// rather than "type it into the Inspector and hope it gets written down": CLAUDE.md's rule is to
/// tune in the Inspector *after* the last run, or lift the numbers into constants - and while this is
/// still moving, constants are the half that survives.
///
/// Every value is absolute, never relative, so running this twice is the same as running it once.
/// Scaling by a factor would shrink the characters again on every re-run.
/// </summary>
static class BoardTuning
{
    // ---- Framing -------------------------------------------------------------------------------

    /// Where on screen the board is allowed to sit. Bottom quarter left to the hand, sliver at the
    /// top for the HUD.
    static readonly Rect BoardViewport = new(0.02f, 0.26f, 0.96f, 0.70f);

    /// Board shifted right and down. +x right, +y up - so a negative y is downward.
    static readonly Vector2 FocusOffset = new(1f, -0.8f);

    /// Below 1 pulls the camera in. 0.9 draws the board about 11% larger than a bare fit.
    const float ZoomScale = 0.9f;

    /// Lowered from 6 so ZoomScale is not immediately clamped away on the smaller boards.
    const float MinOrthoSize = 5f;

    const float MaxOrthoSize = 12f;

    // ---- Body sizes ----------------------------------------------------------------------------

    /// Heroes were authored at 1.0, the single-sprite bodies at 3.0. Both are here at 80% of that.
    /// Absolute values, not a multiplier - see the class note.
    const float HeroScale = 0.8f;

    const float BodyScale = 2.4f;

    /// <summary>
    /// Root scales, per prefab. Only the base prefabs are listed: PlayerKnightTutorial,
    /// PlayerMageTutorial, EnemyRangerTutorial and SkeletonWarriorTutorial are *variants* that do not
    /// override m_LocalScale, so they inherit whatever their base is set to. Writing them explicitly
    /// would create an override on each and quietly pin them to today's number forever.
    ///
    /// Totems are deliberately absent - they were not part of the resize. If they end up looking
    /// oversized next to the shrunken bodies, add Assets/Prefabs/Totem/Totem.prefab here at 0.8.
    /// </summary>
    static readonly (string path, float scale)[] BodyScales =
    {
        ("Assets/Prefabs/Player/PlayerKnight.prefab", HeroScale),
        ("Assets/Prefabs/Player/PlayerMage.prefab", HeroScale),
        ("Assets/Prefabs/Player/PlayerRogue.prefab", HeroScale),
        ("Assets/Prefabs/Enemies/EnemyRanger.prefab", BodyScale),
        ("Assets/Prefabs/Enemies/SkeletonWarrior.prefab", BodyScale),
        ("Assets/Prefabs/Allies/SkeletonAlly.prefab", BodyScale),
    };

    // ---- Block tint ----------------------------------------------------------------------------

    /// About 40% darker. 0.6 in an sRGB colour field, which is what the Inspector and this field both
    /// are - Unity converts it to linear before multiplying, so the result reads as 60% brightness
    /// rather than the 79% a raw linear 0.6 would give in this project's Linear colour space.
    static readonly Color BlockTint = new(0.6f, 0.6f, 0.6f, 1f);

    const string DefaultSetPath = "Assets/Data/TileSetData/StoneDungeon.asset";

    // ---- Tile fill -----------------------------------------------------------------------------

    /// <summary>
    /// The resting wash on an unhighlighted tile.
    ///
    /// Halved from the authored 0.086. The tiles used to be drawn 17% smaller than their pitch, so a
    /// gutter of bare floor broke the board up and each diamond read separately; sizing them to the
    /// cell closed that gutter and turned the same alpha into one continuous sheet. TileBorder does
    /// the separating now, so the fill can afford to be fainter than it ever was.
    /// </summary>
    static readonly Color TileIdleColor = new(1f, 1f, 1f, 0.045f);

    const string TilePrefabPath = "Assets/Prefabs/UI/GridTilePrefab.prefab";

    [MenuItem("Tools/Board/5 - Apply Tuning")]
    static void ApplyTuning()
    {
        ApplyFraming();
        ApplyBodyScales();
        ApplyBlockTint();
        ApplyTileFill();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkAllScenesDirty();

        Debug.Log("BoardTuning: framing, body scales and block tint applied. Save the scene (Ctrl+S).");
    }

    private static void ApplyFraming()
    {
        BoardCamera camera = Object.FindAnyObjectByType<BoardCamera>();

        if (camera == null)
        {
            Debug.LogWarning("BoardTuning: no BoardCamera in the open scene - run "
                             + "Tools/Board/2 - Wire Cameras first. Framing was not applied.");
            return;
        }

        SerializedObject so = new(camera);
        so.FindProperty("boardViewport").rectValue = BoardViewport;
        so.FindProperty("focusOffset").vector2Value = FocusOffset;
        so.FindProperty("zoomScale").floatValue = ZoomScale;
        so.FindProperty("minOrthoSize").floatValue = MinOrthoSize;
        so.FindProperty("maxOrthoSize").floatValue = MaxOrthoSize;
        so.ApplyModifiedProperties();

        Debug.Log($"BoardTuning: framing set - zoom x{ZoomScale}, offset {FocusOffset} "
                  + "(+x right, +y up), min ortho " + MinOrthoSize + ".");
    }

    private static void ApplyBodyScales()
    {
        int changed = 0;

        foreach ((string path, float scale) in BodyScales)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);

            if (contents == null)
            {
                Debug.LogWarning($"BoardTuning: no prefab at {path} - skipped.");
                continue;
            }

            try
            {
                // z left at 1. These are 2D bodies and a scaled z does nothing but make the transform
                // read oddly - the authored prefabs all carry {x, y, 1}.
                contents.transform.localScale = new Vector3(scale, scale, 1f);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                changed++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        Debug.Log($"BoardTuning: {changed} body prefab(s) resized - heroes {HeroScale}, "
                  + $"single-sprite bodies {BodyScale}. Their overhead health bars are children, so "
                  + "they scale with them.");
    }

    /// <summary>
    /// Writes the resting tile colour onto the tile prefab.
    ///
    /// Both the TileSelector field and the SpriteRenderer are set. Only the first matters at runtime -
    /// TileSelector.ApplyColor overwrites the renderer in Awake - but leaving the renderer on the old
    /// value means the prefab and the Scene view preview disagree with the game, which is exactly the
    /// sort of thing that gets tuned twice.
    /// </summary>
    private static void ApplyTileFill()
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(TilePrefabPath);

        if (contents == null)
        {
            Debug.LogWarning($"BoardTuning: no prefab at {TilePrefabPath} - tile fill not applied.");
            return;
        }

        try
        {
            TileSelector selector = contents.GetComponent<TileSelector>();

            if (selector == null)
            {
                Debug.LogWarning($"BoardTuning: {TilePrefabPath} has no TileSelector - fill not applied.");
                return;
            }

            SerializedObject so = new(selector);
            so.FindProperty("idleColor").colorValue = TileIdleColor;
            so.ApplyModifiedProperties();

            SpriteRenderer renderer = contents.GetComponent<SpriteRenderer>();

            if (renderer != null) { renderer.color = TileIdleColor; }

            PrefabUtility.SaveAsPrefabAsset(contents, TilePrefabPath);

            Debug.Log($"BoardTuning: tile resting alpha set to {TileIdleColor.a} - the dark border "
                      + "separates the cells now, so the fill does not have to.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void ApplyBlockTint()
    {
        TileSetData set = AssetDatabase.LoadAssetAtPath<TileSetData>(DefaultSetPath);

        if (set == null)
        {
            Debug.LogWarning($"BoardTuning: no tileset at {DefaultSetPath} - run "
                             + "Tools/Board/4 - Author Default Tile Set first. Tint was not applied.");
            return;
        }

        SerializedObject so = new(set);
        so.FindProperty("groundTint").colorValue = BlockTint;
        so.FindProperty("wallTint").colorValue = BlockTint;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(set);

        Debug.Log($"BoardTuning: floor and wall blocks tinted to {BlockTint} - about 40% darker.");
    }
}
