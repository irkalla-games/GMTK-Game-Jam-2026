using UnityEngine;

/// <summary>
/// A board tile. Tiles are the only thing cards target; a tile forwards what happens to it onto the
/// character currently standing on it. Keeping that indirection here is what lets an action say
/// "damage this tile" without knowing whether anything is standing there.
///
/// Skeleton: occupancy is settable but nothing places characters yet.
/// </summary>
public class Tiles : MonoBehaviour
{
    [SerializeField] private Vector2Int coordinates;

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

    public void addShield(int amount)
    {
        if (Occupant != null) { Occupant.AddShield(amount); }
    }

    public void gainBlock(int amount, int count)
    {
        if (Occupant != null) { Occupant.GainBlock(amount, count); }
    }

    public void gainParry(int reflectTotal, int count)
    {
        if (Occupant != null) { Occupant.GainParry(reflectTotal, count); }
    }

    private void OnMouseDown()
    {
        if (CardPlayManager.Instance != null)
        {
            CardPlayManager.Instance.OnTileClicked(this);
        }
    }
}
