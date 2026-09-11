using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wires the hero energy-pip art: the blue circle the card face's own mana pip already uses -
/// CardFaceV2.prefab's Pip/PipRim SpriteRenderers, tinted from CardPip.png at (0.22, 0.514, 0.863) -
/// plus the spent-pip cross and the available-pip scale/pulse knobs HeroPortrait.RefreshPips/Update
/// read. StatusPip.prefab (HeroPortrait.RefreshPips's pipPrefab) has no sprite assigned at all, which
/// is why a spriteless Image renders as a flat rectangle - reusing the same asset and colour here
/// rather than duplicating either.
///
/// HeroPortrait.prefab's own HeroPortrait component instance carries these fields independently of
/// HeroPortrait.cs's field defaults, so both need a direct edit - changing the .cs default alone does
/// not reach an already-serialized prefab instance.
///
/// Idempotent: every value is re-applied unconditionally on every run.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class EnergyPipArtWiring
{
    private const string StatusPipPrefabPath = "Assets/Prefabs/UI/StatusPip.prefab";
    private const string HeroPortraitPrefabPath = "Assets/Prefabs/UI/HeroPortrait.prefab";
    private const string CardPipSpritePath = "Assets/Sprites/CardFace/CardPip.png";

    // SharpSkin.IconClose points at the same file - not referenced directly here so this command stays
    // usable from outside the Editor-only SharpSkin toolkit's own namespace expectations.
    private const string CrossSpritePath = "Assets/SharpUI/Textures/icon_close.png";

    private static readonly Color FilledColor = new(0.22f, 0.514f, 0.863f, 1f);
    private static readonly Color EmptyColor = new(0.22f, 0.514f, 0.863f, 0.25f);
    private static readonly Color CrossColor = new(0.75f, 0.78f, 0.82f, 0.85f);

    [MenuItem("Tools/Battle HUD/Wire Energy Pips")]
    public static void Recolor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Energy pip art wiring: exit Play Mode first - prefab edits made in play do not persist.");
            return;
        }

        Sprite pipSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CardPipSpritePath);

        if (pipSprite == null)
        {
            Debug.LogError($"Energy pip art wiring: {CardPipSpritePath} not found - cannot recolor.");
            return;
        }

        Sprite crossSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CrossSpritePath);

        if (crossSprite == null)
        {
            Debug.LogError($"Energy pip art wiring: {CrossSpritePath} not found - cannot wire the spent-pip cross.");
            return;
        }

        if (!RecolorStatusPip(pipSprite))
        {
            Debug.LogError($"Energy pip art wiring: {StatusPipPrefabPath} not found - skipping.");
        }

        if (!RecolorHeroPortraitPips(crossSprite))
        {
            Debug.LogError($"Energy pip art wiring: {HeroPortraitPrefabPath} not found - skipping.");
        }

        AssetDatabase.SaveAssets();

        Debug.Log("Energy pip art wiring: done - prefabs saved.");
    }

    private static bool RecolorStatusPip(Sprite pipSprite)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(StatusPipPrefabPath);

        if (existing == null) { return false; }

        GameObject root = PrefabUtility.LoadPrefabContents(StatusPipPrefabPath);
        Image image = root.GetComponent<Image>();

        if (image == null)
        {
            Debug.LogError($"Energy pip art wiring: {StatusPipPrefabPath} has no Image component - skipping.");
            PrefabUtility.UnloadPrefabContents(root);
            return false;
        }

        image.sprite = pipSprite;
        image.color = FilledColor;
        image.preserveAspect = true;

        PrefabUtility.SaveAsPrefabAsset(root, StatusPipPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        return true;
    }

    private static bool RecolorHeroPortraitPips(Sprite crossSprite)
    {
        HeroPortrait existing = AssetDatabase.LoadAssetAtPath<HeroPortrait>(HeroPortraitPrefabPath);

        if (existing == null) { return false; }

        GameObject root = PrefabUtility.LoadPrefabContents(HeroPortraitPrefabPath);
        HeroPortrait portrait = root.GetComponent<HeroPortrait>();

        SerializedObject so = new(portrait);
        so.FindProperty("pipFilledColor").colorValue = FilledColor;
        so.FindProperty("pipEmptyColor").colorValue = EmptyColor;
        so.FindProperty("pipSpentSprite").objectReferenceValue = crossSprite;
        so.FindProperty("pipSpentCrossColor").colorValue = CrossColor;
        so.FindProperty("availablePipScale").floatValue = 1.25f;
        so.FindProperty("pipPulsePeriod").floatValue = 1.4f;
        so.FindProperty("pipPulseMinAlpha").floatValue = 0.75f;
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(root, HeroPortraitPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        return true;
    }
}
