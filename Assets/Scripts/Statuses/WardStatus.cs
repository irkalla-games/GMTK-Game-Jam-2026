using System.Collections.Generic;

/// <summary>
/// Immune to one status at a time, rotating through a cycle at the end of each of the carrier's turns.
///
/// The refusal rides Character.AddStatus's OnGainStatus pipeline: answering WithStacks(0) makes
/// AddStatus drop the application outright, so a warded status never lands at all rather than landing
/// small. That is the same pipeline GainMultiplier and GainBonus scale through - this one is the first
/// to cancel rather than resize.
///
/// The rotation lives here rather than in the component that seeds it, on the same reasoning every
/// other rule in this folder follows: a status is a behaviour, and "which one am I warding right now"
/// is this behaviour's own business. WardCycle only supplies the list and applies it once.
///
/// Never expires - stacks is left alone in OnTurnEnd, unlike Frozen which spends one per turn. A boss
/// that stopped being warded halfway through a fight would just be a boss with a confusing icon.
/// </summary>
public class WardStatus : StatusEffect
{
    private readonly List<StatusType> cycle;
    private int index;

    public WardStatus(IEnumerable<StatusType> cycle, int stacks = 1) : base(StatusType.Warded, stacks)
    {
        this.cycle = new List<StatusType>();

        foreach (StatusType type in cycle)
        {
            if (type != StatusType.None && type != StatusType.Warded) { this.cycle.Add(type); }
        }
    }

    /// What this ward is refusing right now. StatusType.None when nothing usable was authored, which
    /// makes every hook below inert rather than throwing on an empty list.
    public StatusType Subject =>
        cycle.Count == 0 ? StatusType.None : cycle[index % cycle.Count];

    /// The chip shows which status is warded, not a count - there is only ever one ward. See
    /// Status.ShowsCount.
    public override bool ShowsCount => false;

    public override StatusGainInfo OnGainStatus(StatusGainInfo info)
    {
        StatusType subject = Subject;

        if (subject == StatusType.None || info.type != subject) { return info; }

        return info.WithStacks(0);
    }

    /// Rotates at the end of the carrier's own turn, so the ward a player reads during their turn is
    /// the one that will still be up when their cards resolve.
    public override void OnTurnEnd(Character carrier)
    {
        if (cycle.Count > 0) { index = (index + 1) % cycle.Count; }
    }

    public override string Describe() =>
        Subject == StatusType.None ? "Warded" : $"Warded vs {Subject}";
}
