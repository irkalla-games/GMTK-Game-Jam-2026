using System;
using UnityEngine;
using UnityEngine.UIElements;

public class GridTile : MonoBehaviour
{
    private Vector2Int coordinates;
    private Vector3 worldCoordinates;
    private GameObject tilePrefab;
    private Vector2Int position;

    public GridTile(GameObject tilePrefab, Vector2Int position, Vector3 worldCoordinates)
    {
        this.tilePrefab = tilePrefab;
        this.position = position;
        this.worldCoordinates = worldCoordinates;
        GameObject tile = Instantiate(tilePrefab, worldCoordinates, Quaternion.identity);
    }

    public Vector2Int Coordinates => coordinates;

    public Character Occupant { get; private set; }

    public void SetOccupant(Character character)
    {
        Occupant = character;
    }

    public void DealDamage(int amount)
    {
        if (Occupant != null) { Occupant.TakeDamage(amount); }
    }

    public void Heal(int amount)
    {
        if (Occupant != null) { Occupant.Heal(amount); }
    }

    public void GainShield(int amount)
    {
        if (Occupant != null) { Occupant.AddShield(amount); }
    }

    public void GainBlock(int amount, int count)
    {
        if (Occupant != null) { Occupant.GainBlock(amount, count); }
    }

    public void GainParry(int reflectTotal, int count)
    {
        if (Occupant != null) { Occupant.GainParry(reflectTotal, count); }
    }

    public void MoveCharacter(GridTile moveTo)
    {
        if (Occupant != null) { Occupant.MoveTo(moveTo); }
    }

    public void DrawCards(int drawAmount)
    {
        if (Occupant != null) { Occupant.DrawCards(drawAmount); }
    }

    private void OnMouseDown()
    {
        if (CardPlayManager.Instance != null)
        {
            CardPlayManager.Instance.OnTileClicked(this);
        }
    }


}
