using UnityEngine;

/// <summary>
/// Applies a status. One asset type covers every buff and curse in the game - Strengthen is
/// (Strength, 3), Buff is (DoubleNextAttack, 1), and Poison Dart is (Poison, 4) with alliesOnly off.
/// No bespoke code for any of them.
///
/// One number, because a status has one: what it counts is the status's own business, so the same
/// field is a magnitude for Strength, a charge count for Double Attack and a duration for Freeze. See
/// Status.
///
/// Named for what it does rather than what it grants, because `StatusEffect` now means something
/// else: the half of the Status hierarchy a character carries, opposite Aura. This is a CardEffect -
/// the authoring asset that queues the action that applies one.
/// </summary>
[CreateAssetMenu(menuName = "Card Effects/Apply Status")]
public class ApplyStatusEffect : CardEffect
{
    [SerializeField] private StatusType status;

    [Tooltip("What this status counts, which depends on the status: magnitude for Weaken, charges for "
             + "the ones an event spends (Strength included), turns for the ones that wear off, and "
             + "both at once for Poison.")]
    [SerializeField] private int stacks = 1;

    [Tooltip("On for buffs, off for curses.")]
    [SerializeField] private bool alliesOnly = true;

    /// Read-only window onto the private field above, for Card.OutgoingRiders - the same
    /// private-field-plus-property immutability pattern as TargetRange.maxDistance/MaxDistance. Adds
    /// no backing field, so CardSheetImporter's SerializedObject.FindProperty("status") still resolves
    /// exactly as before.
    public StatusType Status => status;

    /// See CardEffect.RiderAmount - this is the one override, since a status's badge shows its stack
    /// count. RiderKind is deliberately left at its base None: OutgoingRiders keys a status rider off
    /// this Status property directly rather than off RiderKind, since StatusIcons already has art for
    /// every StatusType and a second registry entry here would just be a second way to look it up.
    public override int RiderAmount => stacks;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new StatusAction(status, ctx.Amount(stacks)), ctx);
    }

    public override TargetAudience Audience =>
        alliesOnly ? TargetAudience.Ally : TargetAudience.Enemy;
}
