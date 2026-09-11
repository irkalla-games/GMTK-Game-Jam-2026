using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot authoring of three example area-of-effect cards, so the feature has something real to
/// playtest rather than just the machinery: a Radius blast, a Radius burst aimed at the caster, and a
/// Pattern cone - one example of each AreaKind.
///
/// Reuses existing, shared CardEffect assets (Damage 5, Damage 4, Shield 5) rather than authoring new
/// ones - the whole point of moving aimsAt and area onto the per-card entry is that a shared effect
/// asset can be single-target on one card and an area on another without touching the asset itself.
///
/// Idempotent: each card/pattern is only created if its asset does not already exist, so re-running
/// after hand-tuning one of them by hand will not stomp it.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 - that script drives
/// Assembly-CSharp.csproj, which never lists Assets/Editor. Verify by focusing the Editor and checking
/// the console, per CLAUDE.md. New CardData assets are picked up by CardLibraryEditor's own import
/// hook, so AllCards.asset needs no separate step here.
/// </summary>
public static class AreaOfEffectExampleContent
{
    private const string PatternFolder = "Assets/Data/EffectData/Patterns";
    private const string ConePatternPath = PatternFolder + "/Cone3.asset";

    private const string CardFolder = "Assets/Data/CardData/Generic";
    private const string BlastCardPath = CardFolder + "/Fireball Storm.asset";
    private const string AegisCardPath = CardFolder + "/Guardian's Aegis.asset";
    private const string ConeCardPath = CardFolder + "/Flame Cone.asset";

    private const string Damage5Path = "Assets/Data/EffectData/Damage/Damage 5.asset";
    private const string Damage4Path = "Assets/Data/EffectData/Damage/Damage 4.asset";
    private const string Shield5Path = "Assets/Data/EffectData/Shield/Shield 5.asset";

    [MenuItem("Tools/Cards/Author Area Of Effect Examples")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("AoE examples: exit Play Mode first.");
            return;
        }

        EnsureFolder(PatternFolder);
        EnsureFolder(CardFolder);

        EffectPattern cone = EnsureConePattern();

        DamageEffect damage5 = AssetDatabase.LoadAssetAtPath<DamageEffect>(Damage5Path);
        DamageEffect damage4 = AssetDatabase.LoadAssetAtPath<DamageEffect>(Damage4Path);
        ShieldEffect shield5 = AssetDatabase.LoadAssetAtPath<ShieldEffect>(Shield5Path);

        if (damage5 == null || damage4 == null || shield5 == null)
        {
            Debug.LogError("AoE examples: Damage 5 / Damage 4 / Shield 5 not all found - aborted.");
            return;
        }

        WriteCard(BlastCardPath, "Fireball Storm", cost: 2,
            range: new TargetRange(RangeShape.Chebyshev, 1, 4),
            requiredClass: CharacterClass.Any, rarity: Rarity.Common,
            description: "Deal 5 damage to everyone in a 3x3 area.",
            entries: new List<(CardEffect, EffectTarget, Action<SerializedProperty>)>
            {
                (damage5, EffectTarget.PlayedTile, p => SetRadiusArea(p, RangeShape.Chebyshev, 0, 1)),
            });

        WriteCard(AegisCardPath, "Guardian's Aegis", cost: 2,
            range: new TargetRange(RangeShape.SelfTile, 0, 0),
            requiredClass: CharacterClass.Any, rarity: Rarity.Common,
            description: "Gain 5 Shield, and grant 5 Shield to every ally within 2 tiles.",
            entries: new List<(CardEffect, EffectTarget, Action<SerializedProperty>)>
            {
                (shield5, EffectTarget.Source, p => SetRadiusArea(p, RangeShape.Manhattan, 0, 2)),
            });

        WriteCard(ConeCardPath, "Flame Cone", cost: 2,
            range: new TargetRange(RangeShape.Chebyshev, 1, 1),
            requiredClass: CharacterClass.Any, rarity: Rarity.Common,
            description: "Deal 4 damage to everyone in a cone.",
            entries: new List<(CardEffect, EffectTarget, Action<SerializedProperty>)>
            {
                (damage4, EffectTarget.PlayedTile, p => SetPatternArea(p, cone)),
            });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("AoE examples: done.");
    }

    /// <summary>
    /// A narrow-to-wide fan: the clicked tile itself, a 3-wide row one step further out, then a
    /// 5-wide row at the edge of its 2-tile reach. The identical relative shape is painted into both
    /// grids - EffectPattern rotates each independently, so the same fan reads as a cone in any of the
    /// 8 directions rather than needing a hand-tuned diagonal version.
    /// </summary>
    private static EffectPattern EnsureConePattern()
    {
        EffectPattern existing = AssetDatabase.LoadAssetAtPath<EffectPattern>(ConePatternPath);
        if (existing != null) { return existing; }

        EffectPattern pattern = ScriptableObject.CreateInstance<EffectPattern>();
        pattern.Resize(5, 3);
        pattern.SetAnchor(new Vector2Int(2, 0));

        (int x, int y)[] fan =
        {
            (2, 0),
            (1, 1), (2, 1), (3, 1),
            (0, 2), (1, 2), (2, 2), (3, 2), (4, 2),
        };

        foreach ((int x, int y) in fan)
        {
            pattern.SetCardinalCell(x, y, true);
            pattern.SetDiagonalCell(x, y, true);
        }

        AssetDatabase.CreateAsset(pattern, ConePatternPath);
        Debug.Log($"AoE examples: created {ConePatternPath}.");

        return pattern;
    }

    private static void SetRadiusArea(SerializedProperty areaProp, RangeShape shape, int min, int max)
    {
        areaProp.FindPropertyRelative("kind").intValue = (int)AreaKind.Radius;

        SerializedProperty radius = areaProp.FindPropertyRelative("radius");
        radius.FindPropertyRelative("shape").intValue = (int)shape;
        radius.FindPropertyRelative("minDistance").intValue = min;
        radius.FindPropertyRelative("maxDistance").intValue = max;
    }

    private static void SetPatternArea(SerializedProperty areaProp, EffectPattern pattern)
    {
        areaProp.FindPropertyRelative("kind").intValue = (int)AreaKind.Pattern;
        areaProp.FindPropertyRelative("pattern").objectReferenceValue = pattern;
    }

    private static void WriteCard(
        string path, string name, int cost, TargetRange range, CharacterClass requiredClass, Rarity rarity,
        string description, List<(CardEffect effect, EffectTarget aimsAt, Action<SerializedProperty> setArea)> entries)
    {
        if (AssetDatabase.LoadAssetAtPath<CardData>(path) != null) { return; }

        CardData card = ScriptableObject.CreateInstance<CardData>();
        AssetDatabase.CreateAsset(card, path);

        SerializedObject so = new(card);

        so.FindProperty("<cardName>k__BackingField").stringValue = name;
        so.FindProperty("<cost>k__BackingField").intValue = cost;

        SerializedProperty rangeProp = so.FindProperty("<range>k__BackingField");
        rangeProp.FindPropertyRelative("shape").intValue = (int)range.Shape;
        rangeProp.FindPropertyRelative("minDistance").intValue = range.MinDistance;
        rangeProp.FindPropertyRelative("maxDistance").intValue = range.MaxDistance;

        so.FindProperty("<requiredClass>k__BackingField").intValue = (int)requiredClass;
        so.FindProperty("<rarity>k__BackingField").intValue = (int)rarity;
        so.FindProperty("<description>k__BackingField").stringValue = description;

        SerializedProperty entriesProp = so.FindProperty("<effectEntries>k__BackingField");
        entriesProp.arraySize = entries.Count;

        for (int i = 0; i < entries.Count; i++)
        {
            SerializedProperty entry = entriesProp.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("effect").objectReferenceValue = entries[i].effect;
            entry.FindPropertyRelative("aimsAt").intValue = (int)entries[i].aimsAt;
            entries[i].setArea(entry.FindPropertyRelative("area"));
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(card);

        Debug.Log($"AoE examples: created {path}.");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) { return; }

        string parent = path.Substring(0, path.LastIndexOf('/'));
        string leaf = path.Substring(path.LastIndexOf('/') + 1);

        if (!AssetDatabase.IsValidFolder(parent)) { EnsureFolder(parent); }

        AssetDatabase.CreateFolder(parent, leaf);
    }
}
