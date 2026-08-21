using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Recolors the hero energy-pip art from flat white squares to the same blue circle the card face's own
/// mana pip already uses - CardFaceV2.prefab's Pip/PipRim SpriteRenderers, tinted from CardPip.png at
/// (0.22, 0.514, 0.863). StatusPip.prefab (HeroPortrait.RefreshPips's pipPrefab) has no sprite assigned
/// at all, which is why a spriteless Image renders as a flat rectangle - reusing the same asset and
/// colour here rather than duplicating either.
///
/// HeroPortrait.prefab's own HeroPortrait component instance carries its serialized
/// pipFilledColor/pipEmptyColor independently of HeroPortrait.cs's field defaults, so both need a direct
/// edit - changing the .cs default alone does not reach an already-serialized prefab instance.
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

    private static readonly Color FilledColor = new(0.22f, 0.514f, 0.863f, 1f);
    private static readonly Color EmptyColor = new(0.22f, 0.514f, 0.863f, 0.25f);

    [MenuItem("Tools/Battle HUD/Recolor Energy Pips")]
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

        if (!RecolorStatusPip(pipSprite))
        {
            Debug.LogError($"Energy pip art wiring: {StatusPipPrefabPath} not found - skipping.");
        }

        if (!RecolorHeroPortraitPips())
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

    private static bool RecolorHeroPortraitPips()
    {
        HeroPortrait existing = AssetDatabase.LoadAssetAtPath<HeroPortrait>(HeroPortraitPrefabPath);

        if (existing == null) { return false; }

        GameObject root = PrefabUtility.LoadPrefabContents(HeroPortraitPrefabPath);
        HeroPortrait portrait = root.GetComponent<HeroPortrait>();

        SerializedObject so = new(portrait);
        so.FindProperty("pipFilledColor").colorValue = FilledColor;
        so.FindProperty("pipEmptyColor").colorValue = EmptyColor;
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(root, HeroPortraitPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        return true;
    }
}
