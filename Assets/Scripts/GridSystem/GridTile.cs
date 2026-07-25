using UnityEngine;

/// <summary>
/// One board tile. Cards target tiles; a tile forwards what happens to it onto its Occupant.
///
/// This is a component that lives on an instantiated tile GameObject - never `new`'d. GridManager
/// instantiates the prefab and calls Init() to give the tile its grid coordinates.
/// </summary>
public class GridTile : MonoBehaviour
{
    private Vector2Int coordinates;

    public Vector2Int Coordinates => coordinates;

    public Character Occupant { get; private set; }

    public void Init(Vector2Int coordinates)
    {
        this.coordinates = coordinates;
    }

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

    private void OnMouseDown()
    {
        if (CardPlayManager.Instance != null)
        {
            CardPlayManager.Instance.OnTileClicked(this);
        }
    }
}
