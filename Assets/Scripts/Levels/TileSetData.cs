using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One level's floor and wall art: which block sprites the board may roll, and how tall its walls are.
///
/// The authoring half of the board's look, in the same relationship to BoardVisuals that CardData has
/// to Card - this is the shared type asset, BoardVisuals is the per-battle instance that rolled a
/// particular floor out of it. So everything here is private with read-only properties and nothing
/// here is ever written at runtime.
///
/// A level holds a *list* of these and picks one per battle (see LevelData.TileSets), so the same
/// encounter can read as a stone dungeon one run and a mossy ruin the next without re-authoring it.
/// </summary>
[CreateAssetMenu(menuName = "Tile Set Data")]
public class TileSetData : ScriptableObject
{
    [Tooltip("Floor blocks. Each ground cell rolls one of these, so a list of near-identical variants "
             + "reads as texture while a list of one reads as a flat, uniform floor.")]
    [SerializeField] private List<Sprite> groundSprites = new();

    [Tooltip("Wall blocks, rolled per block rather than per cell - a two-block-tall wall rolls twice.")]
    [SerializeField] private List<Sprite> wallSprites = new();

    [Tooltip("Vertical extrusion of one block, in source pixels - how far up the next block in a stack "
             + "sits. 33 for the Isometric_Tiles_Pixel_Art pack, whose 64x64 blocks put their top-face "
             + "left/right vertices on rows 16 and 49. Wrong here and a stacked wall either overlaps "
             + "itself or shows a gap through to the background.")]
    [SerializeField] private int cubeHeightPixels = 33;

    [Tooltip("How many blocks tall the wall along the two far edges is.")]
    [SerializeField] private int wallHeightInBlocks = 2;

    [Tooltip("Multiplied into every floor block. White leaves the art as drawn; 0.6 grey renders it "
             + "about 40% darker. Tinting rather than re-exporting the PNGs keeps the pack shared and "
             + "lets two tilesets take the same blocks in different moods.")]
    [SerializeField] private Color groundTint = Color.white;

    [Tooltip("Multiplied into every wall block. Separate from the floor tint so the wall can be sunk "
             + "back without dragging the floor down with it.")]
    [SerializeField] private Color wallTint = Color.white;

    public IReadOnlyList<Sprite> GroundSprites => groundSprites;

    public IReadOnlyList<Sprite> WallSprites => wallSprites;

    public int CubeHeightPixels => cubeHeightPixels;

    public int WallHeightInBlocks => wallHeightInBlocks;

    public Color GroundTint => groundTint;

    public Color WallTint => wallTint;

    /// <summary>
    /// Whether this set can actually draw a board. Asked by BoardVisuals before it commits to a set,
    /// so a half-authored asset is skipped in favour of another in the level's list rather than
    /// producing a board with holes in it.
    /// </summary>
    public bool IsUsable => CountUsable(groundSprites) > 0
                            && (wallHeightInBlocks <= 0 || CountUsable(wallSprites) > 0);

    private static int CountUsable(List<Sprite> sprites)
    {
        if (sprites == null) { return 0; }

        int usable = 0;

        foreach (Sprite sprite in sprites)
        {
            // `!=` not `??`: these are UnityEngine.Objects, so a missing or destroyed entry only reads
            // as null through the overloaded operator. See CLAUDE.md.
            if (sprite != null) { usable++; }
        }

        return usable;
    }

    /// <summary>
    /// How far up one block sits above the one below it, in world units, given the scale BoardVisuals
    /// is drawing blocks at. Derived from the sprite's own pixels-per-unit rather than assumed, so a
    /// re-import at a different PPU changes how big the art is without ever desynchronising the stack.
    /// </summary>
    public float CubeHeightWorld(Sprite reference, float blockScale)
    {
        if (reference == null || reference.pixelsPerUnit <= 0f) { return 0f; }

        return cubeHeightPixels / reference.pixelsPerUnit * blockScale;
    }
}
