using UnityEngine;

/// <summary>
/// Seeds its Character with an EscapeStatus at battle start - the Evil Wizard blinking clear each
/// time it loses another share of its health.
///
/// A component rather than something a card applies, for the same reason SplitWhenBloodied is one:
/// the rule is true from the first turn and Character has no authored starting-status list to hold it.
///
/// Where it escapes to is EscapeStatus's business, and when a threshold is crossed at all is
/// ThresholdStatus's. All this supplies is how many escapes there are.
/// </summary>
[RequireComponent(typeof(Character))]
public class EscapeWhenHurt : MonoBehaviour
{
    [Tooltip("How many times it escapes across the fight. The thresholds are spread evenly, so 2 "
             + "fires at two thirds and one third of max health.")]
    [Min(1)]
    [SerializeField] private int escapes = 2;

    private void Start()
    {
        Character owner = GetComponent<Character>();

        if (owner == null) { return; }

        owner.AddStatus(new EscapeStatus(escapes));
    }
}
