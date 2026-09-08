using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One status this character starts the fight already carrying.
/// </summary>
[Serializable]
public struct StartingStatus
{
    public StatusType type;

    [Tooltip("What the number means is the status's own business - turns for Frozen, damage returned "
             + "for Thorns, pool size for Shield.")]
    [Min(1)]
    public int stacks;
}

/// <summary>
/// Applies a list of statuses to its Character at battle start - the Evil King's permanent Thorns.
///
/// Fills the gap that made WardCycle, SplitWhenBloodied and EscapeWhenHurt each need a bespoke seeder:
/// Character has no authored starting-status list, so anything true from the first turn had nowhere to
/// be written. This is the general form for the statuses that need nothing but a type and a number;
/// the three bespoke seeders remain because each supplies something this cannot - a cycle, a prefab, a
/// trigger count.
///
/// Goes through StatusEffect.Create, so a status that factory cannot build (Taunt, the aura-only ones)
/// is silently skipped rather than half-applied - see that method's own list of exclusions.
/// </summary>
[RequireComponent(typeof(Character))]
public class StartingStatuses : MonoBehaviour
{
    [Tooltip("Applied once, in order, when the battle starts.")]
    [SerializeField] private List<StartingStatus> statuses = new();

    /// Start, not Awake, on the same reasoning as the other seeders: every Awake in the scene has run
    /// by then, so the Character this sits on is fully built before anything is applied to it.
    private void Start()
    {
        if (statuses.Count == 0) { return; }

        Character owner = GetComponent<Character>();

        if (owner == null) { return; }

        foreach (StartingStatus entry in statuses)
        {
            StatusEffect built = StatusEffect.Create(entry.type, entry.stacks);

            if (built != null) { owner.AddStatus(built); }
        }
    }
}
