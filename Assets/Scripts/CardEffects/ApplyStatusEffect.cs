using UnityEngine;

/// <summary>
/// Applies a status. One asset type covers every buff and curse in the game - Strengthen is
/// (Strength, 3, Indefinite), Buff is (DoubleNextAttack, 1, Indefinite), and Poison Dart is
/// (Poison, 3, 3 turns) with alliesOnly off. No bespoke code for any of them.
///
/// Named for what it does rather than what it grants, because `StatusEffect` now means something
/// else: the half of the Status hierarchy a character carries, opposite Aura. This is a CardEffect -
/// the authoring asset that queues the action that applies one.
/// </summary>
[CreateAssetMenu(menuName = "Card Effects/Apply Status")]
public class ApplyStatusEffect : CardEffect
{
    [SerializeField] private StatusType status;

    [Tooltip("Magnitude, or number of charges for statuses an event spends rather than time.")]
    [SerializeField] private int stacks = 1;

    [Tooltip("Turns before it wears off. -1 lasts the whole combat, which is what Strength and "
             + "Double Attack want.")]
    [SerializeField] private int turnsRemaining = Status.Indefinite;

    [Tooltip("On for buffs, off for curses.")]
    [SerializeField] private bool alliesOnly = true;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new StatusAction(status, stacks, turnsRemaining), ctx);
    }

    public override string Refusal(Character source, GridTile target) =>
        RefuseByOccupant(source, target, wantAlly: alliesOnly);
}
