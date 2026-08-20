using UnityEditor;
using UnityEngine;

/// <summary>
/// Re-imports the isometric block pack as sprites the board can actually draw.
///
/// The 101 PNGs ship as plain textures - `textureType: 0` (Default) with `spriteMode: 0` - which means
/// Unity generates **no Sprite sub-asset at all**, so nothing can reference them from a SpriteRenderer.
/// On top of that the defaults are all wrong for 64px pixel art: bilinear filtering, mipmaps on, DXT
/// compression, and `alphaIsTransparency` off (which fringes the diamond edges).
///
/// ## The pivot is the load-bearing setting
///
/// Every block in the pack is the same shape - verified by scanning per-row alpha across all 101:
///
///     64 x 64 canvas
///       top-face diamond   64 x 32, rows 0..32, centre at row 16 from the top
///       cube extrusion     33 px, left/right vertices at rows 16 and 49
///
/// A tile's origin has to sit on the centre of its **top face**, not the centre of the whole block
/// silhouette, or every character stands 16px too low and the floor does not tile against itself. Row
/// 16 from the top is row 48 from the bottom, so the normalized pivot is (0.5, 48/64) = (0.5, 0.75).
/// Unity's default (0.5, 0.5) centres the silhouette instead, which is the wrong point.
///
/// ## Why 32 pixels per unit
///
/// 64px / 32 PPU = 2.0 world units per block, giving a 2.0 x 1.0 top face - exactly GridManager's
/// `cellSize`, so a block renders at scale 1.0 with no magic multiplier anywhere. BoardVisuals still
/// derives its scale from the sprite rather than assuming this, so a re-import at another PPU only
/// changes how big the art is, never whether it lines up.
///
/// Idempotent and repair-not-skip, same contract as TutorialContentGenerator: every field this tool
/// owns is re-written on every run, so a half-converted folder is fixed by running it again.
/// </summary>
static class BlockSpriteImporter
{
    const string BlocksFolder = "Assets/Extra Assets/Isometric_Tiles_Pixel_Art/Blocks";

    /// 64px block / 32 PPU = 2.0 world units, matching GridManager.cellSize.x.
    const float PixelsPerUnit = 32f;

    /// Centre of the top-face diamond: row 16 from the top of a 64px canvas = 48 from the bottom.
    static readonly Vector2 TopFacePivot = new(0.5f, 0.75f);

    [MenuItem("Tools/Board/1 - Import Block Sprites")]
    static void ImportBlockSprites()
    {
        if (!AssetDatabase.IsValidFolder(BlocksFolder))
        {
            Debug.LogError($"BlockSpriteImporter: no folder at {BlocksFolder}. The Isometric_Tiles_"
                           + "Pixel_Art pack is untracked in git - check it is actually on disk.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { BlocksFolder });

        if (guids.Length == 0)
        {
            Debug.LogError($"BlockSpriteImporter: {BlocksFolder} contains no textures.");
            return;
        }

        int converted = 0;

        // Batched purely to avoid 101 separate refreshes, which is a visible stall - the same reason
        // EnemyIdleAnimationSetup.NormalisePivots batches. Safe here because this tool creates no
        // assets and loads nothing back inside the block; the verification pass below runs after
        // StopAssetEditing. (CLAUDE.md's warning is about *generating* assets inside a batch, where
        // LoadAssetAtPath silently returns null for anything made in the same block.)
        AssetDatabase.StartAssetEditing();

        try
        {
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) { continue; }

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);

                settings.textureType = TextureImporterType.Sprite;
                settings.spriteMode = (int)SpriteImportMode.Single;
                settings.spritePixelsPerUnit = PixelsPerUnit;
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = TopFacePivot;
                settings.filterMode = FilterMode.Point;
                settings.mipmapEnabled = false;
                settings.alphaIsTransparency = true;
                settings.wrapMode = TextureWrapMode.Clamp;
                settings.npotScale = TextureImporterNPOTScale.None;

                importer.SetTextureSettings(settings);
                importer.textureCompression = TextureImporterCompression.Uncompressed;

                importer.SaveAndReimport();
                converted++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.Refresh();

        // After the batch, so the sprite sub-assets actually exist to be loaded. A texture that still
        // yields no Sprite here means the import genuinely failed rather than merely being deferred.
        int missing = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null)
            {
                Debug.LogError($"BlockSpriteImporter: {path} still has no Sprite after re-import.");
                missing++;
            }
        }

        if (missing > 0)
        {
            Debug.LogError($"BlockSpriteImporter: {missing} of {converted} texture(s) produced no "
                           + "Sprite. The board will draw gaps where those blocks were rolled.");
            return;
        }

        Debug.Log($"BlockSpriteImporter: {converted} block(s) imported as Sprites - {PixelsPerUnit} "
                  + $"PPU, pivot {TopFacePivot} (top-face centre), Point filter, no mipmaps, "
                  + "uncompressed. Every one produced a usable Sprite.");
    }
}
