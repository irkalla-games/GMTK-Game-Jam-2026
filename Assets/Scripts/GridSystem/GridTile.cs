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

    private TileSelector selector;

    public Vector2Int Coordinates => coordinates;

    public Character Occupant { get; private set; }

    private void Awake()
    {
        selector = GetComponent<TileSelector>();
    }

    public void Init(Vector2Int coordinates)
    {
        this.coordinates = coordinates;
    }

    public void SetOccupant(Character character)
    {
        Occupant = character;
    }

    /// Lights this tile up as a legal target for the selected card. The colour itself belongs to
    /// TileSelector, which owns the SpriteRenderer.
    public void SetInRange(bool value)
    {
        if (selector != null) { selector.SetInRange(value); }
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
        // GameManager decides what the click means - playing the selected card, or switching to the
        // character standing here.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnTileClicked(this);
        }
    }
}
