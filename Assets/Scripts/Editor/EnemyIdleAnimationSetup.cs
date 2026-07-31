using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

static class EnemyIdleAnimationSetup
{
    const string SkeletonWarriorSprites = "Assets/Extra Assets/Hero and Opponents/Sprites/Enemy2";
    const string EnemyRangerSprites = "Assets/Extra Assets/Hero and Opponents/Sprites/Enemy5";
    const string OutputFolder = "Assets/Animations";

    [MenuItem("Tools/Setup Enemy Idle Animations")]
    static void Setup()
    {
        if (!AssetDatabase.IsValidFolder(OutputFolder))
            AssetDatabase.CreateFolder("Assets", "Animations");

        var skeletonController = BuildIdleController("SkeletonWarrior", SkeletonWarriorSprites, 4, 8f);
        var rangerController = BuildIdleController("EnemyRanger", EnemyRangerSprites, 2, 6f);

        ApplyToPrefab("Assets/Prefabs/SkeletonWarrior.prefab", skeletonController);
        ApplyToSceneObject("SkeletonWarrior (1)", skeletonController, null);
        ApplyToSceneObject("EnemyRanger", rangerController, EnemyRangerSprites + "/idle-1.png");
        ApplyToSceneObject("EnemyRanger (1)", rangerController, EnemyRangerSprites + "/idle-1.png");

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("Enemy idle animations are set up. Save the scene (Ctrl+S) to keep the changes.");
    }

    static AnimatorController BuildIdleController(string characterName, string spriteFolder, int frameCount, float frameRate)
    {
        var keyframes = new ObjectReferenceKeyframe[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            var path = $"{spriteFolder}/idle-{i + 1}.png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                Debug.LogError($"EnemyIdleAnimationSetup: could not load sprite at {path}");
            keyframes[i] = new ObjectReferenceKeyframe { time = i / frameRate, value = sprite };
        }

        var clip = new AnimationClip { frameRate = frameRate };
        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        var clipPath = $"{OutputFolder}/{characterName}_Idle.anim";
        AssetDatabase.DeleteAsset(clipPath);
        AssetDatabase.CreateAsset(clip, clipPath);

        var controllerPath = $"{OutputFolder}/{characterName}.controller";
        AssetDatabase.DeleteAsset(controllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.AddMotion(clip);

        return controller;
    }

    static void ApplyToPrefab(string prefabPath, AnimatorController controller)
    {
        var contents = PrefabUtility.LoadPrefabContents(prefabPath);
        ApplyAnimator(contents, controller);
        PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
        PrefabUtility.UnloadPrefabContents(contents);
    }

    static void ApplyToSceneObject(string objectName, AnimatorController controller, string spritePathOverride)
    {
        var go = GameObject.Find(objectName);
        if (go == null)
        {
            Debug.LogError($"EnemyIdleAnimationSetup: no GameObject named '{objectName}' in the open scene. Open Assets/Scenes/Game.unity and run this again.");
            return;
        }

        ApplyAnimator(go, controller);

        if (spritePathOverride != null)
        {
            var renderer = go.GetComponent<SpriteRenderer>();
            if (renderer != null)
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePathOverride);
        }

        EditorUtility.SetDirty(go);
    }

    static void ApplyAnimator(GameObject go, AnimatorController controller)
    {
        var animator = go.GetComponent<Animator>();
        if (animator == null)
            animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
    }
}
