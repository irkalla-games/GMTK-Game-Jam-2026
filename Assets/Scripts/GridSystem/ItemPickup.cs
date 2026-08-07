using UnityEngine;

/// <summary>
/// Sits on a tile after a character drops it - see Character.ItemDropPrefab and
/// GridTile.DropItem/TryPickUpItem. Anybody can stand on the same tile as one of these; only a
/// player-controlled character moving onto the tile actually picks it up.
///
/// Rarity is rolled and applied at drop time (GridTile.DropItem), not at pickup - so the tier is
/// visible on the board and worth racing an enemy for. Table travels with it so a stolen item still
/// remembers what it should be biased toward if a hero later recovers it.
/// </summary>
public class ItemPickup : MonoBehaviour
{
    [SerializeField] private string itemName;

    [SerializeField] private SpriteRenderer tintTarget;

    [SerializeField] private SpriteRenderer badge;

    [SerializeField] private RarityStyle style;

    public string ItemName => itemName;

    public Rarity Rarity { get; private set; }

    public LootTable Table { get; private set; }

    /// Applies the rolled tier's look and remembers the table it came from. Called once, right after
    /// Instantiate, by GridTile.DropItem.
    public void Configure(Rarity rarity, LootTable table)
    {
        Rarity = rarity;
        Table = table;

        if (style == null) { return; }

        if (tintTarget != null) { tintTarget.color = style.TintFor(rarity); }
        if (badge != null) { badge.sprite = style.BadgeFor(rarity); }
    }
}
