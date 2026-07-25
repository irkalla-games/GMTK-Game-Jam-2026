using UnityEngine;

/// <summary>
/// Applies a status. One asset type covers every buff and curse in the game - Strengthen is
/// (Strength, 3, Indefinite), Buff is (DoubleNextAttack, 1, Indefinite), and a poison dart would be
/// (Poison, 3, 2 turns) with alliesOnly off. No bespoke code for any of them.
/// </summary>
[CreateAssetMenu(menuName = "Card Effects/Apply Status")]
public class StatusEffect : CardEffect
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
