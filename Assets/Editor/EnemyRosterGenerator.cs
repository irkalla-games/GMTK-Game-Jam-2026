using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Turns EnemyRoster into prefabs, animation controllers, clips, cards, effects, loot tables and
/// levels - twenty-five bodies from ten art packs that ship nothing in common with each other.
///
/// A menu command rather than hand-authored .asset and .prefab YAML for the same reason every other
/// *Authoring/*Wiring script here is one: GUIDs and fileID cross-references are Unity's business, and
/// a prefab's overhead health-bar Canvas is forty lines of YAML nobody should be writing by hand.
///
/// Repairs rather than skips, per CLAUDE.md. Every asset is reused if it already exists - which is
/// what keeps its GUID, and so every LevelData and deck reference pointing at it - but every field
/// this generator owns is rewritten on every run. The two deliberate exceptions are documented where
/// they happen: CharacterAnimator.facesRightByDefault and any prefab that already exists are never
/// re-copied from the template.
///
/// Assets are created in dependency order and handed down as live objects rather than re-loaded by
/// path, and AssetDatabase.StartAssetEditing is never wrapped around asset creation - only around the
/// texture-importer pivot pass, which loads nothing back inside the block. See CLAUDE.md on why that
/// distinction matters.
///
/// Editor-only, so a plain Tools/compile-check.ps1 run does not cover it - use -IncludeEditor.
/// </summary>
public static class EnemyRosterGenerator
{
    /// The overhead health bar's world size today: the template's root scale (2.4) times its Canvas
    /// child's local scale (1/3). Every body counter-scales its Canvas back to this, so a Pebble and a
    /// Golem carry the same size bar in the same place even though their roots differ threefold.
    private const float OverheadWorldScale = 0.8f;

    /// Alpha above which a pixel counts as artwork when measuring a body. Not zero: several of these
    /// packs carry an almost-invisible halo around the sprite, and one stray pixel of it would drag the
    /// measured bounds out to the edge of the cell and put the pivot back where it started.
    private const byte AlphaFloor = 8;

    private const string ItemDropPath = "Assets/Prefabs/ItemDrops/ItemDrop.prefab";

    private static int warnings;

    [MenuItem("Tools/Enemies/Generate Roster")]
    public static void Generate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Enemy roster: exit Play Mode first - asset edits made in play are not "
                           + "reliable, and a ScriptableObject written now persists into the .asset.");
            return;
        }

        // Docs/EnemySheets.xlsx is the tuning surface now (Tools/EnemySheet) - it reads Health, Actions
        // Per Turn, Brain, Targeting, Loot Table and Deck back OUT of these same prefabs, but this
        // generator still rewrites every one of them from EnemyRoster.cs on every run. Confirmed here
        // rather than silently reverting a night of playtesting balance work.
        if (!EditorUtility.DisplayDialog(
                "Generate Roster",
                "This rewrites Health, Actions Per Turn, Brain, Targeting, Loot Table and every "
                + "prefab's Deck from EnemyRoster.cs - discarding any tuning done since via "
                + "Docs/EnemySheets.xlsx (Tools > Sync Enemies With Sheet) or the Inspector.\n\n"
                + "Only run this to build a NEW body or apply a change you made in EnemyRoster.cs "
                + "itself. To re-balance an existing one, use the enemy sheet instead.",
                "Rewrite from EnemyRoster.cs", "Cancel"))
        {
            return;
        }

        warnings = 0;

        EnsureFolder(EnemyRoster.EnemyPrefabFolder);
        EnsureFolder(EnemyRoster.BossPrefabFolder);
        EnsureFolder(EnemyRoster.SummonEffectFolder);
        EnsureFolder(EnemyRoster.CardFolder);

        Dictionary<string, BodyMetrics> metrics = NormaliseAllPivots();

        Dictionary<string, CardEffect> effects = EnsureMagnitudeEffects();

        // Clips and controllers for everything up front - they depend on nothing but the art.
        Dictionary<string, Rig> rigs = new();

        foreach (CharacterSpec spec in EnemyRoster.Characters)
        {
            Rig rig = BuildRig(spec);

            if (rig != null) { rigs[spec.name] = rig; }
        }

        // Cards split in two passes around the summon effects, because a boss's summon card needs an
        // effect that needs a minion prefab that needs that minion's own cards. Everything that does
        // not name a SummonSpec can be built now; the rest waits until the enemies exist.
        HashSet<string> summonEffectNames = new();

        foreach (SummonSpec summon in EnemyRoster.Summons) { summonEffectNames.Add(summon.name); }

        // Which body carries each card, so every card is filed under the enemy it belongs to.
        Dictionary<string, string> owners = OwnersOfCards();
        Dictionary<string, CardData> cards = new();

        foreach (CardSpec spec in EnemyRoster.Cards)
        {
            if (!NeedsSummon(spec, summonEffectNames))
            {
                cards[spec.name] = EnsureCard(spec, effects, owners);
            }
        }

        foreach (CharacterSpec spec in EnemyRoster.Characters)
        {
            if (!spec.boss) { EnsurePrefab(spec, rigs, cards, loot: null, metrics); }
        }

        foreach (SummonSpec summon in EnemyRoster.Summons)
        {
            CardEffect effect = EnsureSummonEffect(summon);

            if (effect != null) { effects[summon.name] = effect; }
        }

        foreach (CardSpec spec in EnemyRoster.Cards)
        {
            if (NeedsSummon(spec, summonEffectNames))
            {
                cards[spec.name] = EnsureCard(spec, effects, owners);
            }
        }

        Dictionary<string, LootTable> loot = new();

        foreach (LootSpec spec in EnemyRoster.Loot)
        {
            LootTable table = EnsureLootTable(spec);

            if (table != null) { loot[spec.name] = table; }
        }

        foreach (CharacterSpec spec in EnemyRoster.Characters)
        {
            if (spec.boss) { EnsurePrefab(spec, rigs, cards, loot, metrics); }
        }

        foreach (LevelSpec spec in EnemyRoster.Levels) { EnsureLevel(spec); }

        EnsureRuns();

        RescanCardLibraries();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (warnings == 0)
        {
            Debug.Log($"Enemy roster: done, no warnings. {EnemyRoster.Characters.Length} prefabs, "
                      + $"{EnemyRoster.Cards.Length} cards, {EnemyRoster.Levels.Length} levels and "
                      + $"{EnemyRoster.Runs.Length} runs are up to date. Run Tools/Board/3 - Wire "
                      + "Board Objects next, then close Excel and run Tools/Sync With Sheet to get "
                      + "the new cards into the workbook.");
        }
        else
        {
            Debug.LogWarning($"Enemy roster: done with {warnings} warning(s) - read them above. A "
                             + "missing clip or sheet means that cue plays for zero seconds, which "
                             + "reads in game as a body that never leaves its idle pose.");
        }
    }

    private static bool NeedsSummon(CardSpec spec, HashSet<string> summonEffectNames)
    {
        foreach (EntrySpec entry in spec.entries)
        {
            if (summonEffectNames.Contains(entry.effect)) { return true; }
        }

        return false;
    }

    // =============================================================================================
    // Pass 1 - pivots
    // =============================================================================================

    /// <summary>
    /// What one body actually measures, read off the pixels of its idle frames rather than assumed
    /// from the frame canvas.
    ///
    /// The canvas is not the body. These packs draw a 48x48 bandit or a 250x250 wizard inside a cell
    /// with room around it for a swing, a cape or an FX layer, and the artwork is rarely centred in
    /// that cell - the Black Knight's sits at x 0.41. So a flat (0.5, 0) pivot stands every body a
    /// few pixels to one side of its tile and floating above it, and measuring the canvas for scale
    /// makes a body with generous margins come out small.
    /// </summary>
    private sealed class BodyMetrics
    {
        /// Normalised within one frame rect, and the same for every frame of this body - see Measure.
        public Vector2 pivot = new(0.5f, 0f);

        /// Height of the opaque artwork in world units, which is what `height` in the roster is
        /// solving for. Zero when nothing could be measured.
        public float bodyHeight;

        /// The idle frame's cell size, so a sheet sliced differently can be reported rather than
        /// silently given a pivot that means a different pixel.
        public Vector2Int frameSize;
    }

    /// <summary>
    /// Measures every body from its idle frames, then repoints every frame of every sheet it uses to
    /// that one pivot.
    ///
    /// One pivot per *body*, not per frame. Re-centring each frame on its own artwork would look
    /// correct standing still and cancel out every lunge, recoil and wind-up the animations are drawn
    /// with - the attack frames are deliberately offset inside a shared canvas, and that offset is the
    /// animation. Idle is the reference because it is the pose the body rests in. This is the same
    /// call Tools/Fix Enemy Sprite Pivots already made for the two hand-authored enemies.
    ///
    /// Sheet by sheet rather than folder by folder, because these pack folders are not only character
    /// art - Bandits and Hero Knight keep Background.png and EnvironmentTiles.png right beside the
    /// bodies, and repointing a tileset to a body's pivot would wreck their demo scenes.
    ///
    /// Measuring happens before any importer write and the writes are then batched, so nothing is
    /// loaded back inside the StartAssetEditing block - which is the part that would silently fail.
    /// </summary>
    private static Dictionary<string, BodyMetrics> NormaliseAllPivots()
    {
        Dictionary<string, BodyMetrics> metrics = new();
        Dictionary<string, HashSet<string>> sheetsOf = new();

        foreach (CharacterSpec spec in EnemyRoster.Characters)
        {
            if (!string.IsNullOrEmpty(spec.spriteFolder)
                && !AssetDatabase.IsValidFolder(spec.spriteFolder))
            {
                Warn($"{spec.name}: sprite folder '{spec.spriteFolder}' does not exist.");
                continue;
            }

            if (spec.states == null || spec.states.Length == 0) { continue; }

            HashSet<string> sheets = new();

            foreach (StateSpec state in spec.states)
            {
                string sheet = SheetFor(spec, state);

                if (sheet != null) { sheets.Add(sheet); }
            }

            sheetsOf[spec.name] = sheets;
            metrics[spec.name] = Measure(spec);
        }

        int changed = 0;
        int total = 0;

        AssetDatabase.StartAssetEditing();

        try
        {
            foreach (CharacterSpec spec in EnemyRoster.Characters)
            {
                if (!sheetsOf.TryGetValue(spec.name, out HashSet<string> sheets)) { continue; }

                BodyMetrics body = metrics[spec.name];

                foreach (string sheet in sheets)
                {
                    total++;

                    // A pivot is a fraction of a frame, so it only means the same pixel on a sheet
                    // sliced the same way. Every pack here is uniform per body, and a pack that
                    // stops being would otherwise put one state a few pixels off with no clue why.
                    WarnOnFrameMismatch(spec, sheet, body);

                    if (ApplyPivot(sheet, body.pivot)) { changed++; }
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        Debug.Log($"Enemy roster: {changed} of {total} sheet(s) repointed; the rest already matched "
                  + "their body's measured pivot.");

        return metrics;
    }

    /// <summary>
    /// Reports a sheet sliced to a different cell size than the one the body was measured on.
    ///
    /// A pivot is a fraction of a frame, so 0.44 means a different pixel on a 126-wide cell than on a
    /// 64-wide one. Every pack in this roster is uniform per body, so this should never fire - but a
    /// pack that stops being uniform would otherwise put one state a few pixels off with no clue why.
    /// </summary>
    private static void WarnOnFrameMismatch(CharacterSpec spec, string sheet, BodyMetrics body)
    {
        if (body.frameSize == Vector2Int.zero) { return; }

        Sprite[] frames = FramesOf(sheet);

        if (frames.Length == 0) { return; }

        Vector2Int size = new((int)frames[0].rect.width, (int)frames[0].rect.height);

        if (size == body.frameSize) { return; }

        Warn($"{spec.name}: {sheet} is sliced {size.x}x{size.y} but the body was measured on "
             + $"{body.frameSize.x}x{body.frameSize.y} frames, so its pivot lands on a different pixel "
             + "there. Give that state its own body or re-slice the sheet to match.");
    }

    /// The sheet one state reads its frames from, whichever way that state is sourced.
    private static string SheetFor(CharacterSpec spec, StateSpec state) =>
        state.reuse ? SheetBehind(spec, state) : $"{spec.spriteFolder}/{state.source}.png";

    /// <summary>
    /// The texture behind a clip the pack ships, found through that clip's first keyframe.
    ///
    /// A reused clip names no sheet of its own - it references sprites, and for the mega-sheet packs
    /// those sprites are 324 slices of one file whose name says nothing about which body uses it. This
    /// is silent on failure on purpose: BuildRig reports a missing clip properly a moment later, and
    /// warning twice about one typo helps nobody.
    /// </summary>
    private static string SheetBehind(CharacterSpec spec, StateSpec state)
    {
        if (string.IsNullOrEmpty(spec.clipFolder)) { return null; }

        AnimationClip clip =
            AssetDatabase.LoadAssetAtPath<AnimationClip>($"{spec.clipFolder}/{state.source}.anim");

        if (clip == null) { return null; }

        Sprite frame = FirstFrame(clip);

        return frame == null ? null : AssetDatabase.GetAssetPath(frame);
    }

    /// <summary>
    /// Unions the opaque bounds of a body's idle frames and turns them into a pivot and a height.
    ///
    /// Only the idle frames, and only the ones the idle actually uses: the Black Knight's idle is
    /// eight slices of a 324-frame mega-sheet, and measuring all 324 would union in every other pose
    /// that pack ships - including ones drawn deliberately off-canvas.
    /// </summary>
    private static BodyMetrics Measure(CharacterSpec spec)
    {
        BodyMetrics body = new();

        StateSpec idle = spec.states[0];
        Sprite[] frames = IdleFrames(spec, idle);

        if (frames.Length == 0)
        {
            Warn($"{spec.name}: no idle frames to measure, so it keeps a flat bottom-centre pivot and "
                 + "is scaled from its frame canvas.");
            return body;
        }

        string sheet = AssetDatabase.GetAssetPath(frames[0]);
        Color32[] pixels = DecodePixels(sheet, out int textureWidth, out int textureHeight);

        Rect rect = frames[0].rect;
        body.frameSize = new Vector2Int((int)rect.width, (int)rect.height);

        if (pixels == null || textureWidth <= 0)
        {
            Warn($"{spec.name}: could not read the pixels of {sheet}, so it keeps a flat "
                 + "bottom-centre pivot.");
            return body;
        }

        // Bounds relative to a frame's own corner, unioned across the idle frames.
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

        foreach (Sprite frame in frames)
        {
            Rect r = frame.rect;

            for (int y = 0; y < (int)r.height; y++)
            {
                int row = ((int)r.y + y) * textureWidth;

                for (int x = 0; x < (int)r.width; x++)
                {
                    int index = row + (int)r.x + x;

                    if (index < 0 || index >= pixels.Length) { continue; }

                    // Above a threshold rather than above zero: several of these packs carry a halo of
                    // all-but-invisible pixels around the artwork, and a single stray one would drag
                    // the bounds - and so the pivot - out to the edge of the cell.
                    if (pixels[index].a <= AlphaFloor) { continue; }

                    if (x < minX) { minX = x; }
                    if (x > maxX) { maxX = x; }
                    if (y < minY) { minY = y; }
                    if (y > maxY) { maxY = y; }
                }
            }
        }

        if (minX > maxX)
        {
            Warn($"{spec.name}: every idle frame in {sheet} is transparent, so it keeps a flat "
                 + "bottom-centre pivot.");
            return body;
        }

        float width = rect.width;
        float height = rect.height;

        // +1 because max is the last opaque pixel's index, not the edge past it.
        float centreX = (minX + maxX + 1) * 0.5f;

        // Lifted off the tile by a fraction of the body's own height, for the ones that fly. A pivot
        // below the frame is legal - alignment Custom does not clamp to 0..1.
        float lift = spec.hover * (maxY - minY + 1);

        body.pivot = new Vector2(centreX / width, (minY - lift) / height);
        body.bodyHeight = (maxY - minY + 1) / frames[0].pixelsPerUnit;

        return body;
    }

    /// <summary>
    /// The frames the idle state actually animates - every sub-sprite of its sheet when the clip is
    /// built from one, or exactly the keyframed sprites when the pack's own clip is reused.
    /// </summary>
    private static Sprite[] IdleFrames(CharacterSpec spec, StateSpec idle)
    {
        if (!idle.reuse) { return FramesOf($"{spec.spriteFolder}/{idle.source}.png"); }

        if (string.IsNullOrEmpty(spec.clipFolder)) { return System.Array.Empty<Sprite>(); }

        AnimationClip clip =
            AssetDatabase.LoadAssetAtPath<AnimationClip>($"{spec.clipFolder}/{idle.source}.anim");

        if (clip == null) { return System.Array.Empty<Sprite>(); }

        ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(
            clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"));

        if (keys == null) { return System.Array.Empty<Sprite>(); }

        List<Sprite> frames = new();

        foreach (ObjectReferenceKeyframe key in keys)
        {
            if (key.value is Sprite sprite && !frames.Contains(sprite)) { frames.Add(sprite); }
        }

        return frames.ToArray();
    }

    /// <summary>
    /// The source PNG's pixels, decoded straight from the file.
    ///
    /// Decoded rather than read off the imported Texture2D so the importer is never touched: a texture
    /// asset is not readable by default, and flipping isReadable to sample it would mean two extra
    /// reimports of every sheet and an importer left in a state this tool has no business owning.
    /// LoadImage also gives the original resolution, which is the space sprite rects are expressed in -
    /// the imported texture may have been downscaled by a max-size setting.
    /// </summary>
    private static Color32[] DecodePixels(string assetPath, out int width, out int height)
    {
        width = 0;
        height = 0;

        string root = Path.GetDirectoryName(Application.dataPath);
        string full = string.IsNullOrEmpty(root) ? assetPath : Path.Combine(root, assetPath);

        if (!File.Exists(full)) { return null; }

        Texture2D temp = new(2, 2, TextureFormat.RGBA32, false);

        try
        {
            if (!temp.LoadImage(File.ReadAllBytes(full))) { return null; }

            width = temp.width;
            height = temp.height;

            return temp.GetPixels32();
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }

    /// <summary>
    /// Points every frame of one sheet at `pivot`, as a Custom alignment.
    ///
    /// Through ISpriteEditorDataProvider rather than TextureImporter.spritesheet: that older property
    /// is not merely deprecated in Unity 6, its support has been *removed*, so writing through it
    /// silently changes nothing. The whole-texture TextureImporterSettings write still matters for a
    /// Single-mode texture, which has no sprite rects to walk.
    /// </summary>
    private static bool ApplyPivot(string path, Vector2 pivot)
    {
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer) { return false; }

        bool changed = false;

        TextureImporterSettings settings = new();
        importer.ReadTextureSettings(settings);

        if (settings.spriteAlignment != (int)SpriteAlignment.Custom
            || settings.spritePivot != pivot)
        {
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            importer.SetTextureSettings(settings);
            changed = true;
        }

        SpriteDataProviderFactories factories = new();
        factories.Init();

        ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);

        if (provider != null)
        {
            provider.InitSpriteEditorDataProvider();

            SpriteRect[] rects = provider.GetSpriteRects();
            bool rectsChanged = false;

            // SpriteRect is a class, so these edits land on the objects the provider handed back.
            foreach (SpriteRect rect in rects)
            {
                if (rect.alignment == SpriteAlignment.Custom && rect.pivot == pivot) { continue; }

                rect.alignment = SpriteAlignment.Custom;
                rect.pivot = pivot;
                rectsChanged = true;
            }

            if (rectsChanged)
            {
                provider.SetSpriteRects(rects);
                provider.Apply();
                changed = true;
            }
        }

        if (!changed) { return false; }

        importer.SaveAndReimport();

        return true;
    }

    // =============================================================================================
    // Pass 2 - clips and controllers
    // =============================================================================================

    /// One body's animation assets, handed to the prefab pass as live objects.
    private sealed class Rig
    {
        public AnimatorController controller;
        public string idleState;
        public Sprite idleSprite;

        /// Resolved clip lengths, keyed by state name. Read straight off the AnimationClip rather
        /// than looked up by name later - see EnsurePrefab.
        public readonly Dictionary<string, float> durations = new();
    }

    private static Rig BuildRig(CharacterSpec spec)
    {
        if (spec.states == null || spec.states.Length == 0)
        {
            Warn($"{spec.name} has no states authored.");
            return null;
        }

        string folder = $"{EnemyRoster.AnimationFolder}/{spec.name}";
        EnsureFolder(folder);

        Rig rig = new() { idleState = spec.states[0].state };

        List<(string state, AnimationClip clip)> motions = new();

        foreach (StateSpec state in spec.states)
        {
            AnimationClip clip = state.reuse
                ? ReuseClip(spec, state)
                : BuildClip(spec, state, folder);

            if (clip == null) { continue; }

            motions.Add((state.state, clip));
            rig.durations[state.state] = clip.length;
        }

        if (motions.Count == 0)
        {
            Warn($"{spec.name} resolved no clips at all - no controller was built.");
            return null;
        }

        rig.controller = BuildController(spec, folder, motions, rig.idleState);
        rig.idleSprite = FirstFrame(motions[0].clip);

        if (rig.idleSprite == null)
        {
            Warn($"{spec.name}: the idle clip '{motions[0].clip.name}' has no sprite keyframes, so the "
                 + "prefab keeps whatever sprite the template had.");
        }

        return rig;
    }

    /// <summary>
    /// References a clip the pack already ships. Every such clip in every pack used here binds
    /// m_Sprite on path "" - the object the Animator is on - which is exactly where our SpriteRenderer
    /// sits, so it drops straight onto our prefab with nothing to rewrite.
    /// </summary>
    private static AnimationClip ReuseClip(CharacterSpec spec, StateSpec state)
    {
        if (string.IsNullOrEmpty(spec.clipFolder))
        {
            Warn($"{spec.name} state '{state.state}' asks to reuse '{state.source}' but the spec sets "
                 + "no clipFolder.");
            return null;
        }

        string path = $"{spec.clipFolder}/{state.source}.anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

        if (clip == null) { Warn($"{spec.name}: no clip at {path}."); }

        return clip;
    }

    /// Builds a sprite-swap clip from one sliced sheet's sub-sprites.
    private static AnimationClip BuildClip(CharacterSpec spec, StateSpec state, string folder)
    {
        string sheet = $"{spec.spriteFolder}/{state.source}.png";
        Sprite[] frames = FramesOf(sheet);

        if (frames.Length == 0)
        {
            Warn($"{spec.name}: no sprites found in {sheet} - is it sliced?");
            return null;
        }

        string path = $"{folder}/{spec.name}_{state.state}.anim";

        // Reused rather than recreated so the clip keeps its GUID and any controller already pointing
        // at it survives; every field below is rewritten regardless.
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        bool created = clip == null;

        if (created) { clip = new AnimationClip(); }

        clip.frameRate = state.fps;

        ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[frames.Length];

        for (int i = 0; i < frames.Length; i++)
        {
            keys[i] = new ObjectReferenceKeyframe { time = i / state.fps, value = frames[i] };
        }

        AnimationUtility.SetObjectReferenceCurve(
            clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = state.loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        if (created) { AssetDatabase.CreateAsset(clip, path); }
        else { EditorUtility.SetDirty(clip); }

        return clip;
    }

    /// <summary>
    /// Every sub-sprite of one sliced sheet, in frame order.
    ///
    /// Sorted explicitly: LoadAllAssetsAtPath makes no ordering promise, and a sheet whose frames came
    /// back shuffled would animate as noise. The trailing index is the only thing the packs agree on -
    /// the names around it range from "Idle_3" to "Black_Knight_All_Frame_With_Border_147".
    /// </summary>
    private static Sprite[] FramesOf(string sheetPath)
    {
        List<Sprite> sprites = new();

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(sheetPath))
        {
            if (asset is Sprite sprite) { sprites.Add(sprite); }
        }

        sprites.Sort((a, b) => FrameIndex(a.name).CompareTo(FrameIndex(b.name)));

        return sprites.ToArray();
    }

    private static int FrameIndex(string name)
    {
        Match match = Regex.Match(name, @"(\d+)$");

        return match.Success && int.TryParse(match.Groups[1].Value, out int index) ? index : 0;
    }

    /// <summary>
    /// One state per motion, named in our own vocabulary rather than after the clip asset - which is
    /// the whole point of the indirection. A reused pack clip called "BK_heavy_attack_1" becomes the
    /// state "MeleeAttack", so CharacterAnimator's cue table never learns which pack a body came from.
    ///
    /// No parameters and no transitions: Animator.Play(state, 0, 0f) reaches a state directly, which
    /// is all CharacterAnimator ever does.
    /// </summary>
    private static AnimatorController BuildController(
        CharacterSpec spec, string folder, List<(string state, AnimationClip clip)> motions,
        string idleState)
    {
        string path = $"{folder}/{spec.name}.controller";

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);

        // Kept rather than deleted and remade so the prefab's Animator reference survives a re-run.
        if (controller == null) { controller = AnimatorController.CreateAnimatorControllerAtPath(path); }

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        foreach (ChildAnimatorState child in machine.states) { machine.RemoveState(child.state); }

        AnimatorState idle = null;

        foreach ((string state, AnimationClip clip) in motions)
        {
            AnimatorState added = machine.AddState(state);
            added.motion = clip;
            added.writeDefaultValues = false;

            if (state == idleState) { idle = added; }
        }

        if (idle != null) { machine.defaultState = idle; }

        EditorUtility.SetDirty(controller);

        return controller;
    }

    private static Sprite FirstFrame(AnimationClip clip)
    {
        ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(
            clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"));

        return keys != null && keys.Length > 0 ? keys[0].value as Sprite : null;
    }

    // =============================================================================================
    // Pass 3 - effects
    // =============================================================================================

    /// <summary>
    /// Mints the magnitude-named effects this roster needs and does not already have (Damage 6, Heal
    /// 3, ...), and returns them keyed by name alongside nothing else - every other effect these cards
    /// use already exists under Assets/Data/EffectData and is resolved by name at card-build time.
    /// </summary>
    private static Dictionary<string, CardEffect> EnsureMagnitudeEffects()
    {
        Dictionary<string, CardEffect> built = new();

        foreach (EffectSpec spec in EnemyRoster.Effects)
        {
            string folder = spec.kind switch
            {
                EffectKind.Damage => "Assets/Data/EffectData/Damage",
                EffectKind.Heal => "Assets/Data/EffectData/Heal",
                EffectKind.Shield => "Assets/Data/EffectData/Shield",
                _ => "Assets/Data/EffectData/Block",
            };

            EnsureFolder(folder);

            string path = $"{folder}/{spec.name}.asset";
            CardEffect effect = AssetDatabase.LoadAssetAtPath<CardEffect>(path);

            if (effect == null)
            {
                effect = spec.kind switch
                {
                    EffectKind.Damage => ScriptableObject.CreateInstance<DamageEffect>(),
                    EffectKind.Heal => ScriptableObject.CreateInstance<HealEffect>(),
                    EffectKind.Shield => ScriptableObject.CreateInstance<ShieldEffect>(),
                    _ => ScriptableObject.CreateInstance<BlockEffect>(),
                };

                AssetDatabase.CreateAsset(effect, path);
            }

            SerializedObject so = new(effect);

            string field = spec.kind switch
            {
                EffectKind.Damage => "damageAmount",
                EffectKind.Heal => "healAmount",
                EffectKind.Shield => "shieldAmount",
                _ => "blockCount",
            };

            so.FindProperty(field).intValue = spec.amount;

            // A Heal or Shield an enemy plays on itself is an Ally-audience effect aimed at Source, so
            // these stay at their defaults; only Damage needs saying, and only to say "no friendly
            // fire" explicitly rather than relying on the serialized default.
            if (spec.kind == EffectKind.Damage) { so.FindProperty("canHitAllies").boolValue = false; }
            if (spec.kind == EffectKind.Heal) { so.FindProperty("canHitEnemies").boolValue = false; }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(effect);

            built[spec.name] = effect;
        }

        return built;
    }

    private static CardEffect EnsureSummonEffect(SummonSpec spec)
    {
        string prefabPath = PrefabPathOf(spec.prefab);

        if (prefabPath == null)
        {
            Warn($"summon '{spec.name}' names '{spec.prefab}', which is not in the roster.");
            return null;
        }

        GameObject minion = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        if (minion == null)
        {
            Warn($"summon '{spec.name}' cannot be built - {prefabPath} does not exist yet.");
            return null;
        }

        string path = $"{EnemyRoster.SummonEffectFolder}/{spec.name}.asset";
        SummonEffect effect = AssetDatabase.LoadAssetAtPath<SummonEffect>(path);

        if (effect == null)
        {
            effect = ScriptableObject.CreateInstance<SummonEffect>();
            AssetDatabase.CreateAsset(effect, path);
        }

        SerializedObject so = new(effect);
        so.FindProperty("summonedObject").objectReferenceValue = minion;
        so.FindProperty("lifetimeTurns").intValue = spec.lifetimeTurns;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(effect);

        return effect;
    }

    // =============================================================================================
    // Pass 4 - cards
    // =============================================================================================

    /// <summary>
    /// Which body's deck holds each card, for the cards exactly one body holds.
    ///
    /// A card in more than one deck is left out rather than assigned to whichever body was read first
    /// - it has no single owner, so it belongs at the root of the enemy card folder. MoveInnate is the
    /// standing example: every one of the 25 bodies carries it.
    /// </summary>
    private static Dictionary<string, string> OwnersOfCards()
    {
        Dictionary<string, string> owner = new();
        HashSet<string> shared = new();

        foreach (CharacterSpec spec in EnemyRoster.Characters)
        {
            if (spec.deck == null) { continue; }

            foreach ((string card, int _) in spec.deck)
            {
                if (owner.TryGetValue(card, out string first) && first != spec.name) { shared.Add(card); }
                else { owner[card] = spec.name; }
            }
        }

        foreach (string card in shared) { owner.Remove(card); }

        return owner;
    }

    /// <summary>
    /// Creates or repairs one card, filed under the body that carries it.
    ///
    /// A card that already exists is *moved* rather than recreated - through AssetDatabase.MoveAsset,
    /// which carries its GUID with it, so every deck, loot table and workbook row still pointing at it
    /// keeps pointing at it. Recreating at the new path would mint a fresh GUID and silently empty
    /// every deck holding the old one.
    ///
    /// Moved from wherever it currently sits rather than from an assumed old path, so re-owning a card
    /// - moving it to a different body's deck - files it correctly on the next run instead of leaving
    /// it under the body that used to carry it.
    /// </summary>
    private static CardData EnsureCard(
        CardSpec spec, Dictionary<string, CardEffect> known, Dictionary<string, string> owners)
    {
        string folder = owners.TryGetValue(spec.name, out string body)
            ? $"{EnemyRoster.CardFolder}/{body}"
            : EnemyRoster.CardFolder;

        EnsureFolder(folder);

        string path = $"{folder}/{spec.name}.asset";
        CardData card = FindByName<CardData>(spec.name, EnemyRoster.CardFolder);

        if (card != null)
        {
            string current = AssetDatabase.GetAssetPath(card);

            if (current != path)
            {
                string error = AssetDatabase.MoveAsset(current, path);

                if (string.IsNullOrEmpty(error))
                {
                    Debug.Log($"Enemy roster: moved {spec.name} to {folder}.");
                }
                else
                {
                    Warn($"could not move {current} to {path}: {error}. It keeps working where it is.");
                    path = current;
                }
            }
        }
        else
        {
            card = ScriptableObject.CreateInstance<CardData>();
            AssetDatabase.CreateAsset(card, path);
        }

        SerializedObject so = new(card);

        so.FindProperty("<cardName>k__BackingField").stringValue = spec.cardName;
        so.FindProperty("<cost>k__BackingField").intValue = spec.cost;
        so.FindProperty("<requiredClass>k__BackingField").intValue = (int)CharacterClass.Any;
        so.FindProperty("<description>k__BackingField").stringValue = spec.description;

        // NotOffered is what keeps an enemy card out of every LootTable and reward panel - the same
        // gate EnemySlash and EnemyArrow already sit behind.
        so.FindProperty("<rarity>k__BackingField").intValue = (int)Rarity.NotOffered;

        WriteRange(so.FindProperty("<range>k__BackingField"), spec.shape, spec.min, spec.max);

        SerializedProperty tags = so.FindProperty("<tags>k__BackingField");
        tags.arraySize = 1;
        tags.GetArrayElementAtIndex(0).intValue = (int)spec.tag;

        // Left empty on purpose: `effects` is migration input for CardEffectEntryMigration only, which
        // skips any card whose effectEntries is already populated. effectEntries below is what Card
        // actually resolves.
        so.FindProperty("<effects>k__BackingField").arraySize = 0;

        SerializedProperty entries = so.FindProperty("<effectEntries>k__BackingField");
        entries.arraySize = spec.entries.Length;

        for (int i = 0; i < spec.entries.Length; i++)
        {
            EntrySpec entry = spec.entries[i];
            SerializedProperty element = entries.GetArrayElementAtIndex(i);

            element.FindPropertyRelative("effect").objectReferenceValue = ResolveEffect(spec, entry, known);
            element.FindPropertyRelative("aimsAt").intValue = (int)entry.aim;
            element.FindPropertyRelative("amountDelta").intValue = 0;
            element.FindPropertyRelative("amountPercent").intValue = 0;

            SerializedProperty area = element.FindPropertyRelative("area");
            area.FindPropertyRelative("kind").intValue =
                entry.radius > 0 ? (int)AreaKind.Radius : (int)AreaKind.Single;
            area.FindPropertyRelative("pattern").objectReferenceValue = null;

            WriteRange(area.FindPropertyRelative("radius"),
                entry.radius > 0 ? RangeShape.Chebyshev : RangeShape.Anywhere, 0, entry.radius);
        }

        // Cooldown is the only pacing these cards need. Written as intValue rather than
        // enumValueIndex: the latter stores the member's position in the declaration, which only
        // equals its value while CardKeywordType stays contiguous.
        SerializedProperty keywords = so.FindProperty("<keywords>k__BackingField");
        keywords.arraySize = spec.cooldown > 0 ? 1 : 0;

        if (spec.cooldown > 0)
        {
            SerializedProperty keyword = keywords.GetArrayElementAtIndex(0);
            keyword.FindPropertyRelative("type").intValue = (int)CardKeywordType.Cooldown;
            keyword.FindPropertyRelative("magnitude").intValue = spec.cooldown;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(card);

        return card;
    }

    private static void WriteRange(SerializedProperty range, RangeShape shape, int min, int max)
    {
        range.FindPropertyRelative("shape").intValue = (int)shape;
        range.FindPropertyRelative("minDistance").intValue = min;
        range.FindPropertyRelative("maxDistance").intValue = max;
    }

    private static CardEffect ResolveEffect(
        CardSpec card, EntrySpec entry, Dictionary<string, CardEffect> known)
    {
        if (known.TryGetValue(entry.effect, out CardEffect built)) { return built; }

        CardEffect found = FindByName<CardEffect>(entry.effect, "Assets/Data/EffectData");

        if (found == null)
        {
            Warn($"card '{card.name}' wants effect '{entry.effect}', which is not under "
                 + "Assets/Data/EffectData and is not one this generator mints - that entry is empty.");
        }

        return found;
    }

    // =============================================================================================
    // Pass 5 - prefabs
    // =============================================================================================

    private static void EnsurePrefab(
        CharacterSpec spec, Dictionary<string, Rig> rigs, Dictionary<string, CardData> cards,
        Dictionary<string, LootTable> loot, Dictionary<string, BodyMetrics> metrics)
    {
        string path = PrefabPathOf(spec.name);

        // Copied from the template only the first time. A re-run edits the prefab in place instead,
        // which is what keeps its GUID - and so every LevelData placement, SummonEffect and deck
        // reference pointing at it - intact. Every field this generator owns is still rewritten.
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            if (!AssetDatabase.CopyAsset(EnemyRoster.MeleeTemplate, path))
            {
                Warn($"could not copy {EnemyRoster.MeleeTemplate} -> {path}.");
                return;
            }
        }

        rigs.TryGetValue(spec.name, out Rig rig);

        using (PrefabUtility.EditPrefabContentsScope scope = new(path))
        {
            GameObject root = scope.prefabContentsRoot;
            root.name = spec.name;

            // Where it stands is GridManager.PlaceCharacter's business, so the authored position is
            // never read - zeroed only so the prefab preview is not floating.
            root.transform.localPosition = Vector3.zero;

            SpriteRenderer renderer = root.GetComponent<SpriteRenderer>();

            if (renderer == null)
            {
                Warn($"{path} has no SpriteRenderer - the template must have changed.");
                return;
            }

            if (rig != null && rig.idleSprite != null) { renderer.sprite = rig.idleSprite; }

            metrics.TryGetValue(spec.name, out BodyMetrics body);

            ApplyScale(root, renderer, spec, body);
            ApplyCharacter(root, spec, cards, loot, path);
            ApplyAnimation(root, renderer, spec, rig, path);
        }
    }

    /// <summary>
    /// Solves the root scale for the height this body should read at, rather than authoring 25 scale
    /// numbers by hand: these packs run from 48px at 32 PPU to 250px at 100 PPU, so the same number
    /// means something different in every one of them.
    ///
    /// Measured against the *artwork*, not the frame canvas. A 250x250 Evil Wizard cell is mostly
    /// empty space kept for its spell FX, and dividing by the canvas would render the wizard at a
    /// fraction of the height asked for while a tightly-cropped bandit came out full size.
    ///
    /// The overhead Canvas is then counter-scaled, because it hangs off the root and would otherwise
    /// inherit that same factor - a Pebble's health bar would come out a third the size of a Golem's.
    /// </summary>
    private static void ApplyScale(
        GameObject root, SpriteRenderer renderer, CharacterSpec spec, BodyMetrics body)
    {
        float measured = body != null ? body.bodyHeight : 0f;

        // Falls back to the frame canvas only when the pixels could not be measured at all, which
        // Measure has already warned about.
        if (measured <= 0f)
        {
            measured = renderer.sprite != null ? renderer.sprite.bounds.size.y : 0f;
        }

        if (measured <= 0f)
        {
            Warn($"{spec.name} has nothing to measure - scale left as the template's.");
            return;
        }

        float scale = spec.height / measured;
        root.transform.localScale = new Vector3(scale, scale, 1f);

        Transform canvas = root.transform.Find("Canvas");

        if (canvas == null)
        {
            Warn($"{spec.name} has no Canvas child - its health bar will scale with the body.");
            return;
        }

        float counter = OverheadWorldScale / scale;
        canvas.localScale = new Vector3(counter, counter, 1f);
    }

    private static void ApplyCharacter(
        GameObject root, CharacterSpec spec, Dictionary<string, CardData> cards,
        Dictionary<string, LootTable> loot, string path)
    {
        Character character = root.GetComponent<Character>();

        if (character == null)
        {
            Warn($"{path} has no Character component - the template must have changed.");
            return;
        }

        SerializedObject so = new(character);

        so.FindProperty("maxHealth").intValue = spec.maxHealth;
        so.FindProperty("<Health>k__BackingField").intValue = spec.maxHealth;
        so.FindProperty("playableCharacter").intValue = (int)PlayableCharacter.Enemy;
        so.FindProperty("displayName").stringValue = spec.displayName;
        so.FindProperty("actionPoints").intValue = spec.actionPoints;
        so.FindProperty("brain").intValue = (int)spec.brain;
        so.FindProperty("targetingPattern").objectReferenceValue = spec.targeting == null
            ? null
            : FindByName<TargetingPattern>(spec.targeting, "Assets/Data/TargetingData");

        so.FindProperty("itemDropPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(ItemDropPath);

        LootTable table = null;

        if (spec.loot != null && loot != null) { loot.TryGetValue(spec.loot, out table); }
        if (table == null && spec.loot != null)
        {
            table = FindByName<LootTable>(spec.loot, EnemyRoster.LootFolder);
        }

        // Null is meaningful, not a failure: it inherits the level's own LootTable.
        so.FindProperty("lootTable").objectReferenceValue = table;

        if (spec.loot != null && table == null)
        {
            Warn($"{spec.name} names loot table '{spec.loot}', which was not found.");
        }

        SerializedProperty deck = so.FindProperty("deck");
        List<CardData> built = new();

        foreach ((string name, int copies) in spec.deck)
        {
            CardData card = cards.TryGetValue(name, out CardData made)
                ? made
                : FindByName<CardData>(name, "Assets/Data/CardData");

            if (card == null)
            {
                Warn($"{spec.name} wants card '{name}', which was not found - it is missing from the "
                     + "deck.");
                continue;
            }

            for (int i = 0; i < copies; i++) { built.Add(card); }
        }

        deck.arraySize = built.Count;

        for (int i = 0; i < built.Count; i++)
        {
            deck.GetArrayElementAtIndex(i).objectReferenceValue = built[i];
        }

        so.ApplyModifiedProperties();
    }

    private static void ApplyAnimation(
        GameObject root, SpriteRenderer renderer, CharacterSpec spec, Rig rig, string path)
    {
        Animator animator = root.GetComponent<Animator>();

        if (animator == null) { animator = root.AddComponent<Animator>(); }

        if (rig != null && rig.controller != null) { animator.runtimeAnimatorController = rig.controller; }

        CharacterAnimator body = root.GetComponent<CharacterAnimator>();

        if (body == null) { body = root.AddComponent<CharacterAnimator>(); }

        SerializedObject so = new(body);

        so.FindProperty("animator").objectReferenceValue = animator;
        so.FindProperty("idleStateName").stringValue = rig != null ? rig.idleState : "Idle";

        // A single-sprite body mirrors through SpriteRenderer.flipX, which leaves the transform - and
        // so the overhead Canvas hanging off it - alone. counterFlip is the heroes' strategy and must
        // stay empty here, or SetFacing would take the wrong branch.
        so.FindProperty("flipRenderer").objectReferenceValue = renderer;
        so.FindProperty("counterFlip").objectReferenceValue = null;
        so.FindProperty("muzzle").objectReferenceValue = spec.ranged ? EnsureMuzzle(root, spec) : null;

        // facesRightByDefault is deliberately never written. The two packs' art does not agree on
        // which way a body faces, and a per-character correction made in the Inspector has to survive
        // a re-run - the existing Tools/Setup Character Animations makes the same promise.

        SerializedProperty cues = so.FindProperty("cues");
        cues.arraySize = 0;

        if (rig != null)
        {
            int index = 0;

            foreach (StateSpec state in spec.states)
            {
                if (state.cue == AnimationCue.None) { continue; }
                if (!rig.durations.TryGetValue(state.state, out float duration)) { continue; }

                if (duration <= 0f)
                {
                    Warn($"{spec.name}: clip for state '{state.state}' is zero seconds long, so that "
                         + "cue returns to idle on the frame it starts.");
                }

                cues.arraySize = index + 1;
                SerializedProperty row = cues.GetArrayElementAtIndex(index);

                // intValue, not enumValueIndex - the latter writes the member's position in the
                // declaration, which stops matching the moment AnimationCue gets a gap.
                row.FindPropertyRelative("cue").intValue = (int)state.cue;
                row.FindPropertyRelative("stateName").stringValue = state.state;

                // Taken from the AnimationClip this pass already holds, rather than searched for by
                // name. CharacterAnimator's own fallback matches a clip called exactly the state or
                // ending "_<state>", and a reused pack clip ("BK_heavy_attack_1" driving the state
                // "MeleeAttack") matches neither - every reused cue would silently hold for 0 seconds.
                row.FindPropertyRelative("duration").floatValue = duration;

                index++;
            }
        }

        so.ApplyModifiedProperties();
    }

    /// Where a projectile launches from, for the ranged bodies. Roughly mid-chest and slightly
    /// forward, in the root's own local space - the sprite is pivoted at its feet, so the body spans
    /// 0..height going up from the origin.
    private static Transform EnsureMuzzle(GameObject root, CharacterSpec spec)
    {
        Transform muzzle = root.transform.Find("RangedAttackAnchor");

        if (muzzle == null)
        {
            GameObject anchor = new("RangedAttackAnchor");
            anchor.transform.SetParent(root.transform, false);
            anchor.layer = root.layer;
            muzzle = anchor.transform;
        }

        SpriteRenderer renderer = root.GetComponent<SpriteRenderer>();
        float height = renderer != null && renderer.sprite != null ? renderer.sprite.bounds.size.y : 1f;

        muzzle.localPosition = new Vector3(height * 0.18f, height * 0.55f, 0f);

        return muzzle;
    }

    // =============================================================================================
    // Pass 6 - loot and levels
    // =============================================================================================

    /// <summary>
    /// A boss's own drop: better tiers than the level default, plus one card it always gives up.
    ///
    /// guaranteedCards ignores both the rarity roll and excludeFromRewards, so the card named here has
    /// to be a real player card - a NotOffered enemy card put in this list would genuinely be offered.
    /// </summary>
    private static LootTable EnsureLootTable(LootSpec spec)
    {
        EnsureFolder(EnemyRoster.LootFolder);

        string path = $"{EnemyRoster.LootFolder}/{spec.name}.asset";
        LootTable table = AssetDatabase.LoadAssetAtPath<LootTable>(path);

        if (table == null)
        {
            table = ScriptableObject.CreateInstance<LootTable>();
            AssetDatabase.CreateAsset(table, path);
        }

        SerializedObject so = new(table);

        SerializedProperty tiers = so.FindProperty("tierWeights");
        tiers.arraySize = 3;
        WriteTier(tiers.GetArrayElementAtIndex(0), Rarity.Uncommon, 45);
        WriteTier(tiers.GetArrayElementAtIndex(1), Rarity.Rare, 40);
        WriteTier(tiers.GetArrayElementAtIndex(2), Rarity.Legendary, 15);

        so.FindProperty("tagWeights").arraySize = 0;
        so.FindProperty("cardWeights").arraySize = 0;
        so.FindProperty("excludedCards").arraySize = 0;
        so.FindProperty("choiceCount").intValue = 3;
        so.FindProperty("equipmentChance").floatValue = 0f;

        CardData signature = FindByName<CardData>(spec.guaranteed, "Assets/Data/CardData");

        SerializedProperty guaranteed = so.FindProperty("guaranteedCards");
        guaranteed.arraySize = signature != null ? 1 : 0;

        if (signature != null)
        {
            guaranteed.GetArrayElementAtIndex(0).objectReferenceValue = signature;
        }
        else
        {
            Warn($"loot table '{spec.name}' names card '{spec.guaranteed}', which was not found - it "
                 + "drops from the rarity roll alone.");
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(table);

        return table;
    }

    private static void WriteTier(SerializedProperty tier, Rarity rarity, int weight)
    {
        tier.FindPropertyRelative("rarity").intValue = (int)rarity;
        tier.FindPropertyRelative("weight").intValue = weight;
    }

    /// <summary>
    /// One level, built the way TutorialContentGenerator builds Tutorial.asset - through
    /// SerializedObject, because EnemyPlacement is a plain serializable struct with no public API.
    ///
    /// Cells arrive one-based, the way the Inspector shows them, and are stored one lower. That is
    /// what [OneBasedCell] does on the way in and out; the asset on disk never sees the shifted number.
    /// </summary>
    private static void EnsureLevel(LevelSpec spec)
    {
        EnsureFolder(EnemyRoster.LevelFolder);

        string path = $"{EnemyRoster.LevelFolder}/{spec.name}.asset";
        LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(path);

        if (level == null)
        {
            level = ScriptableObject.CreateInstance<LevelData>();
            AssetDatabase.CreateAsset(level, path);
        }

        SerializedObject so = new(level);

        // Empty rather than null-checked at every use: a parade level authors no waves at all, and a
        // half-authored spec should clear the asset's list rather than leave a stale one behind.
        (string prefab, Vector2Int cell)[] placements =
            spec.enemies ?? System.Array.Empty<(string prefab, Vector2Int cell)>();
        (int turn, string prefab, Vector2Int cell)[] reinforcements =
            spec.waves ?? System.Array.Empty<(int turn, string prefab, Vector2Int cell)>();
        Vector2Int[] spawnCells = spec.partySpawnCells ?? System.Array.Empty<Vector2Int>();

        SerializedProperty enemies = so.FindProperty("enemies");
        enemies.arraySize = placements.Length;

        for (int i = 0; i < placements.Length; i++)
        {
            (string prefab, Vector2Int cell) = placements[i];
            WritePlacement(enemies.GetArrayElementAtIndex(i), prefab, cell, spec.name);
        }

        // Grouped by turn, since EnemyWave holds a list rather than one placement.
        List<int> turns = new();

        foreach ((int turn, string _, Vector2Int _) in reinforcements)
        {
            if (!turns.Contains(turn)) { turns.Add(turn); }
        }

        turns.Sort();

        SerializedProperty waves = so.FindProperty("waves");
        waves.arraySize = turns.Count;

        for (int i = 0; i < turns.Count; i++)
        {
            SerializedProperty wave = waves.GetArrayElementAtIndex(i);
            wave.FindPropertyRelative("turn").intValue = turns[i];

            SerializedProperty members = wave.FindPropertyRelative("enemies");
            int count = 0;

            foreach ((int turn, string prefab, Vector2Int cell) in reinforcements)
            {
                if (turn != turns[i]) { continue; }

                members.arraySize = count + 1;
                WritePlacement(members.GetArrayElementAtIndex(count), prefab, cell, spec.name);
                count++;
            }

            members.arraySize = count;
        }

        SerializedProperty spawns = so.FindProperty("partySpawnCells");
        spawns.arraySize = spawnCells.Length;

        for (int i = 0; i < spawnCells.Length; i++)
        {
            spawns.GetArrayElementAtIndex(i).vector2IntValue = ToStored(spawnCells[i]);
        }

        so.FindProperty("boardSize").vector2IntValue = spec.boardSize;
        so.FindProperty("turnsToSurvive").intValue = spec.turnsToSurvive;
        so.FindProperty("handSize").intValue = 5;

        // Left as whatever the asset already has: which tilesets a level uses and what it rewards on
        // clear are art and balance decisions this roster has no opinion about, and blanking them on
        // every run would undo them.
        SerializedProperty loot = so.FindProperty("lootTable");

        if (loot.objectReferenceValue == null)
        {
            loot.objectReferenceValue = FindByName<LootTable>("EarlyGame", EnemyRoster.LootFolder);
        }

        SerializedProperty clear = so.FindProperty("clearRewardTable");

        if (clear.objectReferenceValue == null)
        {
            clear.objectReferenceValue =
                FindByName<LootTable>("EarlyLevelReward", EnemyRoster.LootFolder);
        }

        // A level with no tileset builds a board with no floor art under the tiles - playable, and
        // invisible. Seeded with whatever is on disk rather than left empty, and only when the level
        // has none, so a set chosen by hand is never overwritten.
        SerializedProperty tileSets = so.FindProperty("tileSets");

        if (tileSets.arraySize == 0)
        {
            TileSetData set = FindAny<TileSetData>("Assets/Data/TileSetData");

            if (set != null)
            {
                tileSets.arraySize = 1;
                tileSets.GetArrayElementAtIndex(0).objectReferenceValue = set;
            }
            else
            {
                Warn($"level '{spec.name}' has no tileSets and none exist on disk, so its board "
                     + "builds with no floor art at all.");
            }
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(level);
    }

    /// <summary>
    /// Authors every campaign in the roster's Runs table.
    ///
    /// The levels list is assigned outright, not appended to - which is what lets a level be *removed*
    /// from a run by deleting it from the table. Everything else on the asset is left alone, so the
    /// party, the deck overrides and carryDamageBetweenLevels survive untouched.
    /// </summary>
    private static void EnsureRuns()
    {
        EnsureFolder(EnemyRoster.RunFolder);

        foreach (RunSpec spec in EnemyRoster.Runs) { EnsureRun(spec); }
    }

    private static void EnsureRun(RunSpec spec)
    {
        string path = $"{EnemyRoster.RunFolder}/{spec.name}.asset";
        RunData run = AssetDatabase.LoadAssetAtPath<RunData>(path);
        bool created = run == null;

        if (created)
        {
            run = ScriptableObject.CreateInstance<RunData>();
            AssetDatabase.CreateAsset(run, path);
        }

        SerializedObject so = new(run);
        SerializedProperty levels = so.FindProperty("levels");
        levels.arraySize = spec.levels.Length;

        for (int i = 0; i < spec.levels.Length; i++)
        {
            LevelData level = FindByName<LevelData>(spec.levels[i], EnemyRoster.LevelFolder);

            if (level == null)
            {
                Warn($"run '{spec.name}' names level '{spec.levels[i]}', which was not found - that "
                     + "slot is empty and the run will skip straight past it.");
            }

            levels.GetArrayElementAtIndex(i).objectReferenceValue = level;
        }

        // Only on creation. A party edited afterwards is the author's, not this tool's.
        if (created && !string.IsNullOrEmpty(spec.seedPartyFrom)) { SeedParty(so, spec); }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(run);

        Debug.Log($"Enemy roster: {(created ? "created" : "updated")} {spec.name} with "
                  + $"{spec.levels.Length} level(s): {string.Join(", ", spec.levels)}.");
    }

    /// <summary>
    /// Copies the starting party off another run, so a freshly created campaign has somebody to play
    /// it with.
    ///
    /// StartingParty rather than StartingHeroes: the heroes list is what the character-select screen
    /// fills in per playthrough, while StartingParty is the standalone-play fallback RunManager reads
    /// when nothing was chosen - which is exactly the case a run opened straight from Game.unity is in.
    /// </summary>
    private static void SeedParty(SerializedObject target, RunSpec spec)
    {
        RunData source = FindByName<RunData>(spec.seedPartyFrom, EnemyRoster.RunFolder);

        if (source == null)
        {
            Warn($"run '{spec.name}' wanted its party seeded from '{spec.seedPartyFrom}', which was "
                 + "not found - it starts with nobody in it.");
            return;
        }

        SerializedObject from = new(source);

        CopyArray(from.FindProperty("startingParty"), target.FindProperty("startingParty"));
        CopyArray(from.FindProperty("startingPartyDecks"), target.FindProperty("startingPartyDecks"));

        target.FindProperty("carryDamageBetweenLevels").boolValue =
            from.FindProperty("carryDamageBetweenLevels").boolValue;
    }

    private static void CopyArray(SerializedProperty from, SerializedProperty to)
    {
        to.arraySize = from.arraySize;

        for (int i = 0; i < from.arraySize; i++)
        {
            to.GetArrayElementAtIndex(i).objectReferenceValue =
                from.GetArrayElementAtIndex(i).objectReferenceValue;
        }
    }

    private static void WritePlacement(
        SerializedProperty placement, string prefabName, Vector2Int cell, string level)
    {
        string path = PrefabPathOf(prefabName);
        GameObject prefab = path == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);

        if (prefab == null)
        {
            Warn($"level '{level}' places '{prefabName}', whose prefab was not found.");
        }

        placement.FindPropertyRelative("prefab").objectReferenceValue = prefab;
        placement.FindPropertyRelative("cell").vector2IntValue = ToStored(cell);
        placement.FindPropertyRelative("deckOverride").arraySize = 0;
    }

    private static Vector2Int ToStored(Vector2Int oneBased) =>
        Vector2Int.Max(oneBased - Vector2Int.one, Vector2Int.zero);

    // =============================================================================================
    // Shared
    // =============================================================================================

    private static string PrefabPathOf(string name)
    {
        foreach (CharacterSpec spec in EnemyRoster.Characters)
        {
            if (spec.name != name) { continue; }

            string folder = spec.boss ? EnemyRoster.BossPrefabFolder : EnemyRoster.EnemyPrefabFolder;

            return $"{folder}/{name}.prefab";
        }

        return null;
    }

    /// The first asset of type T under `folder`, or null. For seeding a field that needs *something*
    /// sensible rather than one particular thing - EnsureLevel's tileset is the only caller.
    private static T FindAny<T>(string folder) where T : Object
    {
        foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder }))
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));

            if (asset != null) { return asset; }
        }

        return null;
    }

    /// <summary>
    /// One asset of type T with exactly this file name, under `folder`.
    ///
    /// FindAssets matches loosely - "Block 1" also returns "Block 10" - so the file name is compared
    /// exactly afterwards. The search term is quoted because half these names contain spaces.
    ///
    /// Deliberately no "t:" filter. Whether that filter follows a custom ScriptableObject hierarchy -
    /// whether t:CardEffect finds a DamageEffect - is not worth betting forty card entries on, and
    /// LoadAssetAtPath<T> already returns null for the wrong type, so the check below is free.
    /// </summary>
    private static T FindByName<T>(string name, string folder) where T : Object
    {
        if (string.IsNullOrEmpty(name)) { return null; }

        foreach (string guid in AssetDatabase.FindAssets($"\"{name}\"", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (Path.GetFileNameWithoutExtension(path) != name) { continue; }

            T asset = AssetDatabase.LoadAssetAtPath<T>(path);

            if (asset != null) { return asset; }
        }

        return null;
    }

    /// Keeps AllCards.asset in step with the ~40 cards this adds. CardLibraryEditor does the same on
    /// import, but a card created and edited inside one menu command has not been through that yet.
    private static void RescanCardLibraries()
    {
        List<CardData> found = new();

        foreach (string guid in AssetDatabase.FindAssets("t:CardData", new[] { "Assets/Data/CardData" }))
        {
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(guid));

            if (card != null) { found.Add(card); }
        }

        foreach (string guid in AssetDatabase.FindAssets("t:CardLibrary"))
        {
            CardLibrary library =
                AssetDatabase.LoadAssetAtPath<CardLibrary>(AssetDatabase.GUIDToAssetPath(guid));

            if (library == null) { continue; }

            library.SetCards(found);
            EditorUtility.SetDirty(library);
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) { return; }

        string[] parts = path.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";

            if (!AssetDatabase.IsValidFolder(next)) { AssetDatabase.CreateFolder(current, parts[i]); }

            current = next;
        }
    }

    private static void Warn(string message)
    {
        warnings++;
        Debug.LogWarning($"Enemy roster: {message}");
    }
}
