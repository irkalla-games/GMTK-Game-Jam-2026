using UnityEngine;

/// <summary>
/// Splits the carrier in two when its health crosses the halfway mark, with what was left divided
/// between the halves - the Reinforced Golem cracking apart.
///
/// Carries the prefab to spawn rather than looking one up: a Status is a plain C# object, so the
/// reference is handed in by SplitWhenBloodied, the component on the prefab that seeds this. That
/// split - authored data on the component, the rule in the status - is the same one WardCycle and
/// WardStatus already use.
///
/// The half that walks away has its own seeder switched off, so a fight ends in two Golems rather
/// than an ever-finer spray of them.
/// </summary>
public class SplitStatus : ThresholdStatus
{
    private readonly GameObject halfPrefab;

    public SplitStatus(GameObject halfPrefab, int triggers = 1)
        : base(StatusType.Splitting, triggers)
    {
        this.halfPrefab = halfPrefab;
    }

    protected override void OnThresholdCrossed(Character carrier)
    {
        if (halfPrefab == null || carrier.IsDead || carrier.Tile == null) { return; }
        if (GridManager.Instance == null) { return; }

        GridTile room = GridManager.Instance.NearestFreeSpawnTile(carrier.Tile.Coordinates);

        if (room == null || room == carrier.Tile) { return; }

        // Halved before either body is written, so both read the same number whichever order they are
        // inspected in. Minimum of 1: a split at 1 health must not spawn a corpse.
        int each = Mathf.Max(1, carrier.Health / 2);

        Character other = room.SummonObject(halfPrefab);

        if (other == null) { return; }

        carrier.SetHealth(each);
        other.SetHealth(each);

        // The new half is a fresh copy of the same prefab and would happily split again at its own
        // halfway mark. Spending its seeder here keeps the two bodies genuinely identical rather than
        // needing a separate "already split" prefab.
        if (other.TryGetComponent(out SplitWhenBloodied theirs)) { theirs.Disarm(); }

        Debug.Log($"{carrier.name} split into two at {each} health each");
    }

    public override string Describe() => "Splits in two at half health";
}
