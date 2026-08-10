using System;
using System.Collections.Generic;
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

    /// Dropped items sitting on this tile, separate from Occupant - a character stands on the same
    /// tile as an item rather than being blocked by it. A list, not a single slot: an enemy carrying
    /// stolen loot drops both its own table's roll and whatever it was carrying when it dies on the
    /// same tile as its own drop.
    private readonly List<ItemPickup> items = new();

    public IReadOnlyList<ItemPickup> Items => items;

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

    /// Lights this tile's hover tint on or off from the outside - see TileSelector.SetHovered.
    public void SetHovered(bool value)
    {
        if (selector != null) { selector.SetHovered(value); }
    }

    public void DealDamage(int amount, Character attacker = null)
    {
        if (Occupant != null) { Occupant.TakeDamage(amount, attacker); }
    }

    public void Heal(int amount)
    {
        if (Occupant != null) { Occupant.Heal(amount); }
    }

    // Shield, Block and Parry are statuses like anything else - these are the same forwarders they
    // always were, they just hand the occupant a status instead of calling a bespoke method per stat.

    public void GainShield(int amount)
    {
        if (Occupant != null) { Occupant.GainShield(amount); }
    }

    /// Block is the one that cannot go through the type/stacks form: `amount` comes off each hit and
    /// `count` is how many hits it applies to, which is two numbers - see BlockStatus.
    public void GainBlock(int amount, int count)
    {
        if (Occupant == null || amount <= 0 || count <= 0) { return; }

        Occupant.AddStatus(new BlockStatus(amount, count, Status.Indefinite));
    }

    public void GainParry(int count)
    {
        if (Occupant != null) { Occupant.AddStatus(StatusType.Parry, count, Status.Indefinite); }
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

    /// <summary>
    /// Returns the summoned Character (or null on a bad prefab / an already-occupied tile), so a
    /// caller can play a spawn-in animation on the exact body that just appeared - see SummonAction.
    /// </summary>
    public Character SummonObject(GameObject summonObject)
    {
        if (Occupant != null) { return null; }

        GameObject go = Instantiate(summonObject, GridManager.Instance.IsoToWorld(coordinates.x, coordinates.y), Quaternion.identity);

        Character character = go.GetComponent<Character>();

        if (character == null) { return null; }

        // Instantiate's position argument only sets the transform - Character.Start() runs at end of
        // frame and re-places itself onto whatever startCoordinates it was authored with, which would
        // otherwise silently drag a fresh summon back to (0, 0). PlaceOnGrid writes startCoordinates
        // first, same trick BattleManager.SpawnEnemies already relies on.
        character.PlaceOnGrid(coordinates);

        if (BattleManager.Instance != null) { BattleManager.Instance.AddCharacter(character); }

        return character;
    }

    /// Spawns an item on this tile, e.g. loot from a character that just died here or loot it was
    /// carrying. Appends rather than overwriting - a character killed while holding stolen loot drops
    /// that alongside its own roll, and both sit here until someone walks onto the tile.
    public void DropItem(GameObject itemPrefab, Rarity rarity, LootTable table)
    {
        if (itemPrefab == null) { return; }

        GameObject go = Instantiate(itemPrefab, GridManager.Instance.IsoToWorld(coordinates.x, coordinates.y), Quaternion.identity);
        ItemPickup pickup = go.GetComponent<ItemPickup>();

        if (pickup == null)
        {
            Debug.LogWarning($"{itemPrefab.name} has no ItemPickup component - dropped nothing usable "
                             + $"on {coordinates}");
            Destroy(go);
            return;
        }

        pickup.Configure(rarity, table);
        items.Add(pickup);
    }

    /// <summary>
    /// Called after a character finishes moving onto this tile. A player-controlled character queues a
    /// reward choice per item with LootManager - resolved later, once the whole card has finished, not
    /// inline here. Anybody else just steals it: enemies are meant to be able to walk loot off the
    /// board without ever seeing a choice screen.
    /// </summary>
    public void TryPickUpItem(Character character)
    {
        if (items.Count == 0 || character == null) { return; }

        foreach (ItemPickup pickup in items)
        {
            if (character.IsPlayerControlled)
            {
                if (LootManager.Instance != null)
                {
                    LootManager.Instance.QueuePickup(character, pickup.Rarity, pickup.Table);
                }
                else
                {
                    Debug.LogWarning($"{character.name} stepped on {pickup.ItemName} but no LootManager "
                                     + "is in the scene - the reward is lost with no choice shown.");
                }
            }
            else
            {
                character.CarryLoot(pickup.Rarity, pickup.Table);
            }

            if (pickup != null) { Destroy(pickup.gameObject); }
        }

        items.Clear();
    }
}
