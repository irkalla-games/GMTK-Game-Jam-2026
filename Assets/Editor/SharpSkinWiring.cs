using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Applies the Sharp GUI skin across the battle HUD: every button in Game.unity, the shared
/// SkipButtonPrefab behind five of them, and the panel surfaces on the party sheet, equipment tiles and
/// status rows.
///
/// **Never touches a Filled image.** HealthFill, ShieldFill and the energy pips are driven by
/// Image.fillAmount and fillOrigin - see HealthBarFill, which is the one place that arithmetic lives -
/// and SharpSkin.ApplySliced rewrites image.type to Sliced or Simple. Running it over a fill would
/// leave every health bar in the game permanently full, with nothing in the console to say why. Only
/// the bar BACKGROUNDS are skinned here; the fills keep their authored type and are recoloured, at
/// most, through PanelPalette.
///
/// Scene buttons that belong to a prefab instance are skipped, because the prefab asset itself is
/// skinned first - skinning both would work, but every instance would carry a pile of prefab overrides
/// that make a real future override impossible to spot.
/// </summary>
public static class SharpSkinWiring
{
    private const string ScenePath = "Assets/Scenes/Game.unity";

    private const string SkipButtonPrefabPath = "Assets/Prefabs/UI/SkipButtonPrefab.prefab";
    private const string HeroPortraitPath = "Assets/Prefabs/UI/HeroPortrait.prefab";
    private const string EquipmentViewerPath = "Assets/Prefabs/UI/EquipmentViewer.prefab";
    private const string StatusChipPath = "Assets/Prefabs/UI/StatusChip.prefab";
    private const string WaveCirclePath = "Assets/Prefabs/UI/WaveCircle.prefab";

    [MenuItem("Tools/Battle HUD/Skin Buttons And Panels")]
    public static void Skin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Sharp skin: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        SkinSkipButtonPrefab();
        SkinHudPrefabs();

        if (!OpenGameScene()) { return; }

        int skinned = SkinSceneButtons();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log($"Sharp skin: done - {skinned} scene button(s) skinned, prefabs and scene saved.");
    }

    /// <summary>
    /// The shared button prefab. Five instances in Game.unity derive from it - PileCloseButton,
    /// CancelButton, DiscardPileButton, SheetCloseButton and DeckPileButton - so this one edit is what
    /// re-skins most of the battle UI's buttons.
    /// </summary>
    private static void SkinSkipButtonPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(SkipButtonPrefabPath);

        if (root == null)
        {
            Debug.LogWarning($"Sharp skin: {SkipButtonPrefabPath} not found.");
            return;
        }

        Button button = root.GetComponent<Button>();

        if (button != null)
        {
            SharpSkin.ApplyButton(button);

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(includeInactive: true);

            if (label != null)
            {
                label.alignment = TextAlignmentOptions.Center;
                EditorUtility.SetDirty(label);
            }
        }

        PrefabUtility.SaveAsPrefabAsset(root, SkipButtonPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    /// <summary>
    /// Skins every Button in the open scene that is not part of a prefab instance.
    ///
    /// Every button rather than a named list: the goal is one look, and a named list is a thing to
    /// forget to add to. The pause menu's own buttons are already skinned by PauseMenuWiring, and
    /// re-applying is idempotent, so overlapping with it costs nothing.
    /// </summary>
    private static int SkinSceneButtons()
    {
        int count = 0;

        foreach (Button button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include))
        {
            // The prefab asset was skinned above; skinning the instance too would bury a real override
            // in a crowd of styling ones.
            if (PrefabUtility.IsPartOfPrefabInstance(button)) { continue; }

            SharpSkin.ApplyButton(button);

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(includeInactive: true);

            if (label != null)
            {
                label.alignment = TextAlignmentOptions.Center;
                EditorUtility.SetDirty(label);
            }

            count++;
        }

        return count;
    }

    private static void SkinHudPrefabs()
    {
        // PartySheetColumn.prefab and StatusDetailRow.prefab are deliberately absent: Tools/Battle
        // HUD/Restyle Party Sheet already owns their chrome, including that column's own HealthBarBg.
        // Two commands writing the same Image is two answers to one question - whichever ran last
        // would win, and which that was would depend on the order someone happened to click them.
        SkinPrefab(HeroPortraitPath, root =>
        {
            SkinNamed(root, "HealthBarBg", SharpSkin.BarFrame);
        });

        SkinPrefab(EquipmentViewerPath, root =>
        {
            SkinRoot(root, SharpSkin.Panel);
        });

        // Chips and wave circles are pictures in a border rather than surfaces, so they take the
        // heavy frame. Their Icon / PortraitImage children are assigned at runtime from StatusIcons
        // and the enemy roster, and are deliberately not touched.
        SkinPrefab(StatusChipPath, root =>
        {
            SkinRoot(root, SharpSkin.Frame, 4f);
        });

        SkinPrefab(WaveCirclePath, root =>
        {
            SkinRoot(root, SharpSkin.Frame, 4f);
        });
    }

    /// <summary>
    /// Opens a prefab asset, runs an edit over it, and saves it back.
    ///
    /// LoadPrefabContents rather than editing instances in a scene: HeroPortrait, StatusChip and
    /// WaveCircle are all pooled and spawned at runtime, so anything styled on a live instance is gone
    /// the next time the pool rebuilds.
    /// </summary>
    private static void SkinPrefab(string path, System.Action<GameObject> edit)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogWarning($"Sharp skin: {path} not found - skipped.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);

        edit(root);

        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
    }

    private static void SkinRoot(GameObject root, string sprite, float pixelsPerUnitMultiplier = 1f)
    {
        Image image = root.GetComponent<Image>();

        if (image == null)
        {
            Debug.LogWarning($"Sharp skin: {root.name} has no Image on its root - skipped.");
            return;
        }

        if (IsFilled(image, root.name)) { return; }

        SharpSkin.ApplySliced(image, sprite, pixelsPerUnitMultiplier);
    }

    /// <summary>
    /// Finds a descendant by name and skins its Image.
    ///
    /// Searched across all descendants rather than direct children, because these names sit at
    /// different depths in different prefabs - HealthBarBg is a grandchild in PartySheetColumn and a
    /// child in HeroPortrait.
    /// </summary>
    private static void SkinNamed(GameObject root, string childName, string sprite)
    {
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            if (candidate.name != childName) { continue; }

            Image image = candidate.GetComponent<Image>();

            if (image == null) { continue; }

            if (IsFilled(image, childName)) { return; }

            SharpSkin.ApplySliced(image, sprite);

            return;
        }
    }


    /// <summary>
    /// Refuses to skin an Image whose type is Filled.
    ///
    /// A guard rather than a convention, because the consequence is invisible: ApplySliced would set
    /// image.type to Sliced, fillAmount would stop having any effect, and every health bar would render
    /// permanently full with no error anywhere. HealthBarFill drives those images and expects Filled to
    /// survive.
    /// </summary>
    private static bool IsFilled(Image image, string label)
    {
        if (image.type != Image.Type.Filled) { return false; }

        Debug.Log($"Sharp skin: {label} is a Filled image (a health, shield or energy fill) - left alone.");

        return true;
    }

    private static bool OpenGameScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);

        return true;
    }
}
