using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Strips every carried curse off the target and heals it for the total stacks removed - Cleansing
/// Light. CleansePoisonEffect's broader sibling: that one takes Poison alone and can hand it on
/// (Purge), this one takes every status in Curses and only ever heals.
///
/// Counts raw stacks, so a timed curse heals for the turns it had left, not its size - Weaken 3 with
/// two turns to run heals 2.
/// </summary>
[CreateAssetMenu(menuName = "Card Effects/Cleanse Curses")]
public class CleanseEffect : CardEffect
{
    /// What counts as a curse to cleanse. Summoned is a curse in StatusType's own words but is a
    /// summoned ally's lifetime clock - cleansing it would make the body permanent - and Warded is a
    /// boss's immunity, not something a hero ever carries. Both are left out on purpose.
    public static readonly IReadOnlyList<StatusType> Curses = new[]
    {
        StatusType.Poison, StatusType.Frozen, StatusType.Rooted, StatusType.Weaken,
        StatusType.Vulnerable, StatusType.Taunt, StatusType.Pilfered, StatusType.Sapped,
    };

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new CleanseAction(), ctx);
    }

    // Needs a living ally standing there - the same rule Heal and CleansePoisonEffect use.
    public override TargetAudience Audience => TargetAudience.Ally;
}
