using UnityEngine;

/// <summary>
/// Seeds its Character with a SplitStatus at battle start - the Reinforced Golem's crack at the
/// halfway mark.
///
/// A component rather than something a card applies, because there is no card: the rule is true from
/// the first turn, and Character has no authored starting-status list to put it in. It also has to
/// hold the prefab reference, which a plain C# Status cannot. Same shape as WardCycle.
///
/// When the split happens is SplitStatus's business, and when a *threshold* is crossed at all is
/// ThresholdStatus's - see those two. All this does is supply the prefab and apply the status once.
/// </summary>
[RequireComponent(typeof(Character))]
public class SplitWhenBloodied : MonoBehaviour
{
    [Tooltip("What each half spawns as - normally this same prefab. Leave empty to disable the split.")]
    [SerializeField] private GameObject halfPrefab;

    [Tooltip("Off on a half that was itself split off, so the split does not cascade. SplitStatus "
             + "spends this on whatever it spawns.")]
    [SerializeField] private bool armed = true;

    /// Spends this seeder before it ever runs - what SplitStatus calls on the half it spawns, since
    /// that half is a fresh copy of the same prefab and would otherwise split again at its own
    /// halfway mark.
    public void Disarm() => armed = false;

    /// Start, not Awake, for the same reason Totem subscribes in Start: every Awake in the scene has
    /// run by then, so the Character this sits on is fully built before anything is applied to it.
    /// SplitStatus disarms a freshly spawned half during the spawning frame, which is before that
    /// half's own Start - so the check here still sees the disarm.
    private void Start()
    {
        if (!armed || halfPrefab == null) { return; }

        Character owner = GetComponent<Character>();

        if (owner == null) { return; }

        owner.AddStatus(new SplitStatus(halfPrefab));
    }
}
