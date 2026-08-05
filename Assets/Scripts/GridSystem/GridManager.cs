using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class GridManager : Singleton<GridManager>
{
    [Header("Grid Settings")]
    [SerializeField] private int width = 5;
    [SerializeField] private int height = 6;

    [Header("Tile Setup")]
    [SerializeField] private GameObject tilePrefab;
    [SerializeField] private Transform tileParent;

    private readonly Dictionary<Vector2Int, GridTile> tiles = new();

    /// Live telegraph lines, torn down and rebuilt each turn.
    private readonly List<LineRenderer> telegraphs = new();


    protected override void Awake()
    {
        base.Awake();

        // base.Awake() destroys a duplicate and returns, but returning from it does not return from
        // here - without this guard a second GridManager would build a whole second grid on its way
        // out. Any Singleton subclass that overrides Awake needs the same check.
        if (Instance != this) { return; }

        CreateGrid();
    }


    private void CreateGrid()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int position = new Vector2Int(x, y);

                // Instantiate the prefab, then attach/init a GridTile component on the real GameObject.
                // GridTile is a MonoBehaviour, so it must live on an instance - never `new`'d.
                GameObject go = Instantiate(tilePrefab, tileParent);
                go.transform.position = IsoToWorld(position.x, position.y);

                GridTile tile = go.GetComponent<GridTile>();
                if (tile == null) { tile = go.AddComponent<GridTile>(); }
                tile.Init(position);

                tiles.Add(position, tile);
            }
        }
    }


    public GridTile GetTile(Vector2Int position)
    {
        return tiles.GetValueOrDefault(position);
    }


    public bool IsTileAvailable(Vector2Int position)
    {
        GridTile tile = GetTile(position);

        if (tile == null)
            return false;

        return tile.Occupant == null;
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
        character.transform.DOMove(destination.transform.position, .15f);

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
    /// Draws what an enemy has committed to doing: a line from it to the tile its card is aimed at.
    ///
    /// Red when the tile has somebody on it, amber when it does not - which distinguishes a swing
    /// from a walk without needing to know anything about the card. That is the same trick the brains
    /// use to tell attacks from moves: what a card does is settled by where it is legal, not by a
    /// label on it.
    ///
    /// Here rather than in a TelegraphViewer of its own - that would be two public methods, which is
    /// a function looking for a file rather than a concept. GridManager is already what draws on the
    /// board, and this is the same job as ShowPlayableTiles with a different reason.
    ///
    /// Deliberately a *separate* channel from the tile highlight: telegraphs have to survive the
    /// player selecting and deselecting a card, and ClearPlayableTiles resets every tile.
    /// </summary>
    public void ShowIntent(Character enemy, Intent intent)
    {
        if (enemy == null || enemy.Tile == null || intent.IsWait) { return; }

        GridTile tile = GetTile(intent.target);

        if (tile == null) { return; }

        List<Vector3> points = new() { enemy.Tile.transform.position, tile.transform.position };

        telegraphs.Add(DrawTelegraph(points, tile.Occupant != null));
    }


    public void ClearIntents()
    {
        foreach (LineRenderer line in telegraphs)
        {
            if (line != null) { Destroy(line.gameObject); }
        }

        telegraphs.Clear();
    }


    /// <summary>
    /// One telegraph line. Built in code rather than from a prefab so there is nothing to wire in the
    /// Inspector and nothing to lose to a scene reload - the whole thing is created and destroyed
    /// inside a turn.
    ///
    /// Sprites/Default is the one shader guaranteed present in a 2D project that respects vertex
    /// color, so the line does not come out magenta without a material authored for it.
    /// </summary>
    private LineRenderer DrawTelegraph(List<Vector3> points, bool isAttack)
    {
        GameObject go = new("Telegraph");
        go.transform.SetParent(transform);

        LineRenderer line = go.AddComponent<LineRenderer>();
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.widthMultiplier = isAttack ? 0.16f : 0.1f;
        line.numCapVertices = 4;
        line.useWorldSpace = true;

        // Above the characters it is drawn between, but still under the hand - a telegraph is board
        // information, and a card you are reading should never be cut in half by one.
        line.sortingLayerName = SortingLayers.Characters;
        line.sortingOrder = 100;

        Color colour = isAttack ? new Color(1f, 0.25f, 0.2f, 0.9f) : new Color(1f, 0.85f, 0.3f, 0.75f);
        line.startColor = colour;
        line.endColor = new Color(colour.r, colour.g, colour.b, colour.a * 0.35f);

        line.positionCount = points.Count;

        // Nudged toward the camera so the line sits over the tiles rather than z-fighting them.
        for (int i = 0; i < points.Count; i++)
        {
            line.SetPosition(i, points[i] + Vector3.back * 0.5f);
        }

        return line;
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
        }

        return board;
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

    
    public Vector3 IsoToWorld(int x, int y)
    {
        float tileWidth = 3f;
        float tileHeight = 1.5f;

        return new Vector3(
            ((x - y) * tileWidth / 2) + 2.1f,
            ((x + y) * tileHeight / 2) + 1.7f,
            0);
    }
    
}
