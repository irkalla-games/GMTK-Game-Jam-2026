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

    // -----------------------------------------------------------------------------------------
    // Attack/hurt/die/move animations, on top of the idle-only setup above. A separate menu item
    // and its own clip-building method rather than reusing BuildIdleController - that one is left
    // exactly as it was so the original tool keeps behaving exactly as it always has.
    //
    // Heroes are not touched here at all. PlayerKnight/PlayerMage already reference Knight.controller
    // and Priest.controller from the Miniature Army pack, and those already contain every state this
    // needs - idle, attack, casting, walk, hurt, die - with no parameters and no transitions. Calling
    // Animator.Play(stateName, 0, 0f) at runtime does not need a transition to reach a state, so there
    // is nothing to add to those controllers; only a CharacterAnimator pointing at the states they
    // already have.
    // -----------------------------------------------------------------------------------------

    /// The frame whose pivot every other frame in the same folder is matched to. Idle is the reference
    /// because it is the one the artist actually set - see NormalisePivots.
    const string PivotReferenceFrame = "idle-1.png";

    /// <summary>
    /// Copies the idle frames' hand-set pivot onto every other frame in each enemy's folder.
    ///
    /// Only the idle frames were ever authored with one: they carry alignment 9 (Custom) at roughly
    /// (0.35, 0) - the character's feet, near the bottom of a 128x96 canvas - while every attack, hit,
    /// death and walk frame was left at Unity's default alignment 0 (Center), (0.5, 0.5). So the body
    /// jumped half a canvas the moment anything but idle played.
    ///
    /// Safe to copy the normalised pivot verbatim because every frame in both folders is the same
    /// 128x96 canvas, so 0.35 lands on the same pixel in all of them. It is also the *right* fix rather
    /// than centring each frame on its own artwork: these are drawn in place on a shared canvas, and
    /// re-centring per frame would cancel out the lunge an attack animation is supposed to have.
    /// </summary>
    [MenuItem("Tools/Fix Enemy Sprite Pivots")]
    static void FixEnemySpritePivots()
    {
        int changed = NormalisePivots(SkeletonWarriorSprites) + NormalisePivots(EnemyRangerSprites);

        Debug.Log($"Enemy sprite pivots: {changed} frame(s) repointed to match {PivotReferenceFrame}.");
    }

    static int NormalisePivots(string spriteFolder)
    {
        string referencePath = $"{spriteFolder}/{PivotReferenceFrame}";

        if (AssetImporter.GetAtPath(referencePath) is not TextureImporter reference)
        {
            Debug.LogError($"NormalisePivots: no reference frame at {referencePath} - nothing in "
                           + $"{spriteFolder} was changed.");
            return 0;
        }

        var referenceSettings = new TextureImporterSettings();
        reference.ReadTextureSettings(referenceSettings);

        Vector2 pivot = referenceSettings.spritePivot;
        int alignment = referenceSettings.spriteAlignment;

        if (alignment != (int)SpriteAlignment.Custom)
        {
            Debug.LogWarning($"NormalisePivots: {referencePath} is not on a custom pivot, so there is "
                             + "nothing distinctive to copy. Set the idle frame's pivot first.");
            return 0;
        }

        int changed = 0;

        // Batched: without this every SaveAndReimport triggers its own refresh, which on ~40 frames
        // per folder is a visible stall.
        AssetDatabase.StartAssetEditing();

        try
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { spriteFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) { continue; }

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);

                if (settings.spriteAlignment == alignment && settings.spritePivot == pivot) { continue; }

                settings.spriteAlignment = alignment;
                settings.spritePivot = pivot;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
                changed++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        return changed;
    }

    [MenuItem("Tools/Setup Character Animations")]
    static void SetupCharacterAnimations()
    {
        if (!AssetDatabase.IsValidFolder(OutputFolder))
            AssetDatabase.CreateFolder("Assets", "Animations");

        // Before the clips are built, so a freshly generated clip never bakes in a half-canvas jump.
        FixEnemySpritePivots();

        var skeletonController = BuildCombatController("SkeletonWarrior", new[]
        {
            new ClipSpec("Idle", SkeletonWarriorSprites, "idle-", 4, 8f, true),
            new ClipSpec("MeleeAttack", SkeletonWarriorSprites, "attack-A", 12, 16f, false),
            new ClipSpec("Hurt", SkeletonWarriorSprites, "hit-", 3, 12f, false),
            new ClipSpec("Die", SkeletonWarriorSprites, "dead-", 4, 8f, false),
            new ClipSpec("Move", SkeletonWarriorSprites, "walk-", 6, 10f, true),
        });

        var rangerController = BuildCombatController("EnemyRanger", new[]
        {
            new ClipSpec("Idle", EnemyRangerSprites, "idle-", 2, 6f, true),
            new ClipSpec("RangedAttack", EnemyRangerSprites, "attack-A", 6, 16f, false),
            new ClipSpec("Hurt", EnemyRangerSprites, "hit-", 4, 12f, false),
            new ClipSpec("Die", EnemyRangerSprites, "dead-", 4, 8f, false),
            new ClipSpec("Move", EnemyRangerSprites, "run-", 12, 14f, true),
        });

        ApplyCharacterAnimator("Assets/Prefabs/SkeletonWarrior.prefab", skeletonController, "Idle",
            new[]
            {
                new CueBinding { cue = AnimationCue.MeleeAttack, stateName = "MeleeAttack" },
                new CueBinding { cue = AnimationCue.Hurt, stateName = "Hurt" },
                new CueBinding { cue = AnimationCue.Die, stateName = "Die" },
                new CueBinding { cue = AnimationCue.Move, stateName = "Move" },
            }, useFlipRenderer: true);

        ApplyCharacterAnimator("Assets/Prefabs/EnemyRanger.prefab", rangerController, "Idle",
            new[]
            {
                new CueBinding { cue = AnimationCue.RangedAttack, stateName = "RangedAttack" },
                new CueBinding { cue = AnimationCue.Hurt, stateName = "Hurt" },
                new CueBinding { cue = AnimationCue.Die, stateName = "Die" },
                new CueBinding { cue = AnimationCue.Move, stateName = "Move" },
            }, useFlipRenderer: true);

        // null controller = leave whatever is already assigned (Knight.controller / Priest.controller)
        // alone; only the CharacterAnimator cue table is new.
        //
        // The pack's clip is called "attack" on both heroes, but the two mean different things: the
        // Knight's swings a sword in reach, the Priest's is a staff cast thrown at range. So they bind
        // to different cues, which is exactly the indirection the cue table exists for - one clip name,
        // two meanings, and neither hero needs the card to know which.
        ApplyCharacterAnimator("Assets/Prefabs/PlayerKnight.prefab", null, "idle",
            new[]
            {
                new CueBinding { cue = AnimationCue.MeleeAttack, stateName = "attack" },
                new CueBinding { cue = AnimationCue.Cast, stateName = "casting" },
                new CueBinding { cue = AnimationCue.Hurt, stateName = "hurt" },
                new CueBinding { cue = AnimationCue.Die, stateName = "die" },
                new CueBinding { cue = AnimationCue.Move, stateName = "walk" },
            }, useFlipRenderer: false);

        ApplyCharacterAnimator("Assets/Prefabs/PlayerMage.prefab", null, "idle",
            new[]
            {
                // Fireball and every other ranged card land here. "casting" stays on Cast for heals
                // and buffs, which should not read as an attack at all.
                new CueBinding { cue = AnimationCue.RangedAttack, stateName = "attack" },
                // A melee card in a mage's hand still shows something rather than freezing.
                new CueBinding { cue = AnimationCue.MeleeAttack, stateName = "attack" },
                new CueBinding { cue = AnimationCue.Cast, stateName = "casting" },
                new CueBinding { cue = AnimationCue.Hurt, stateName = "hurt" },
                new CueBinding { cue = AnimationCue.Die, stateName = "die" },
                new CueBinding { cue = AnimationCue.Move, stateName = "walk" },
            }, useFlipRenderer: false);

        AssetDatabase.SaveAssets();
        Debug.Log("Character animations are set up: SkeletonWarrior and EnemyRanger got new "
                 + "MeleeAttack/RangedAttack/Hurt/Die/Move controllers, and all four prefabs now carry "
                 + "a CharacterAnimator. Knight/Priest controllers were not touched - only read. "
                 + "'Faces Right By Default' is never written by this tool, so a per-character flip "
                 + "correction set in the Inspector survives re-running it.");
    }

    /// One state's source frames and playback, going into BuildCombatController.
    private readonly struct ClipSpec
    {
        public readonly string state;
        public readonly string spriteFolder;
        public readonly string framePrefix;
        public readonly int frameCount;
        public readonly float frameRate;
        public readonly bool loop;

        public ClipSpec(string state, string spriteFolder, string framePrefix, int frameCount,
                         float frameRate, bool loop)
        {
            this.state = state;
            this.spriteFolder = spriteFolder;
            this.framePrefix = framePrefix;
            this.frameCount = frameCount;
            this.frameRate = frameRate;
            this.loop = loop;
        }
    }

    /// Builds one clip per spec and adds each as a state named after ClipSpec.state - not whatever
    /// the underlying clip asset happens to be called, so CharacterAnimator's cue table can name a
    /// short, stable state ("Attack") independent of the generated asset's file name. The first spec
    /// (Idle, by convention) becomes the controller's default state.
    static AnimatorController BuildCombatController(string characterName, ClipSpec[] specs)
    {
        var controllerPath = $"{OutputFolder}/{characterName}.controller";
        AssetDatabase.DeleteAsset(controllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

        foreach (ClipSpec spec in specs)
        {
            AnimationClip clip = BuildSpriteClip($"{characterName}_{spec.state}", spec);
            var state = controller.AddMotion(clip);
            state.name = spec.state;
        }

        return controller;
    }

    static AnimationClip BuildSpriteClip(string clipFileName, ClipSpec spec)
    {
        var keyframes = new ObjectReferenceKeyframe[spec.frameCount];
        for (int i = 0; i < spec.frameCount; i++)
        {
            var path = $"{spec.spriteFolder}/{spec.framePrefix}{i + 1}.png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                Debug.LogError($"SetupCharacterAnimations: could not load sprite at {path}");
            keyframes[i] = new ObjectReferenceKeyframe { time = i / spec.frameRate, value = sprite };
        }

        var clip = new AnimationClip { frameRate = spec.frameRate };
        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = spec.loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        var clipPath = $"{OutputFolder}/{clipFileName}.anim";
        AssetDatabase.DeleteAsset(clipPath);
        AssetDatabase.CreateAsset(clip, clipPath);

        return clip;
    }

    /// <summary>
    /// How long the clip behind `stateName` runs, or 0 if there is none.
    ///
    /// Matches the clip's own name first, then a "{Character}_{stateName}" suffix, because the two
    /// naming schemes in play disagree: the pack's hero clips are named exactly like their states
    /// ("attack"), while clips this tool generates are prefixed with the character to keep them unique
    /// inside one Animations folder ("SkeletonWarrior_MeleeAttack").
    /// </summary>
    static float ClipLengthFor(RuntimeAnimatorController controller, string stateName)
    {
        if (controller == null || string.IsNullOrEmpty(stateName)) { return 0f; }

        foreach (AnimationClip clip in controller.animationClips)
        {
            if (clip != null && clip.name == stateName) { return clip.length; }
        }

        foreach (AnimationClip clip in controller.animationClips)
        {
            if (clip != null && clip.name.EndsWith("_" + stateName)) { return clip.length; }
        }

        return 0f;
    }

    /// <summary>
    /// Adds (or updates) the Animator and a CharacterAnimator on one prefab, entirely through
    /// SerializedObject so this works on CharacterAnimator's private [SerializeField]s without the
    /// class needing public setters just for this tool.
    ///
    /// useFlipRenderer picks which flip strategy this body uses - see CharacterAnimator.SetFacing:
    /// true wires flipRenderer to the prefab's own root SpriteRenderer (the enemies, one sprite each),
    /// false wires counterFlip to its "Canvas" child (the heroes, a multi-part puppet whose Canvas
    /// must not mirror along with the rest of the body).
    /// </summary>
    static void ApplyCharacterAnimator(string prefabPath, AnimatorController controller,
                                        string idleState, CueBinding[] cues, bool useFlipRenderer)
    {
        var contents = PrefabUtility.LoadPrefabContents(prefabPath);

        var animator = contents.GetComponent<Animator>();
        if (animator == null) { animator = contents.AddComponent<Animator>(); }
        if (controller != null) { animator.runtimeAnimatorController = controller; }

        var characterAnimator = contents.GetComponent<CharacterAnimator>();
        if (characterAnimator == null) { characterAnimator = contents.AddComponent<CharacterAnimator>(); }

        var so = new SerializedObject(characterAnimator);
        so.FindProperty("animator").objectReferenceValue = animator;
        so.FindProperty("idleStateName").stringValue = idleState;

        // Whichever controller this prefab will actually run with: the one just generated, or the one
        // it already had (the heroes, whose pack controllers this tool only reads).
        RuntimeAnimatorController effective = controller != null
            ? controller
            : animator.runtimeAnimatorController;

        var cuesProp = so.FindProperty("cues");
        cuesProp.ClearArray();
        for (int i = 0; i < cues.Length; i++)
        {
            // Resolved here, at build time, rather than left at 0 for CharacterAnimator to look up at
            // runtime. Runtime can only search RuntimeAnimatorController.animationClips by *clip*
            // name, and a generated clip is called "SkeletonWarrior_MeleeAttack" while its state is
            // called "MeleeAttack" - so that lookup found nothing, every enemy cue resolved to a 0
            // second hold, and Play returned to idle on the same frame it started. The heroes only
            // escaped it because the Miniature Army clips happen to be named exactly like their states.
            float duration = cues[i].duration > 0f
                ? cues[i].duration
                : ClipLengthFor(effective, cues[i].stateName);

            if (duration <= 0f)
            {
                Debug.LogWarning($"{prefabPath}: no clip found for state '{cues[i].stateName}' - that "
                                 + "cue will play for zero seconds. Check the state name matches the "
                                 + "controller.");
            }

            cuesProp.InsertArrayElementAtIndex(i);
            var element = cuesProp.GetArrayElementAtIndex(i);
            // intValue, not enumValueIndex: the latter writes the enum member's *position* in the
            // declaration, which only equals its value while AnimationCue stays contiguous. Appending
            // a cue with a gap would silently rebind every row below it.
            element.FindPropertyRelative("cue").intValue = (int)cues[i].cue;
            element.FindPropertyRelative("stateName").stringValue = cues[i].stateName;
            element.FindPropertyRelative("duration").floatValue = duration;
        }

        if (useFlipRenderer)
        {
            so.FindProperty("flipRenderer").objectReferenceValue = contents.GetComponent<SpriteRenderer>();
        }
        else
        {
            so.FindProperty("counterFlip").objectReferenceValue = contents.transform.Find("Canvas");
        }

        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
        PrefabUtility.UnloadPrefabContents(contents);
    }
}
