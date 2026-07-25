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

                GridTile tile = new GridTile(tilePrefab, position, IsoToWorld(position.x, position.y));

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
        if (!CanMove(character, destination))
            return false;


        character.Tile.SetOccupant(null);
        destination.SetOccupant(character);

        character.MoveTo(destination);
        character.transform.DOMove(destination.transform.position, .15f);

        return true;
    }


    private bool CanMove(Character character, GridTile destination)
    {
        if (destination.Occupant != null)
            return false;

        return true;
    }


    public List<GridTile> GetTilesInRange(
        GridTile start,
        int range)
    {
        List<GridTile> results = new();

        foreach (GridTile tile in tiles.Values)
        {
            int distance =
                Mathf.Abs(tile.Coordinates.x - start.Coordinates.x)
                +
                Mathf.Abs(tile.Coordinates.y - start.Coordinates.y);


            if (distance <= range)
            {
                results.Add(tile);
            }
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
