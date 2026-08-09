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

    [Header("Tile Setup")]
    [SerializeField] private GameObject tilePrefab;
    [SerializeField] private Transform tileParent;

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
    public void BuildGrid(Vector2Int size)
    {
        ClearGrid();

        CreateGrid(size.x > 0 ? size.x : width, size.y > 0 ? size.y : height);
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
