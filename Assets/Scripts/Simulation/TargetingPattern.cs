using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How an enemy chooses between the targets it could legally hit.
///
/// Weakest = 0 on purpose, and this is load-bearing three times over: an entry left at its dropdown
/// default, a TargetingPattern asset with an empty list, and a Character with no pattern assigned at
/// all must every one of them behave exactly like the hardcoded "ties break toward the most hurt"
/// TryFindAttack has always used. Same trick as RangeShape.Anywhere = 0 - the all-zero value is the
/// old behaviour, so authoring a pattern is opt-in and no existing enemy changes silently.
///
/// Written into .asset files. Append, never reorder.
/// </summary>
public enum TargetPriority
{
    Weakest = 0,
    Closest = 1,
    Furthest = 2,
    Random = 3,
    Strongest = 4,
}

/// <summary>
/// An ordered list of who an enemy goes for, cycled through as it attacks.
///
/// The asset is the *type*, shared by every skeleton on the board - so it holds no cursor. How far
/// through the sequence any one skeleton is lives on that skeleton (Character.targetingCursor),
/// exactly as a Card's cost lives on the Card and not on the CardData. Writing a cursor here would
/// edit the .asset file on disk the first time you exited Play Mode.
/// </summary>
[CreateAssetMenu(menuName = "Enemies/Targeting Pattern")]
public class TargetingPattern : ScriptableObject
{
    [Tooltip("Consumed by attacks only. A Move or a Summon reads the current entry - it is who the "
        + "enemy walks toward - without spending it.")]
    [SerializeField] private List<TargetPriority> sequence = new();

    /// Wraps. An empty sequence answers Weakest, which is the behaviour every enemy already had.
    public TargetPriority At(int cursor)
    {
        if (sequence.Count == 0) { return TargetPriority.Weakest; }

        int index = cursor % sequence.Count;

        return sequence[index < 0 ? index + sequence.Count : index];
    }
}
