using UnityEngine;

/// <summary>
/// Fires once each time the carrier's health crosses another evenly-spaced share of its maximum - the
/// shared spine behind the one-off boss rules (SplitStatus, EscapeStatus), and where a later "enrage
/// below half" would slot in with no new plumbing.
///
/// One crossing rule in one place, deliberately. Each of these rules is trivial on its own and the
/// interesting part is entirely in the bookkeeping: which shares have already been spent, that a
/// single enormous blow crossing two of them at once still only spends one, and that the check reads
/// health *after* mitigation. Writing that three times is how three subtly different boss behaviours
/// happen by accident.
///
/// Thresholds are spread evenly across the bar rather than authored one by one: `triggers` of 2 fires
/// at two thirds and one third, `triggers` of 1 fires at the halfway mark. That is enough for every
/// boss on the roster and keeps the authoring to a single number.
///
/// Never ages and never badges a number - see ShowsCount. These are intrinsic to the body wearing
/// them, so nothing cleanses them either: Character.RemoveStatus is by type, and no card names these.
/// </summary>
public abstract class ThresholdStatus : StatusEffect
{
    private readonly int triggers;
    private int spent;

    protected ThresholdStatus(StatusType type, int triggers) : base(type, 1)
    {
        this.triggers = Mathf.Max(1, triggers);
    }

    /// How many crossings are still to come - what a tooltip reads, since the chip shows no number.
    public int Remaining => Mathf.Max(0, triggers - spent);

    /// One number for a rule whose count would only ever read "1". See Status.ShowsCount.
    public override bool ShowsCount => false;

    public sealed override void OnDamageTaken(Character carrier, DamageInfo info)
    {
        if (carrier == null || spent >= triggers) { return; }

        // The share of the bar this crossing sits at: with two triggers, the first fires at 2/3 and
        // the second at 1/3. Compared against health after mitigation, which is the only number that
        // reflects what the hit actually did.
        float share = (float)(triggers - spent - 1) / triggers;

        if (carrier.Health > Mathf.RoundToInt(carrier.MaxHealth * share)) { return; }

        // Spent before the payload runs, not after: a payload that itself damages the carrier - or
        // kills something that damages it back - must not re-enter this and spend a second crossing
        // on the same blow.
        spent++;

        OnThresholdCrossed(carrier);
    }

    /// What this particular rule does when a share is crossed. Called at most `triggers` times over
    /// the carrier's life, once per crossing, with health already at its new value.
    protected abstract void OnThresholdCrossed(Character carrier);
}
