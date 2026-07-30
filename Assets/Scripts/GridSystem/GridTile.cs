using System;
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

    /// <summary>
    /// Routed through GridManager, not straight to Occupant.MoveTo. MoveTo only swaps occupancy
    /// references - it does not move the transform and does not consult MoveRefusal - so calling it
    /// directly leaves the sprite standing on one tile while the board thinks it is on another.
    /// </summary>
    public void MoveCharacter(GridTile moveTo)
    {
        if (Occupant == null || GridManager.Instance == null) { return; }

        GridManager.Instance.MoveCharacter(Occupant, moveTo);
    }

    public void DrawCards(int drawAmount)
    {
        if (Occupant != null) { Occupant.DrawCards(drawAmount); }
    }

    public void ApplyStatus(StatusType status, int stacks, int turnsRemaining)
    {
        if (Occupant != null) { Occupant.AddStatus(status, stacks, turnsRemaining); }
    }

    private void OnMouseDown()
    {
        // BattleManager decides what the click means - playing the selected card, or switching to the
        // character standing here.
        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.OnTileClicked(this);
        }
    }

    public void SummonObject(GameObject summonObject)
    {
        if(Occupant == null)
        {
            GameObject go = Instantiate(summonObject, GridManager.Instance.IsoToWorld(this.coordinates.x, this.coordinates.y), Quaternion.identity);
        }
    }
}
