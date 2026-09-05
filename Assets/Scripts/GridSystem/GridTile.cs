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

    /// Persistent effects this tile itself carries, independent of who is standing on it - a Wall of
    /// Force, a patch of flames. See TileEffect.
    private readonly List<TileEffect> tileEffects = new();

    public IReadOnlyList<TileEffect> TileEffects => tileEffects;

    private TileEffectOverlay effectOverlay;

    private TileWarningOverlay warningOverlay;

    private TileHatchOverlay hatchOverlay;

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

        // The single writer of occupancy, so this alone covers a route going stale from a move, a
        // spawn, or a death freeing a tile - see GridManager.BoardChanged.
        GridManager.BoardChanged();
    }

    /// Lights this tile up as a legal target for the selected card. The colour itself belongs to
    /// TileSelector, which owns the SpriteRenderer.
    public void SetInRange(bool value)
    {
        if (selector != null) { selector.SetInRange(value); }
    }

    /// Lights this tile up as covered by the selected card's area footprint, aimed at the hovered tile.
    /// See TileSelector.SetInArea.
    public void SetInArea(bool value)
    {
        if (selector != null) { selector.SetInArea(value); }
    }

    /// Lights this tile's hover tint on or off from the outside - see TileSelector.SetHovered.
    public void SetHovered(bool value)
    {
        if (selector != null) { selector.SetHovered(value); }
    }

    /// Cross-hatches this tile as in range of the selected card but not a legal click - see
    /// GridManager.ShowPlayableTiles and TileHatchOverlay. Lazily attached like the spawn warning below,
    /// for the same reason: a tile that never hatches never pays for the extra renderer.
    public void SetHatched(bool value)
    {
        if (!value)
        {
            if (hatchOverlay != null) { hatchOverlay.SetActive(false); }
            return;
        }

        if (hatchOverlay == null) { hatchOverlay = TileHatchOverlay.AttachTo(this); }
        if (hatchOverlay != null) { hatchOverlay.SetActive(true); }
    }

    /// Starts or stops this tile's red spawn-warning pulse - see GridManager.ShowSpawnWarning. Lazily
    /// attaches TileWarningOverlay on first use, the same as RefreshEffectOverlay does below for
    /// TileEffectOverlay; a tile that never warns never pays for the extra renderer.
    public void SetSpawnWarning(bool value)
    {
        if (!value)
        {
            if (warningOverlay != null) { warningOverlay.SetActive(false); }
            return;
        }

        if (warningOverlay == null) { warningOverlay = TileWarningOverlay.AttachTo(this); }
        if (warningOverlay != null) { warningOverlay.SetActive(true); }
    }

    /// <summary>
    /// Stabs this tile red once, as an enemy attack covering it resolves - see BattleManager.Execute
    /// and TileWarningOverlay.Flash. Lazily attaches the same overlay the spawn warning uses, so a tile
    /// never carries two renderers for two kinds of red and the sorting budget stays where it is.
    ///
    /// No bool and no clearing counterpart, unlike every other visual on this class: the stab has a
    /// fixed length and ends itself.
    /// </summary>
    public void FlashThreat()
    {
        if (warningOverlay == null) { warningOverlay = TileWarningOverlay.AttachTo(this); }
        if (warningOverlay != null) { warningOverlay.Flash(); }
    }

    public void DealDamage(int amount, Character attacker = null)
    {
        if (Occupant != null)
        {
            Occupant.TakeDamage(amount, attacker);
            return;
        }

        // Nobody here to hit - most often because they dodged away since this attack was aimed. Still
        // worth a "0" so the swing (which has already played by the time this runs - see
        // GameAction.Perform, which aims at the tile itself, never the occupant) doesn't read as having
        // vanished into nothing.
        if (FloatingTextManager.Instance != null) { FloatingTextManager.Instance.ShowMiss(this); }
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

    /// `count` is how many hits the Block applies to. How much comes off each is
    /// BlockStatus.AmountPerHit, the same for every Block in the game.
    public void GainBlock(int count)
    {
        if (Occupant != null) { Occupant.AddStatus(StatusType.Block, count); }
    }

    public void GainParry(int count)
    {
        if (Occupant != null) { Occupant.AddStatus(StatusType.Parry, count); }
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

    /// `amountBonus` is what the applier's equipment adds to the size of the status - see
    /// Character.AppliedPotency. Defaults to none, so every caller that has no applier to ask (a tile
    /// effect, a status a status grants) keeps building exactly what it did before.
    public void ApplyStatus(StatusType status, int stacks, int amountBonus = 0)
    {
        if (Occupant != null) { Occupant.AddStatus(StatusEffect.Create(status, stacks, amountBonus)); }
    }

    /// Taunt is the one that cannot go through the type/stacks form - it carries a reference to the
    /// character being taunted *by*, which StatusEffect.Create has nowhere to put. A null taunter would
    /// be a taunt pointing at nobody, so it is refused here rather than becoming a status that can
    /// never be satisfied. `stacks` is how many turns it lasts.
    public void Taunt(Character taunter, int stacks)
    {
        if (Occupant == null || taunter == null) { return; }

        Occupant.AddStatus(new TauntStatus(taunter, stacks));
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

    /// <summary>
    /// Applies a tile effect, merging into one of the same type already here - the same
    /// merge-by-type shape Character.AddStatus uses. Lazily attaches the overlay wash on the first
    /// effect and tints it from whichever effect is oldest, which is also the only one today's two
    /// wall cards would ever let coexist with a second of a different kind on the same tile.
    /// </summary>
    public void AddTileEffect(TileEffect incoming)
    {
        if (incoming == null || incoming.type == TileEffectType.None || incoming.turnsRemaining <= 0)
        {
            return;
        }

        foreach (TileEffect existing in tileEffects)
        {
            if (existing.type != incoming.type) { continue; }

            existing.Merge(incoming);
            RefreshEffectOverlay();
            return;
        }

        tileEffects.Add(incoming);
        RefreshEffectOverlay();
        GridManager.BoardChanged();
    }

    /// Why a tile effect on this tile refuses to let `mover` step here, or null if none object. Asked
    /// by GridManager.MoveRefusal, the same choke point a status's own MoveRefusal already answers
    /// through.
    public string EnterRefusal(Character mover)
    {
        foreach (TileEffect effect in tileEffects)
        {
            string refusal = effect.EnterRefusal(mover, this);

            if (refusal != null) { return refusal; }
        }

        return null;
    }

    /// Runs every tile effect's end-of-turn hook, then drops whatever just expired. See
    /// GridManager.TickTileEffects - called once per round, at the end of the player turn, since a
    /// tile belongs to nobody's "own phase" the way a character's statuses do.
    public void TickTileEffects()
    {
        foreach (TileEffect effect in tileEffects) { effect.OnTurnEnd(this); }

        tileEffects.RemoveAll(effect => effect.IsExpired);
        RefreshEffectOverlay();
        GridManager.BoardChanged();
    }

    private void RefreshEffectOverlay()
    {
        if (tileEffects.Count == 0)
        {
            if (effectOverlay != null) { effectOverlay.Clear(); }
            return;
        }

        if (effectOverlay == null) { effectOverlay = TileEffectOverlay.AttachTo(this); }
        if (effectOverlay != null) { effectOverlay.SetTint(tileEffects[0].OverlayColor); }
    }
}
