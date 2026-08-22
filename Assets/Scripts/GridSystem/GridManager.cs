using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class GridManager : Singleton<GridManager>
{
    [Header("Grid Settings")]
    [Tooltip("Board size used when the level does not specify one. LevelData.BoardSize wins wherever "
             + "it is authored.")]
    [SerializeField] private int width = 5;
    [SerializeField] private int height = 6;

    [Tooltip("World size of one cell's top face - the pitch the board is laid out on, not the size of "
             + "the sprite that draws it. x must be twice y for a 2:1 isometric board, which is the "
             + "shape every block in the art pack is drawn at. 2.0 x 1.0 is one 64px block at 32 "
             + "pixels per unit, so blocks render at scale 1.")]
    [SerializeField] private Vector2 cellSize = new(2f, 1f);

    [Tooltip("Outline drawn around every tile. The tiles meet edge to edge now, so without it the "
             + "board reads as one continuous wash with no visible cell boundaries. Alpha 0 turns it "
             + "off.")]
    [SerializeField] private Color tileBorderColor = new(0f, 0f, 0f, 0.45f);

    [Tooltip("Border thickness as a fraction of a cell's half-width, measured horizontally. 0.05 is "
             + "about 6 pixels of the source diamond's 128.")]
    [Range(0.005f, 0.3f)]
    [SerializeField] private float tileBorderThickness = 0.05f;

    [Header("Tile Setup")]
    [SerializeField] private GameObject tilePrefab;
    [SerializeField] private Transform tileParent;

    [Tooltip("Builds the block floor and the wall along the two far edges. Lives on the same object "
             + "the tiles are parented to, so the whole board tears down together.")]
    [SerializeField] private BoardVisuals boardVisuals;

    public Vector2 CellSize => cellSize;

    /// <summary>
    /// The board this level actually built, as opposed to the serialized fallback above.
    ///
    /// BuildGrid used to throw its argument away, which was fine while nothing needed it. The wall
    /// ring, the depth sort and the camera fit all do: every one of them is a question about the
    /// board as a whole rather than about any one tile, and deriving the extent by walking the
    /// dictionary would answer it differently the moment the board stops being a full rectangle.
    /// </summary>
    public Vector2Int BoardSize { get; private set; }

    /// <summary>
    /// Sorting orders reserved per cell. A cell needs more than one: its tile sits above the floor
    /// block, its aura and tile-effect overlays sit just below the tile, and a stacked wall needs one
    /// order per block. Four is enough for all of that with room to spare, and keeps consecutive
    /// cells from ever colliding.
    /// </summary>
    public const int DepthStride = 4;

    /// <summary>
    /// How far forward a cell is - 0 at the very back, rising toward the camera.
    ///
    /// `+x` runs up-right and `+y` up-left, so `x + y` grows *away* from the camera and this is its
    /// complement. Anything drawn on the board sorts by it: a cube overlaps what is behind it, so
    /// back-to-front is the only order that does not have a tile painting over the one in front.
    ///
    /// Accepts the phantom wall cells at `x == BoardSize.x` and `y == BoardSize.y` too - it is plain
    /// arithmetic on the board extent, not a dictionary lookup, precisely so the wall ring can share
    /// one depth rule with the floor instead of inventing a second one.
    /// </summary>
    public int CellDepth(Vector2Int cell) => (BoardSize.x + BoardSize.y) - (cell.x + cell.y);

    /// The base sorting order for everything drawn on `cell`. Sub-layers offset from here - see the
    /// stride above.
    public int CellSortingOrder(Vector2Int cell) => CellDepth(cell) * DepthStride;

    [Tooltip("How long a step's tween takes. MoveAction waits this exact number rather than the "
             + "unrelated ActionManager pacing delay, which used to only coincidentally match it, and "
             + "holds the walk animation for the same length. The old 0.15 was tuned for a character "
             + "that teleported between tiles with no animation to read.")]
    [SerializeField] private float moveDuration = 0.4f;

    public float MoveDuration => moveDuration;

    private readonly Dictionary<Vector2Int, GridTile> tiles = new();


    // No Awake override. The grid used to be built here, which needed a `if (Instance != this) return;`
    // guard so a duplicate GridManager did not build a second board on its way to being destroyed.
    // BuildGrid is called explicitly now, by whoever knows the size, so there is nothing left to guard
    // and Singleton.Awake is enough on its own.


    /// <summary>
    /// Builds the board at the size this level asked for, replacing whatever was there.
    ///
    /// Called by BattleManager.Start rather than from Awake here, because the size comes from the
    /// level and the level comes from the run - neither of which is resolved until BattleManager has
    /// had a chance to bootstrap one. Nothing may touch a tile before that call: it is the first thing
    /// BattleManager does, ahead of SpawnParty and SpawnEnemies, which both need tiles to exist.
    ///
    /// A size of zero or less on either axis falls back to the serialized width/height, which keeps a
    /// LevelData authored before boardSize existed - Level1 is one - building the board it always did.
    /// </summary>
    public void BuildGrid(Vector2Int size, TileSetData tileSet = null, int seed = 0)
    {
        ClearGrid();

        BoardSize = new Vector2Int(size.x > 0 ? size.x : width, size.y > 0 ? size.y : height);

        // Before the tiles, and before the visuals: both ask CellSortingOrder, which is derived from
        // BoardSize. Setting it after would sort the whole board against the *previous* level's size.
        CreateGrid(BoardSize.x, BoardSize.y);

        if (boardVisuals != null) { boardVisuals.Build(BoardSize, tileSet, seed); }
    }


    /// <summary>
    /// The footprint the camera has to fit: every floor block, the skirt hanging below the front
    /// edge, and the wall standing above the back two.
    ///
    /// Measured from the built visuals wherever they exist, because the cube skirt and the wall
    /// height are properties of the art rather than of the cell grid - a taller wall has to move the
    /// camera and nothing but the renderers knows how tall it ended up. Falls back to the flat ring
    /// of tile centres when there is no floor art, so a board with an unauthored tileset still frames
    /// sensibly instead of collapsing to a point.
    /// </summary>
    public Bounds BoardBounds
    {
        get
        {
            if (boardVisuals != null && boardVisuals.HasBuilt) { return boardVisuals.Bounds; }

            Bounds flat = new(IsoToWorld(0, 0), Vector3.zero);

            foreach (GridTile tile in tiles.Values)
            {
                flat.Encapsulate(tile.transform.position);
            }

            // The tile centres alone describe a diamond one cell too small on every side, since a
            // centre is half a cell in from the edge it sits on.
            flat.Expand(new Vector3(cellSize.x, cellSize.y, 0f));

            return flat;
        }
    }


    /// <summary>
    /// Tears the board down: tile objects and the lookup that pointed at them.
    ///
    /// Destroys by walking `tiles` rather than tileParent's children, so anything else parented there
    /// is left alone and a null tileParent is not a crash. Occupants are not touched - characters are
    /// destroyed with the scene, and a board rebuild that also killed the party would be a very
    /// surprising thing for a method called ClearGrid to do.
    /// </summary>
    private void ClearGrid()
    {
        foreach (GridTile tile in tiles.Values)
        {
            if (tile != null) { Destroy(tile.gameObject); }
        }

        tiles.Clear();
    }


    private void CreateGrid(int columns, int rows)
    {
        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                Vector2Int position = new Vector2Int(x, y);

                // Instantiate the prefab, then attach/init a GridTile component on the real GameObject.
                // GridTile is a MonoBehaviour, so it must live on an instance - never `new`'d.
                GameObject go = Instantiate(tilePrefab, tileParent);
                go.transform.position = IsoToWorld(position.x, position.y);
                go.transform.localScale = TileScale(go);

                GridTile tile = go.GetComponent<GridTile>();
                if (tile == null) { tile = go.AddComponent<GridTile>(); }
                tile.Init(position);

                // Back to front, same rule the floor blocks under it use. Without this every tile
                // sits at order 0 and Unity falls back to distance, which for a flat 2D board is a
                // coin toss - and a tile drawn over the one in front of it shows through the cube
                // standing on it.
                SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
                if (renderer != null) { renderer.sortingOrder = CellSortingOrder(position); }

                // After the sorting order is set, not before: the border takes its own order from the
                // tile's, so it would otherwise stack itself against a stale 0.
                if (tileBorderColor.a > 0f)
                {
                    TileBorder.AttachTo(tile, tileBorderColor, tileBorderThickness);
                }

                tiles.Add(position, tile);
            }
        }
    }


    /// <summary>
    /// Scales a tile so its diamond is exactly one cell, rather than trusting the scale authored on
    /// the prefab.
    ///
    /// The prefab carries 2.5, which was right when a cell was 3.0 x 1.5 and wrong the moment the
    /// pitch changed - the diamonds came out 25% oversized and overlapped their neighbours. Deriving
    /// it means cellSize is the single number that decides how big a cell is, and the highlight can
    /// never disagree with the floor block underneath it again.
    ///
    /// Derived from the sprite the same way BoardVisuals.ScaleFor derives the block scale. Scaling the
    /// transform also scales the PolygonCollider2D, so what you can click stays exactly what you can
    /// see. Falls back to the authored scale if there is no sprite to measure.
    /// </summary>
    private Vector3 TileScale(GameObject tile)
    {
        SpriteRenderer renderer = tile.GetComponent<SpriteRenderer>();

        if (renderer == null || renderer.sprite == null) { return tile.transform.localScale; }

        Sprite sprite = renderer.sprite;

        if (sprite.pixelsPerUnit <= 0f || sprite.rect.width <= 0f) { return tile.transform.localScale; }

        float scale = cellSize.x / (sprite.rect.width / sprite.pixelsPerUnit);

        return new Vector3(scale, scale, 1f);
    }


    public GridTile GetTile(Vector2Int position)
    {
        return tiles.GetValueOrDefault(position);
    }

    /// <summary>
    /// Every tile on the board, in no meaningful order - Dictionary.Values makes no ordering promise, so
    /// anything that cares must sort by Coordinates itself.
    ///
    /// Exposed because `tiles` is private and callers that want to ask a question of the whole board
    /// otherwise have to walk a coordinate range and GetTile each cell, which quietly assumes the board
    /// is rectangular and gapless. The tutorial spotlight is the current caller: "every tile this card
    /// may be aimed at" is exactly that kind of question.
    /// </summary>
    public IEnumerable<GridTile> AllTiles => tiles.Values;


    public bool IsTileAvailable(Vector2Int position)
    {
        GridTile tile = GetTile(position);

        if (tile == null)
            return false;

        return tile.Occupant == null;
    }


    /// <summary>
    /// Whether this cell sits on the outer edge of the board - it is missing at least one of its four
    /// cardinal neighbours.
    ///
    /// Derived from the tile dictionary rather than from a stored size, because there is no stored
    /// size to trust: BuildGrid throws the dimensions it was handed away, and the serialized
    /// width/height above are only the fallback, so they say nothing about a board a level sized
    /// itself. Deriving needs no second copy to keep in sync, and it stays correct if the board ever
    /// stops being a plain rectangle.
    /// </summary>
    public bool IsBorderCell(Vector2Int cell)
    {
        if (!tiles.ContainsKey(cell)) { return false; }

        return !tiles.ContainsKey(cell + Vector2Int.up)
            || !tiles.ContainsKey(cell + Vector2Int.down)
            || !tiles.ContainsKey(cell + Vector2Int.left)
            || !tiles.ContainsKey(cell + Vector2Int.right);
    }


    /// <summary>
    /// Where a body aimed at `cell` can actually stand: `cell` itself when it is free, otherwise the
    /// nearest free *border* tile. Null when there is nowhere - the caller is expected to give up on
    /// the spawn rather than stack two characters on one tile.
    ///
    /// The authored cell wins outright even when it is interior, because it is distance 0 from itself
    /// and "as close to where it was authored as possible" is the whole rule. The border restriction
    /// is on the search for somewhere else: reinforcements arriving mid-battle come in from the edge,
    /// not out of thin air in the middle of a fight.
    ///
    /// Here rather than on BattleManager because `tiles` is private and there is no way to enumerate
    /// the board from outside - which cells exist and which are free is the board's own question.
    /// Note this is deliberately not MoveRefusal: that asks whether a character already on the board
    /// may walk somewhere, and consults its statuses. Nobody exists yet to be Rooted.
    /// </summary>
    public GridTile NearestFreeSpawnTile(Vector2Int cell)
    {
        if (IsTileAvailable(cell)) { return GetTile(cell); }

        GridTile best = null;
        int bestDistance = int.MaxValue;

        foreach (KeyValuePair<Vector2Int, GridTile> entry in tiles)
        {
            if (entry.Value.Occupant != null) { continue; }

            if (!IsBorderCell(entry.Key)) { continue; }

            Vector2Int step = entry.Key - cell;
            int distance = Mathf.Abs(step.x) + Mathf.Abs(step.y);

            if (distance > bestDistance) { continue; }

            // Ties broken on coordinates, never on which one the dictionary happened to hand back
            // first: enumeration order is not something to lean on, and the same board being asked
            // the same question twice should answer the same way both times.
            if (best != null && distance == bestDistance && !IsEarlier(entry.Key, best.Coordinates))
            {
                continue;
            }

            best = entry.Value;
            bestDistance = distance;
        }

        return best;
    }


    /// Lower x first, then lower y. Only exists to make NearestFreeSpawnTile's tie-break deterministic.
    private static bool IsEarlier(Vector2Int a, Vector2Int b) => a.x != b.x ? a.x < b.x : a.y < b.y;

    /// Clockwise from north, index 0. Index i and index i+4 (mod 8) are always opposite each other,
    /// which StepAwayFrom leans on to build each distance tier as a pair of indices.
    private static readonly Vector2Int[] ClockwiseSteps =
    {
        new(0, 1), new(1, 1), new(1, 0), new(1, -1),
        new(0, -1), new(-1, -1), new(-1, 0), new(-1, 1),
    };

    /// <summary>
    /// Where a status like Dodge relocates its carrier to: the neighbour tile directly opposite
    /// `awayFrom`, ranked farthest-from-`awayFrom` first. The 8 neighbours form 4 tiers by ring-distance
    /// from the opposite direction - {opposite alone}, then its two ring-neighbours, then the next two
    /// out, then the last two, deliberately excluding the direction pointing straight at `awayFrom`
    /// itself (always occupied by it, so never a legal candidate anyway). Each tier is tried in full
    /// before falling to the next; a two-tile tier picks between them at random. Null once no tier has
    /// a legal tile - MoveRefusal decides legality, so Rooted, occupied neighbours and board edges are
    /// already handled exactly as an ordinary move would be. `awayFrom` null (sourceless damage) starts
    /// the ranking due south instead of computing a direction.
    /// </summary>
    public GridTile StepAwayFrom(Character carrier, GridTile awayFrom)
    {
        if (carrier == null || carrier.Tile == null) { return null; }

        Vector2Int origin = carrier.Tile.Coordinates;
        int oppositeIndex = 4;

        if (awayFrom != null)
        {
            int dx = System.Math.Sign(origin.x - awayFrom.Coordinates.x);
            int dy = System.Math.Sign(origin.y - awayFrom.Coordinates.y);

            int found = System.Array.IndexOf(ClockwiseSteps, new Vector2Int(dx, dy));
            if (found >= 0) { oppositeIndex = found; }
        }

        List<GridTile> tier = new();

        for (int radius = 0; radius <= 3; radius++)
        {
            tier.Clear();

            int a = (oppositeIndex + radius) % 8;
            int b = (oppositeIndex - radius + 8) % 8;

            AddIfLegal(tier, carrier, origin + ClockwiseSteps[a]);
            if (b != a) { AddIfLegal(tier, carrier, origin + ClockwiseSteps[b]); }

            if (tier.Count > 0) { return tier[Random.Range(0, tier.Count)]; }
        }

        return null;
    }

    private void AddIfLegal(List<GridTile> into, Character carrier, Vector2Int cell)
    {
        GridTile candidate = GetTile(cell);

        if (candidate != null && MoveRefusal(carrier, candidate) == null) { into.Add(candidate); }
    }


    public bool MoveCharacter(Character character, GridTile destination)
    {
        string refusal = MoveRefusal(character, destination);

        if (refusal != null)
        {
            string who = character != null ? character.name : "nobody";
            string where = destination != null ? destination.Coordinates.ToString() : "nowhere";
            Debug.LogWarning($"cannot move {who} to {where}: {refusal}");
            return false;
        }

        // MoveTo already clears the old tile and claims the new one. Doing it here too would throw
        // on a character that has not been placed on the board yet (Tile is still null).
        character.MoveTo(destination);
        character.transform.DOMove(destination.transform.position, moveDuration);

        destination.TryPickUpItem(character);

        return true;
    }


    /// <summary>
    /// Drops a character onto a cell outright: no tween, no move rules, no pickup. Placement, not
    /// movement - it is how a character arrives on the board in the first place, whether at battle
    /// start or freshly summoned mid-turn.
    ///
    /// Here rather than on Character for the same reason MoveCharacter is: where a tile *is* in world
    /// space is the board's business. Character.MoveTo only swaps occupancy references, so a caller
    /// that wants a body to actually appear somewhere would otherwise have to reach for
    /// `transform.position = tile.transform.position` itself - which is a character knowing how the
    /// grid is laid out.
    /// </summary>
    public bool PlaceCharacter(Character character, Vector2Int cell)
    {
        GridTile tile = GetTile(cell);

        if (character == null || tile == null) { return false; }

        character.MoveTo(tile);
        character.transform.position = tile.transform.position;

        return true;
    }


    /// <summary>
    /// Swaps two characters' places outright - Shambles. Deliberately consults no refusal at all: a
    /// swap is something done TO both characters, not a move either one is choosing to make, so a
    /// Rooted target and a Wall of Force both stand aside for it. GridManager's business for the same
    /// reason MoveCharacter and PlaceCharacter are - where a tile sits in world space, and the
    /// occupancy bookkeeping that goes with it.
    ///
    /// Both tiles are cleared before either character claims one: Character.MoveTo only clears a tile
    /// whose Occupant still points at the mover, so calling it straight through would have the second
    /// MoveTo see the first character's stale reference sitting on the tile it is about to leave.
    /// Clearing both up front sidesteps that regardless of which order the two MoveTo calls run in.
    /// </summary>
    public bool SwapCharacters(Character a, Character b)
    {
        if (a == null || b == null || a == b || a.Tile == null || b.Tile == null) { return false; }

        GridTile tileA = a.Tile;
        GridTile tileB = b.Tile;

        tileA.SetOccupant(null);
        tileB.SetOccupant(null);

        a.MoveTo(tileB);
        b.MoveTo(tileA);

        a.transform.DOMove(tileB.transform.position, moveDuration);
        b.transform.DOMove(tileA.transform.position, moveDuration);

        // Each character lands on a tile it was not just standing on, so both may have something to
        // pick up - unlike an ordinary move, where only the single destination matters.
        tileB.TryPickUpItem(a);
        tileA.TryPickUpItem(b);

        return true;
    }


    /// <summary>
    /// Every rule about where a character may move lives here. Null means the move is legal, anything
    /// else is the reason it was refused, for the caller to log.
    ///
    /// Public and static so MoveEffect can ask it *before* the card is paid for, without needing
    /// GridManager.Instance. Range is deliberately not checked here: this answers a board question -
    /// may this character stand here - while range is a card question. Knockback, teleports and enemy
    /// repositioning will all reuse this and none of them know about a card.
    /// </summary>
    public static string MoveRefusal(Character character, GridTile destination)
    {
        if (character == null) { return "there is nobody to move"; }

        if (destination == null) { return "there is no destination tile"; }

        if (destination.Occupant == character) { return "it is already standing there"; }

        if (destination.Occupant != null) { return $"{destination.Occupant.name} is standing there"; }

        // A tile effect may object to itself - Wall of Force refuses every mover, occupant or not.
        string tileRefusal = destination.EnterRefusal(character);
        if (tileRefusal != null) { return tileRefusal; }

        // A status may object to the mover rather than to the tile - Rooted refuses every destination.
        // Asked here rather than anywhere else precisely because this method is the single answer both
        // the pre-flight check and the move itself consult, so a rooted character's Move card lights
        // no tiles and its click costs no energy.
        foreach (Status status in character.ActiveStatuses())
        {
            string refusal = status.MoveRefusal(character, destination);

            if (refusal != null) { return refusal; }
        }

        return null;
    }


    /// <summary>
    /// Tints every tile this card could legally be played on, and clears the rest.
    ///
    /// Built from Card.Refusal - the exact predicate the click itself is gated on - so the highlight
    /// cannot promise a tile that a click would then refuse. A tile within range but occupied by
    /// somebody else stays dark, because Move's own rule rejects it.
    /// </summary>
    public void ShowPlayableTiles(Card card, Character source)
    {
        if (card == null || source == null)
        {
            ClearPlayableTiles();
            return;
        }

        List<GridTile> playable = new();

        foreach (GridTile tile in tiles.Values)
        {
            if (card.Refusal(source, tile) == null) { playable.Add(tile); }
        }

        // A card with no restriction at all is legal on every tile, and lighting the whole board is
        // noise rather than information - the raised card in hand already says one is selected. Note
        // this asks what the card actually refuses, not just its range: Fireball may be aimed anywhere
        // on the board but only at an enemy, so its handful of legal tiles do get lit.
        if (playable.Count == tiles.Count)
        {
            ClearPlayableTiles();
            return;
        }

        foreach (GridTile tile in tiles.Values) { tile.SetInRange(false); }

        foreach (GridTile tile in playable) { tile.SetInRange(true); }
    }


    public void ClearPlayableTiles()
    {
        foreach (GridTile tile in tiles.Values) { tile.SetInRange(false); }
    }


    /// <summary>
    /// Reddens every tile that would actually be affected if `card` were played on `hovered` right
    /// now - the aiming preview. Raw geometry from Card.AreaFootprint, not filtered by who is standing
    /// where: the shape is what the player needs to see to aim it, same as ShowPlayableTiles shows every
    /// legal tile rather than only the ones with something worth hitting on them.
    ///
    /// A card with no area entries paints nothing, which is exactly right - the existing yellow hover
    /// tint already marks a single-target card's one tile, and TileSelector's priority ladder puts hover
    /// above area anyway, so a Single entry would be invisible here even if it were included.
    /// </summary>
    public void ShowAreaPreview(Card card, Character source, GridTile hovered)
    {
        ClearAreaPreview();

        if (card == null || hovered == null) { return; }

        foreach (GridTile tile in card.AreaFootprint(source, hovered)) { tile.SetInArea(true); }
    }


    public void ClearAreaPreview()
    {
        foreach (GridTile tile in tiles.Values) { tile.SetInArea(false); }
    }


    /// Characters currently showing a damage preview - tracked so ClearDamagePreview can drop exactly
    /// those rather than walking every occupied tile on the board on every hover change.
    private readonly List<Character> damagePreviewed = new();

    /// <summary>
    /// Shows a projected health loss on every character `card` would actually damage if played on
    /// `hovered` right now - the numeric sibling of ShowAreaPreview's red footprint. Reads
    /// Card.PreviewDamage, which has already run the full outgoing/incoming pipeline with nothing
    /// spent, and forwards each result to that character's own CharacterOverheadViewer - the same
    /// "push, don't ask" shape ShowPlayableTiles uses to drive TileSelector.
    /// </summary>
    public void ShowDamagePreview(Card card, Character source, GridTile hovered)
    {
        ClearDamagePreview();

        if (card == null || source == null || hovered == null) { return; }

        foreach (KeyValuePair<Character, int> entry in card.PreviewDamage(source, hovered))
        {
            CharacterOverheadViewer viewer = entry.Key.GetComponent<CharacterOverheadViewer>();
            if (viewer == null) { continue; }

            viewer.SetDamagePreview(entry.Value);
            damagePreviewed.Add(entry.Key);
        }
    }


    public void ClearDamagePreview()
    {
        foreach (Character character in damagePreviewed)
        {
            if (character == null) { continue; }

            CharacterOverheadViewer viewer = character.GetComponent<CharacterOverheadViewer>();
            if (viewer != null) { viewer.ClearDamagePreview(); }
        }

        damagePreviewed.Clear();
    }


    /// <summary>
    /// Drops the hover tint from every tile, leaving the range highlight alone. Called by
    /// BattleManager when input locks: a tile lit under the cursor gets no OnMouseExit when a modal
    /// opens over it, because the cursor never actually moved.
    ///
    /// Here rather than on BattleManager for the same reason ShowPlayableTiles is - `tiles` is
    /// private and there is no way to enumerate the board from outside.
    /// </summary>
    public void ClearHoveredTiles()
    {
        foreach (GridTile tile in tiles.Values) { tile.SetHovered(false); }
    }


    /// Tiles currently pulsing as a wave's spawn warning - tracked so ClearSpawnWarning turns off
    /// exactly those rather than walking the whole board, the same shape damagePreviewed uses for
    /// ClearDamagePreview.
    private readonly List<GridTile> spawnWarned = new();

    /// <summary>
    /// Pulses red every tile an enemy arriving at `cell` would threaten: the corner it lands in plus
    /// its two orthogonal neighbours for a corner spawn, the whole row or column for a plain edge
    /// spawn, or just the tile itself anywhere else. Driven by WaveCircle hovering a wave preview
    /// circle - see NextWavePanel.
    ///
    /// Corner and edge come from BoardSize rather than IsBorderCell: IsBorderCell only answers "missing
    /// a neighbour", which cannot tell a corner from a plain edge the way this needs to.
    /// </summary>
    public void ShowSpawnWarning(Vector2Int cell)
    {
        ClearSpawnWarning();

        bool onXEdge = cell.x == 0 || cell.x == BoardSize.x - 1;
        bool onYEdge = cell.y == 0 || cell.y == BoardSize.y - 1;

        if (onXEdge && onYEdge)
        {
            AddWarned(cell);
            AddWarned(cell + Vector2Int.up);
            AddWarned(cell + Vector2Int.down);
            AddWarned(cell + Vector2Int.left);
            AddWarned(cell + Vector2Int.right);
        }
        else if (onXEdge)
        {
            for (int y = 0; y < BoardSize.y; y++) { AddWarned(new Vector2Int(cell.x, y)); }
        }
        else if (onYEdge)
        {
            for (int x = 0; x < BoardSize.x; x++) { AddWarned(new Vector2Int(x, cell.y)); }
        }
        else
        {
            AddWarned(cell);
        }
    }


    public void ClearSpawnWarning()
    {
        foreach (GridTile tile in spawnWarned)
        {
            if (tile != null) { tile.SetSpawnWarning(false); }
        }

        spawnWarned.Clear();
    }


    private void AddWarned(Vector2Int at)
    {
        GridTile tile = GetTile(at);

        if (tile == null) { return; }

        tile.SetSpawnWarning(true);
        spawnWarned.Add(tile);
    }


    /// <summary>
    /// A coordinates-only snapshot of the board for the enemy brains.
    ///
    /// Built fresh each time it is asked for rather than cached: an enemy decides against the board
    /// as it stands the moment it acts, and a snapshot held across a turn is exactly the staleness
    /// the design is trying to make visible rather than accidental.
    ///
    /// This is the seam that keeps brains testable - past this method there is no MonoBehaviour, no
    /// GridTile, and nothing that needs a scene to exist.
    /// </summary>
    public Board Read()
    {
        Board board = new();

        foreach (KeyValuePair<Vector2Int, GridTile> entry in tiles)
        {
            board.AddCell(entry.Key);

            Character occupant = entry.Value.Occupant;

            if (occupant != null && !occupant.IsDead)
            {
                board.SetOccupant(entry.Key, occupant.Affiliation);
            }

            // A Wall of Force blocks pathing the same way an occupant does, without being one - asked
            // through EnterRefusal(null) rather than a bespoke query so the board never disagrees with
            // what an actual move would be refused for.
            if (entry.Value.EnterRefusal(null) != null) { board.SetBlocked(entry.Key); }
        }

        return board;
    }


    /// <summary>
    /// Ticks every tile's own effects by one round - Wall of Flames burns, both walls age. Called once
    /// per round, at the end of the player turn (see BattleManager.RunBattle), since a tile belongs to
    /// nobody's "own phase" the way TickStatuses splits by side.
    ///
    /// Here rather than on BattleManager for the same reason Read() is - `tiles` is private and there
    /// is no way to enumerate the board from outside.
    /// </summary>
    public void TickTileEffects()
    {
        foreach (GridTile tile in tiles.Values) { tile.TickTileEffects(); }
    }


    /// Every tile a card with this range, cast from `start`, could be aimed at. The metric itself
    /// lives in TargetRange, so this and the play-time gate can never drift apart.
    public List<GridTile> GetTilesInRange(GridTile start, TargetRange range)
    {
        List<GridTile> results = new();

        foreach (GridTile tile in tiles.Values)
        {
            if (range.Contains(start, tile)) { results.Add(tile); }
        }

        return results;
    }


    /// Every tile a footprint aimed from `caster` toward `aim` covers. The metric itself lives in
    /// AreaShape, so this and Card.ResolveEffects can never drift apart, the same relationship
    /// GetTilesInRange has with TargetRange.
    public List<GridTile> GetTilesInArea(GridTile caster, GridTile aim, AreaShape area)
    {
        List<GridTile> results = new();

        if (aim == null) { return results; }

        Vector2Int casterCoord = caster != null ? caster.Coordinates : aim.Coordinates;

        foreach (GridTile tile in tiles.Values)
        {
            if (area.Covers(casterCoord, aim.Coordinates, tile.Coordinates)) { results.Add(tile); }
        }

        return results;
    }

    
    /// <summary>
    /// Where a cell sits in world space.
    ///
    /// The 2:1 isometric mapping: `+x` runs up-right and `+y` up-left, which is what puts the party
    /// on the right of the screen and the enemies on the left given how the levels are authored. That
    /// orientation is load-bearing - every level's spawn cells were placed against it - so the signs
    /// here are not free to change.
    ///
    /// Cell (0,0) sits at the world origin. There is deliberately no origin offset any more: the old
    /// `+1.1 / +2.6` was tuned to centre one particular 5x6 board under a camera that could not move,
    /// so every other size drifted - the 3x6 tutorial board visibly sat left of centre. Framing is
    /// BoardCamera's job now, and it can only do it if the board is somewhere predictable.
    /// </summary>
    public Vector3 IsoToWorld(int x, int y)
    {
        return new Vector3(
            (x - y) * cellSize.x / 2f,
            (x + y) * cellSize.y / 2f,
            0f);
    }

}
