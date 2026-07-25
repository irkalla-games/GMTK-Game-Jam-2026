using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;
using DG.Tweening;

public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    [Header("Grid Settings")]
    [SerializeField] private int width = 5;
    [SerializeField] private int height = 6;

    [Header("Tile Setup")]
    [SerializeField] private GameObject tilePrefab;
    [SerializeField] private Transform tileParent;

    private Dictionary<Vector2Int, GridTile> tiles = new();


    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

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
        if (tiles.TryGetValue(position, out GridTile tile))
        {
            return tile;
        }

        return null;
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

    
    Vector3 IsoToWorld(int x, int y)
    {
        float tileWidth = 2.6f;
        float tileHeight = 1.4f;

        return new Vector3(
            ((x - y) * tileWidth / 2) + 3,
            ((x + y) * tileHeight / 2) + 2.5f,
            0);
    }
    
}
