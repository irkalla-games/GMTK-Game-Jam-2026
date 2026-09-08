using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Seeds its Character with a WardStatus at battle start - the Dread Sorcerer's rotating immunity.
///
/// A component rather than something a card applies, because there is no card: the ward is true from
/// the first turn, and Character has no authored starting-status list to put it in. Same shape as
/// Totem, which is likewise a rule authored on the prefab beside the Character it belongs to.
///
/// All this does is supply the list and apply it once. Which subject is live, and when it rotates,
/// is WardStatus's own business - see its class comment for why the rule lives there.
/// </summary>
[RequireComponent(typeof(Character))]
public class WardCycle : MonoBehaviour
{
    [Tooltip("Statuses this character is immune to, one at a time, rotating at the end of each of its "
             + "turns. Order is the rotation order.")]
    [SerializeField] private List<StatusType> cycle = new();

    /// Start, not Awake, for the same reason Totem subscribes in Start: every Awake in the scene has
    /// run by then, so the Character this sits on is fully built before anything is applied to it.
    private void Start()
    {
        if (cycle.Count == 0) { return; }

        Character owner = GetComponent<Character>();

        if (owner == null) { return; }

        owner.AddStatus(new WardStatus(cycle));
    }
}
