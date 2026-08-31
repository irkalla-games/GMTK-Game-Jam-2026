using UnityEngine;

/// <summary>
/// A hand-painted area-of-effect footprint - a cone, a T, an X, whatever shape a designer draws in the
/// grid inspector. Referenced from an AreaShape on a card's effect entry, so the same painted shape can
/// be reused by any card that wants a cone (Pattern.cs authors it once).
///
/// Two grids, not one, because rotating a painted grid by 45 degrees is not lattice-preserving - it
/// leaves holes and doubled cells. `cardinalCells` is authored pointing Up and rotates in exact 90
/// degree steps to cover Up/Right/Down/Left. `diagonalCells` is authored pointing UpRight and likewise
/// rotates in exact 90 degree steps to cover the other four. Left unpainted, a diagonal aim falls back
/// to whichever cardinal grid rotation is nearest - so a cone with only cardinalCells drawn is still
/// usable in every direction, just not diagonally precise.
///
/// `anchor` is the cell that represents the tile actually clicked - a cone typically anchors near one
/// edge so the shape extends away from the caster, while a burst anchors in the middle.
/// </summary>
[CreateAssetMenu(menuName = "Card/Effect Pattern")]
public class EffectPattern : ScriptableObject
{
    [SerializeField] private int width = 5;
    [SerializeField] private int height = 5;

    [Tooltip("Which painted cell is the tile the player actually clicked. Every other cell is read "
             + "relative to this one.")]
    [SerializeField] private Vector2Int anchor = new(2, 2);

    [Tooltip("Painted facing Up (+Y). Rotated in 90 degree steps to cover Up/Right/Down/Left.")]
    [SerializeField] private bool[] cardinalCells;

    [Tooltip("Painted facing Up-Right (+X+Y). Rotated in 90 degree steps to cover the four diagonals. "
             + "Leave empty to fall back to the nearest cardinal rotation instead.")]
    [SerializeField] private bool[] diagonalCells;

    public int Width => width;
    public int Height => height;
    public Vector2Int Anchor => anchor;

    /// Editor-only. Moves which painted cell represents the clicked tile.
    public void SetAnchor(Vector2Int value)
    {
        anchor = new Vector2Int(Mathf.Clamp(value.x, 0, width - 1), Mathf.Clamp(value.y, 0, height - 1));
    }

    /// <summary>
    /// Resizes both grids in the Editor, preserving whatever painted cells still fall inside the new
    /// bounds. The old OnEnable-based auto-init wiped the pattern on every width/height edit - this is
    /// the fix, and it is deliberately the only place either array is written outside the Inspector.
    /// </summary>
    public void Resize(int newWidth, int newHeight)
    {
        cardinalCells = ResizeGrid(cardinalCells, width, height, newWidth, newHeight);
        diagonalCells = ResizeGrid(diagonalCells, width, height, newWidth, newHeight);
        anchor = new Vector2Int(Mathf.Clamp(anchor.x, 0, newWidth - 1), Mathf.Clamp(anchor.y, 0, newHeight - 1));
        width = newWidth;
        height = newHeight;
    }

    private static bool[] ResizeGrid(bool[] cells, int oldW, int oldH, int newW, int newH)
    {
        bool[] result = new bool[newW * newH];

        if (cells == null) { return result; }

        int copyW = Mathf.Min(oldW, newW);
        int copyH = Mathf.Min(oldH, newH);

        for (int y = 0; y < copyH; y++)
        {
            for (int x = 0; x < copyW; x++)
            {
                result[y * newW + x] = cells[y * oldW + x];
            }
        }

        return result;
    }

    /// Editor-only paint access. Never called at runtime - GetCell/SetCell used to lazily reallocate
    /// the backing array on read, which is a write to a ScriptableObject and, in the Editor, persists
    /// into the .asset the moment anything reads a pattern during Play Mode.
    public bool GetCardinalCell(int x, int y) => GetCell(cardinalCells, x, y);

    public void SetCardinalCell(int x, int y, bool value) => SetCell(ref cardinalCells, x, y, value);

    public bool GetDiagonalCell(int x, int y) => GetCell(diagonalCells, x, y);

    public void SetDiagonalCell(int x, int y, bool value) => SetCell(ref diagonalCells, x, y, value);

    private bool GetCell(bool[] cells, int x, int y)
    {
        if (cells == null || cells.Length != width * height) { return false; }
        if (x < 0 || x >= width || y < 0 || y >= height) { return false; }

        return cells[y * width + x];
    }

    private void SetCell(ref bool[] cells, int x, int y, bool value)
    {
        if (cells == null || cells.Length != width * height) { cells = new bool[width * height]; }
        if (x < 0 || x >= width || y < 0 || y >= height) { return; }

        cells[y * width + x] = value;
    }

    /// <summary>
    /// True if `cell` (world/board coordinates) is covered by this pattern when aimed from `caster`
    /// toward `aim`. Pure coordinate math - no GridTile - so EnemyBrain can score a pattern against
    /// Board the same way the player-facing highlight does.
    /// </summary>
    public bool Covers(Vector2Int caster, Vector2Int aim, Vector2Int cell) => Covers(caster, aim, cell, -1);

    /// <summary>
    /// As above, but with the facing the player has locked in - see Card.AimOctant. `octantOverride` is
    /// an *absolute* facing (0-7, as SnapOctant numbers them) that replaces the caster->aim direction
    /// outright; -1 means "no rotation", deriving the facing from that direction exactly as the overload
    /// above does.
    ///
    /// Absolute rather than a relative quarter-turn count, on purpose. A relative offset gets re-applied
    /// on top of a freshly computed base direction every time the cursor moves, so a wall the player had
    /// turned vertical would snap back to horizontal the moment they aimed at a tile on a different
    /// bearing. Locking the facing is the whole reason a rotation survives re-aiming.
    /// </summary>
    public bool Covers(Vector2Int caster, Vector2Int aim, Vector2Int cell, int octantOverride)
    {
        int octant = octantOverride >= 0 ? octantOverride % 8 : SnapOctant(aim - caster);
        Vector2Int worldOffset = cell - aim;

        bool isDiagonalOctant = octant % 2 == 1;

        if (isDiagonalOctant && HasAny(diagonalCells))
        {
            int turns = (octant - 1) / 2;
            return LookUp(diagonalCells, RotateCcw(worldOffset, turns));
        }

        // Cardinal octant, or a diagonal aim with no diagonal grid painted - snap to the nearer
        // cardinal instead of leaving the pattern unusable on a diagonal aim.
        int cardinalOctant = isDiagonalOctant ? NearestCardinalOctant(octant) : octant;
        int cardinalTurns = cardinalOctant / 2;

        return LookUp(cardinalCells, RotateCcw(worldOffset, cardinalTurns));
    }

    /// How far the furthest painted cell (either grid) sits from the anchor, in tile steps. What an
    /// enemy brain reads as this pattern's threat radius, and what the card-face icon sizes itself to.
    public int MaxReach()
    {
        int reach = 0;
        reach = Mathf.Max(reach, FurthestCell(cardinalCells));
        reach = Mathf.Max(reach, FurthestCell(diagonalCells));
        return reach;
    }

    private int FurthestCell(bool[] cells)
    {
        if (cells == null) { return 0; }

        int furthest = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!cells[y * width + x]) { continue; }

                int dx = Mathf.Abs(x - anchor.x);
                int dy = Mathf.Abs(y - anchor.y);
                furthest = Mathf.Max(furthest, Mathf.Max(dx, dy));
            }
        }

        return furthest;
    }

    private bool LookUp(bool[] cells, Vector2Int localOffset)
    {
        return GetCell(cells, anchor.x + localOffset.x, anchor.y + localOffset.y);
    }

    private static bool HasAny(bool[] cells)
    {
        if (cells == null) { return false; }

        foreach (bool cell in cells)
        {
            if (cell) { return true; }
        }

        return false;
    }

    /// Rotates a world-space offset backward into pattern-local space, undoing `turns` clockwise
    /// quarter turns - the inverse of how a painted offset is rotated forward to aim it.
    private static Vector2Int RotateCcw(Vector2Int v, int turns)
    {
        for (int i = 0; i < ((turns % 4) + 4) % 4; i++)
        {
            v = new Vector2Int(-v.y, v.x);
        }

        return v;
    }

    /// <summary>
    /// Which of 8 grid directions `direction` is nearest to, measured clockwise from Up (octant 0):
    /// 0 Up, 1 UpRight, 2 Right, 3 DownRight, 4 Down, 5 DownLeft, 6 Left, 7 UpLeft. Even octants are
    /// cardinal, odd are diagonal - that parity is what Covers uses to pick which grid to read.
    /// </summary>
    /// The public face of SnapOctant. Card needs it to work out which absolute facing a fresh rotation
    /// should start turning from - the one the footprint is showing right now, before the player's first
    /// press of the rotate key locks it.
    public static int OctantOf(Vector2Int direction) => SnapOctant(direction);

    private static int SnapOctant(Vector2Int direction)
    {
        if (direction == Vector2Int.zero) { return 0; }

        float angle = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
        if (angle < 0f) { angle += 360f; }

        return Mathf.RoundToInt(angle / 45f) % 8;
    }

    /// The cardinal octant immediately clockwise-before an odd (diagonal) one - the deterministic
    /// fallback used when no diagonal grid is painted. UpRight(1)->Up(0), DownRight(3)->Right(2), and
    /// so on; picking the earlier cardinal every time rather than rounding keeps it predictable instead
    /// of alternating direction on a tie.
    private static int NearestCardinalOctant(int oddOctant) => oddOctant - 1;
}
