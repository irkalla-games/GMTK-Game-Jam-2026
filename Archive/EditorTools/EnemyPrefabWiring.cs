using UnityEditor;
using UnityEngine;

/// <summary>
/// Two Character fields on the two current enemy prefabs, applied together since both edits go through
/// the same LoadPrefabContents/SaveAsPrefabAsset round trip:
///
/// - A portrait. Character.portrait is unauthored on every enemy prefab today - WaveCircle falls back
///   to the prefab's board sprite when it is blank, but a purpose-picked glyph reads better in a small
///   circle, the same reasoning HeroPortraitResizeWiring gives the three player classes.
///
/// - A TargetingPattern. SkeletonWarrior currently has none at all, which is an accident rather than a
///   design: every totem is 1 HP, so the fallback Weakest behaviour ("most hurt legal target") can never
///   prefer a hero over a totem, and every skeleton beelines for the nearest one. WarriorTargeting.asset
///   makes that a deliberate off switch (IgnoreTotems) instead. RangerTargeting.asset gives the Ranger a
///   15% chance per turn to hunt the nearest totem on top of its existing Strongest sequence - see
///   TargetingPattern.TotemChancePercent and Character.RollTotemHunt.
///
/// Idempotent, and edits existing prefabs via PrefabUtility.LoadPrefabContents/SaveAsPrefabAsset rather
/// than recreating them - the same technique HeroPortraitResizeWiring.AssignPortrait uses, which is what
/// keeps the prefabs' file GUIDs, and every scene/prefab reference to them, intact.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class EnemyPrefabWiring
{
    private const string SkeletonWarriorPrefabPath = "Assets/Prefabs/Enemies/SkeletonWarrior.prefab";
    private const string EnemyRangerPrefabPath = "Assets/Prefabs/Enemies/EnemyRanger.prefab";

    private const string SkeletonPortraitPath = "Assets/Extra Assets/Dark UI/New Icons/White Skull.png";
    private const string RangerPortraitPath = "Assets/Extra Assets/Dark UI/New Icons/White Bow.png";

    private const string WarriorTargetingPath = "Assets/Data/TargetingData/WarriorTargeting.asset";
    private const string RangerTargetingPath = "Assets/Data/TargetingData/RangerTargeting.asset";

    [MenuItem("Tools/Enemies/Wire Totem Targeting And Portraits")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Enemy prefab wiring: exit Play Mode first - prefab edits made in play do not persist.");
            return;
        }

        ApplyToPrefab(SkeletonWarriorPrefabPath, SkeletonPortraitPath, WarriorTargetingPath);
        ApplyToPrefab(EnemyRangerPrefabPath, RangerPortraitPath, RangerTargetingPath);

        AssetDatabase.SaveAssets();

        Debug.Log("Enemy prefab wiring: done - portraits and targeting patterns assigned.");
    }

    private static void ApplyToPrefab(string prefabPath, string portraitSpritePath, string targetingPatternPath)
    {
        GameObject existingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        if (existingAsset == null)
        {
            Debug.LogError($"Enemy prefab wiring: {prefabPath} not found - skipping.");
            return;
        }

        Sprite portrait = AssetDatabase.LoadAssetAtPath<Sprite>(portraitSpritePath);

        if (portrait == null)
        {
            Debug.LogError($"Enemy prefab wiring: portrait sprite not found at {portraitSpritePath} - "
                + $"skipping {prefabPath}'s portrait.");
        }

        TargetingPattern pattern = AssetDatabase.LoadAssetAtPath<TargetingPattern>(targetingPatternPath);

        if (pattern == null)
        {
            Debug.LogError($"Enemy prefab wiring: targeting pattern not found at {targetingPatternPath} - "
                + $"skipping {prefabPath}'s pattern.");
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        Character character = root.GetComponent<Character>();

        if (character == null)
        {
            Debug.LogError($"Enemy prefab wiring: {prefabPath} has no Character component - skipping.");
            PrefabUtility.UnloadPrefabContents(root);
            return;
        }

        SerializedObject so = new(character);

        if (portrait != null) { so.FindProperty("portrait").objectReferenceValue = portrait; }
        if (pattern != null) { so.FindProperty("targetingPattern").objectReferenceValue = pattern; }

        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        Debug.Log($"Enemy prefab wiring: {prefabPath} updated.");
    }
}
