using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The board you can see: a floor of isometric blocks, and a wall along the two far edges.
///
/// Rebuilt with the board rather than painted into the scene, which is what the Tilemap it replaces
/// could not do - that was a fixed 48-cell plate on a 2.5 x 1.25 pitch while the logical board ran on
/// 3.0 x 1.5, so the art drifted further out of line the further from the origin you looked, and a
/// 3x6 tutorial board still got a 6x8 floor.
///
/// Purely decorative: nothing here is clickable and nothing here is asked a gameplay question. The
/// interactive diamond is still GridTile, one per *ground* cell, sitting on the Grid sorting layer
/// above these. Walls are not cells - they occupy the phantom column x = gw and row y = gh, which no
/// GridTile exists for, so nothing can be played onto them.
///
/// ## Why walls only on two edges
///
/// `+x` runs up-right and `+y` up-left, so the far edges from the camera are `x = gw` and `y = gh`.
/// Walling those two reads as an arena boundary; walling the near two would put a wall between the
/// camera and the board. The corner cell (gw, gh) closes the join.
///
/// ## Draw order
///
/// A cube overlaps whatever is behind it, so the floor has to be drawn back to front. That is exactly
/// GridManager.CellSortingOrder - further back is a lower order - and it is why a wall, whose cell sum
/// is higher than any ground cell on its row, correctly ends up *behind* the floor tile in front of
/// it despite being taller.
/// </summary>
public class BoardVisuals : MonoBehaviour
{
    private const string BlockName = "Block";
    private const string BackdropName = "Backdrop";

    private readonly List<GameObject> blocks = new();

    [Header("Backdrop")]
    [Tooltip("Colour directly behind the grid's centre.")]
    [SerializeField] private Color glowCenterColor = new(0.32f, 0.34f, 0.42f, 1f);

    [Tooltip("Colour the glow fades out to - matches BoardCamera's own background colour (both default "
             + "to pure black) so the two meet without a visible seam once the glow has faded out.")]
    [SerializeField] private Color glowEdgeColor = Color.black;

    [Tooltip("World distance from the grid's centre at which the glow has fully faded to glowEdgeColor. "
             + "Small on purpose - this is what keeps the light a contained pool around the board rather "
             + "than washing out the whole screen.")]
    [SerializeField] private float glowRadius = 9f;

    [Tooltip("World-space width/height of the backdrop square, centred on the grid. Comfortably larger "
             + "than BoardCamera's frustum ever gets (max ortho size 12, so up to ~24 tall) and than "
             + "glowRadius, so every pixel out to the screen edge is settled at glowEdgeColor rather "
             + "than a hard cutoff at the sprite's own border.")]
    [SerializeField] private float backdropSize = 80f;

    private GameObject backdrop;

    /// <summary>
    /// The footprint of everything built, including the cube skirt hanging below the front edge and
    /// the wall standing above the back ones. This - not the flat ring of tile centres - is what the
    /// camera has to fit, which is why it is measured from the renderers rather than computed from
    /// the cell grid.
    /// </summary>
    public Bounds Bounds { get; private set; }

    public bool HasBuilt { get; private set; }

    /// <summary>
    /// Tears down the previous floor and builds one for `size` out of `set`.
    ///
    /// `seed` makes the per-cell sprite roll repeatable: the same battle rebuilding its board - which
    /// BuildGrid does on every level load - lays down the identical floor rather than reshuffling
    /// under the player. Pass a fresh seed per battle, not per rebuild.
    /// </summary>
    public void Build(Vector2Int size, TileSetData set, int seed)
    {
        Clear();

        if (size.x <= 0 || size.y <= 0) { return; }

        // Ahead of the tileset check below - the glow behind an unauthored board is still worth
        // showing even though that board has no floor art to sit on top of it.
        BuildBackdrop(GridManager.Instance.IsoToWorld(size.x, size.y) * 0.5f);

        // `!=` not `??` - a missing asset is a Unity fake-null. See CLAUDE.md.
        if (set == null || !set.IsUsable)
        {
            Debug.LogWarning("BoardVisuals: no usable TileSetData for this level, so the board has no "
                             + "floor art. Author one and add it to LevelData.tileSets - the board is "
                             + "still playable, it is just invisible under the tiles.");
            return;
        }

        System.Random roll = new(seed);

        float blockScale = ScaleFor(First(set.GroundSprites));

        if (blockScale <= 0f)
        {
            Debug.LogError("BoardVisuals: could not derive a block scale - the ground sprite has no "
                           + "width or no pixels-per-unit. Run Tools/Board/1 - Import Block Sprites.");
            return;
        }

        float cubeHeight = set.CubeHeightWorld(First(set.GroundSprites), blockScale);

        for (int x = 0; x <= size.x; x++)
        {
            for (int y = 0; y <= size.y; y++)
            {
                Vector2Int cell = new(x, y);
                bool isWall = x == size.x || y == size.y;
                int baseOrder = GridManager.Instance.CellSortingOrder(cell);
                Vector3 floor = GridManager.Instance.IsoToWorld(x, y);

                if (!isWall)
                {
                    Spawn(Roll(set.GroundSprites, roll), floor, blockScale, baseOrder, set.GroundTint);
                    continue;
                }

                // Stacked upward from the floor plane. The first block is lifted a full cube so its
                // own 33px skirt lands flush on the floor plane - the wall stands *on* the board
                // rather than being sunk into it, and there is no seam to see through.
                for (int level = 1; level <= set.WallHeightInBlocks; level++)
                {
                    Vector3 position = floor + Vector3.up * (cubeHeight * level);

                    // level - 1 so the lowest block keeps the cell's base order and each one above it
                    // draws over the one below, which is the direction a stack overlaps in.
                    Spawn(Roll(set.WallSprites, roll), position, blockScale, baseOrder + level - 1,
                          set.WallTint);
                }
            }
        }

        Bounds = Measure();
        HasBuilt = blocks.Count > 0;
    }

    public void Clear()
    {
        foreach (GameObject block in blocks)
        {
            if (block != null) { Destroy(block); }
        }

        blocks.Clear();
        HasBuilt = false;
        Bounds = default;
    }

    /// <summary>
    /// How much to scale a block so its top face is exactly one cell wide.
    ///
    /// Derived from the sprite rather than assumed, so re-importing the pack at a different
    /// pixels-per-unit changes how detailed the art is without ever desynchronising it from the grid.
    /// At the intended 32 PPU a 64px block is already 2.0 units wide against a 2.0 cell, so this
    /// returns 1.0 and the blocks render at their authored size.
    /// </summary>
    private static float ScaleFor(Sprite sprite)
    {
        if (sprite == null || sprite.pixelsPerUnit <= 0f || sprite.rect.width <= 0f) { return 0f; }

        float spriteWidth = sprite.rect.width / sprite.pixelsPerUnit;

        return GridManager.Instance.CellSize.x / spriteWidth;
    }

    private void Spawn(Sprite sprite, Vector3 position, float scale, int sortingOrder, Color tint)
    {
        if (sprite == null) { return; }

        GameObject block = new(BlockName, typeof(SpriteRenderer));

        block.transform.SetParent(transform, false);
        block.transform.position = position;
        block.transform.localScale = new Vector3(scale, scale, 1f);

        // Layers are not inherited by a new GameObject the way the transform parent is, and the board
        // camera culls by layer - a block left on Default would simply never be drawn.
        block.layer = gameObject.layer;

        SpriteRenderer renderer = block.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerName = SortingLayers.Background;
        renderer.sortingOrder = sortingOrder;

        // Multiplied into the sprite by the renderer rather than baked into the PNG, so the same 101
        // shared blocks can read as a bright stone hall in one level and a dim crypt in the next.
        renderer.color = tint;

        blocks.Add(block);
    }

    /// <summary>
    /// Places (or repositions, on a rebuild) the ambient glow behind the whole board, centred on
    /// `centre` in world space. Kept as a single reused GameObject across rebuilds rather than one
    /// torn down and respawned by Clear() alongside the blocks - a level reload just moves it.
    /// </summary>
    private void BuildBackdrop(Vector3 centre)
    {
        if (backdrop == null)
        {
            backdrop = new GameObject(BackdropName, typeof(SpriteRenderer));
            backdrop.transform.SetParent(transform, false);

            // Same reasoning as Spawn's block.layer line - the board camera culls by layer, not by
            // sorting layer, and a backdrop left on Default would never be drawn.
            backdrop.layer = gameObject.layer;

            SpriteRenderer renderer = backdrop.GetComponent<SpriteRenderer>();
            renderer.sprite = RadialGradientSprite();
            renderer.sortingLayerName = SortingLayers.Background;

            // Below every floor/wall block's order (CellSortingOrder is never negative), so the glow
            // always sits behind the board regardless of how big it is.
            renderer.sortingOrder = -10000;
        }

        backdrop.transform.position = new Vector3(centre.x, centre.y, 0f);
        backdrop.transform.localScale = new Vector3(backdropSize, backdropSize, 1f);
    }

    /// <summary>
    /// A soft radial gradient from glowCenterColor to glowEdgeColor, baked into a small texture rather
    /// than tinted from a shared one - a renderer tint is a single uniform multiply, which cannot blend
    /// between two independent colours the way this glow needs to.
    ///
    /// Normalised by glowRadius in world units rather than by the texture's own corner distance, so the
    /// glow's size is a property of glowRadius alone and does not shift if backdropSize is ever tuned -
    /// backdropSize only has to stay bigger than glowRadius, so the flat glowEdgeColor it fades into has
    /// room to reach every edge of the screen.
    /// </summary>
    private Sprite RadialGradientSprite()
    {
        const int size = 128;

        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        Vector2 centrePixel = new((size - 1) / 2f, (size - 1) / 2f);
        float worldPerPixel = backdropSize / size;
        float radius = Mathf.Max(glowRadius, 0.01f);

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float worldDistance = Vector2.Distance(new Vector2(x, y), centrePixel) * worldPerPixel;
                float t = Mathf.Clamp01(worldDistance / radius);
                texture.SetPixel(x, y, Color.Lerp(glowCenterColor, glowEdgeColor, t));
            }
        }

        texture.Apply();

        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// The union of every block renderer's bounds. Encapsulate from the first renderer rather than
    /// from `default`, because a zero-size Bounds at the origin would drag the union back to include
    /// (0,0) even when the board does not reach it.
    private Bounds Measure()
    {
        bool started = false;
        Bounds total = default;

        foreach (GameObject block in blocks)
        {
            if (block == null) { continue; }

            SpriteRenderer renderer = block.GetComponent<SpriteRenderer>();

            if (renderer == null) { continue; }

            if (!started)
            {
                total = renderer.bounds;
                started = true;
                continue;
            }

            total.Encapsulate(renderer.bounds);
        }

        return total;
    }

    private static Sprite Roll(IReadOnlyList<Sprite> sprites, System.Random roll)
    {
        if (sprites == null || sprites.Count == 0) { return null; }

        // Null entries are skipped rather than rolled and discarded, so a list with a hole in it still
        // fills every cell instead of leaving gaps wherever the hole came up.
        List<Sprite> usable = new();

        foreach (Sprite sprite in sprites)
        {
            if (sprite != null) { usable.Add(sprite); }
        }

        if (usable.Count == 0) { return null; }

        return usable[roll.Next(usable.Count)];
    }

    private static Sprite First(IReadOnlyList<Sprite> sprites)
    {
        if (sprites == null) { return null; }

        foreach (Sprite sprite in sprites)
        {
            if (sprite != null) { return sprite; }
        }

        return null;
    }
}
