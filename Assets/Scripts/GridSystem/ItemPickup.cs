using UnityEngine;

/// <summary>
/// Sits on a tile after a character drops it - see Character.ItemDropPrefab and
/// GridTile.DropItem/TryPickUpItem. Anybody can stand on the same tile as one of these; only a
/// player-controlled character moving onto the tile actually picks it up.
/// </summary>
public class ItemPickup : MonoBehaviour
{
    [SerializeField] private string itemName;

    public string ItemName => itemName;
}
