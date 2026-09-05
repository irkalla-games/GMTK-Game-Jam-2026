using UnityEditor;
using UnityEngine;

/// <summary>
/// Re-imports the two new card art packs under `Assets/Card Art` as sprites CardViewer can actually
/// draw into the card face.
///
/// Both packs ship import settings that are individually reasonable for their original purpose and
/// wrong for this one:
///
///   - The 310 CaptainCatSparrow icons (`*/Icons/*.png`) come in as `spriteMode: Single` but
///     `spriteMeshType: Tight` - fine for a UGUI Image, but SpriteRenderer's Sliced draw mode (which
///     the card face's Image slot uses) silently refuses a Tight mesh. CardViewer would show nothing.
///   - The 200 Free_FantasySkillIcon icons (`SkillIcon*/*.png`) come in as `spriteMode: Multiple`, so
///     Unity generates a `<name>_0` sub-sprite rather than a sprite at the texture's own path -
///     `AssetDatabase.LoadAssetAtPath&lt;Sprite&gt;(path)` returns null for every one of them, which is
///     what CardArtAssigner and CardData.image both need to succeed.
///
/// This tool re-imports every icon under either pack to one shared, working configuration: Single
/// sprite mode, FullRect mesh (Sliced-safe), centered pivot, alpha-as-transparency, no mipmaps, and a
/// 512px cap - the card's own art window renders around 155 x 80px, so nothing above 512 is ever
/// visible and the packs' native 512-1024px originals were otherwise bloating the built player for no
/// return.
///
/// Idempotent and repair-not-skip: every field this tool owns is re-applied on every run, and a
/// texture already at these settings is left untouched (SaveAndReimport only fires on an actual
/// difference) so re-running after adding more icons to either pack costs almost nothing.
///
/// Batched behind StartAssetEditing/StopAssetEditing for the same reason BlockSpriteImporter is: nothing
/// is created and nothing is loaded back inside the block, so the CLAUDE.md warning about
/// StartAssetEditing hiding freshly-created assets from LoadAssetAtPath does not apply here - only the
/// verification pass after StopAssetEditing loads anything back.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
static class CardArtImportNormaliser
{
    private const string CardArtFolder = "Assets/Card Art";

    /// The card face's art window renders at ~155 x 80px on screen (CardFaceV2.prefab's Image slot is
    /// 1.548978 x 0.8015901 world units at 100 PPU) - nothing above this is ever visible.
    private const int MaxTextureSize = 512;

    [MenuItem("Tools/Cards/1 - Normalise Card Art Imports")]
    private static void Normalise()
    {
        if (!AssetDatabase.IsValidFolder(CardArtFolder))
        {
            Debug.LogError($"Card art import normaliser: no folder at {CardArtFolder}.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { CardArtFolder });
        System.Collections.Generic.List<string> iconPaths = new();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // Only the two icon sets - not the packs' preview screenshots, demo-scene backgrounds, or
            // UI chrome (container frames, masks, header labels), none of which the card face ever uses.
            bool isIcon = path.Contains("/Icons/") || path.Contains("/SkillIcon/")
                          || path.Contains("/SkillIcon_Simplified/");

            if (isIcon) { iconPaths.Add(path); }
        }

        if (iconPaths.Count == 0)
        {
            Debug.LogError($"Card art import normaliser: found no icons under {CardArtFolder}. "
                            + "Expected */Icons/*.png and SkillIcon*/*.png.");
            return;
        }

        int reimported = 0;

        AssetDatabase.StartAssetEditing();

        try
        {
            foreach (string path in iconPaths)
            {
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) { continue; }

                TextureImporterSettings settings = new();
                importer.ReadTextureSettings(settings);

                bool changed = importer.maxTextureSize != MaxTextureSize
                               || settings.textureType != TextureImporterType.Sprite
                               || settings.spriteMode != (int)SpriteImportMode.Single
                               || settings.spriteMeshType != SpriteMeshType.FullRect
                               || settings.spriteAlignment != (int)SpriteAlignment.Center
                               || settings.mipmapEnabled
                               || !settings.alphaIsTransparency
                               || settings.wrapMode != TextureWrapMode.Clamp;

                if (!changed) { continue; }

                settings.textureType = TextureImporterType.Sprite;
                settings.spriteMode = (int)SpriteImportMode.Single;
                settings.spritePixelsPerUnit = 100f;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                settings.spritePivot = new Vector2(0.5f, 0.5f);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.alphaIsTransparency = true;
                settings.mipmapEnabled = false;
                settings.wrapMode = TextureWrapMode.Clamp;

                importer.SetTextureSettings(settings);
                importer.maxTextureSize = MaxTextureSize;

                importer.SaveAndReimport();
                reimported++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.Refresh();

        int missing = 0;

        foreach (string path in iconPaths)
        {
            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null)
            {
                Debug.LogError($"Card art import normaliser: {path} still has no Sprite after re-import.");
                missing++;
            }
        }

        if (missing > 0)
        {
            Debug.LogError($"Card art import normaliser: {missing} of {iconPaths.Count} icon(s) "
                            + "produced no Sprite - CardArtAssigner will not see them.");
            return;
        }

        Debug.Log($"Card art import normaliser: {iconPaths.Count} icon(s) checked, {reimported} "
                  + $"re-imported as Single/FullRect sprites capped at {MaxTextureSize}px. Every icon "
                  + "produced a usable Sprite.");
    }
}
