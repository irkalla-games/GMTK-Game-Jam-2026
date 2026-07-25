using UnityEngine;

/// <summary>
/// Skeleton. Everything a card can do to a character arrives through the tile it's standing on, so
/// these are the entry points Tiles forwards to. Combat rules (how block/shield/parry actually reduce
/// incoming damage, death, statuses) are not implemented here.
/// </summary>
public class Character : MonoBehaviour
{
    [SerializeField] private int maxHealth = 10;

    [SerializeField] private int maxEnergy = 3;

    [SerializeField] private bool isPlayerControlled;

    public int Health { get; private set; }

    /// Each character has their own pool; playing a card spends the acting character's energy.
    public int Energy { get; private set; }

    public bool IsPlayerControlled => isPlayerControlled;

    public bool IsDead => Health <= 0;

    /// The tile this character is standing on.
    public GridTile Tile { get; private set; }

    public void SetTile(GridTile tile)
    {
        if (Tile != null && Tile.Occupant == this) { Tile.SetOccupant(null); }
        Tile = tile;
        if (tile != null) { tile.SetOccupant(this); }
    }

    public bool CanAfford(int cost) => cost <= Energy;

    public void SpendEnergy(int cost)
    {
        Energy = Mathf.Max(0, Energy - cost);
    }

    public void ResetEnergy()
    {
        Energy = maxEnergy;
    }

    //TODO: block/shield/parry mitigation. Right now damage goes straight to health.
    public void TakeDamage(int amount) { Health = Mathf.Max(0, Health - amount); }

    public void Heal(int amount) { Health = Mathf.Min(maxHealth, Health + amount); }

    public void AddShield(int amount) { }

    public void GainBlock(int amount, int count) { }

    public void GainParry(int reflectTotal, int count) { }

    public void MoveTo(GridTile moveTo)
    {

    }

    //TODO: there is one shared deck and hand right now, so this draws into it regardless of who asked.
    //Once characters own their own decks this routes to this character's pile instead.
    public void DrawCards(int amount)
    {
        if (GameManager.Instance != null) { GameManager.Instance.DrawCards(amount); }
    }

    private void Awake()
    {
        Health = maxHealth;
        Energy = maxEnergy;
    }
}
