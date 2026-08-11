/// <summary>
/// Negates each of the next few hits outright and sidesteps the carrier to a nearby tile. `stacks` is
/// the charge count, the same shape as Parry.
///
/// The move is a queued MoveAction, not a synchronous GridManager.MoveCharacter call - the same
/// "reaction builds a null-card ActionContext" pattern Totem.TryReact uses. Queuing keeps this safe to
/// call from inside Character.TakeDamage: ActionManager.AddAction only enqueues (ActionManager is
/// already mid-Execute, so isRunning is true), the move gets its normal walk animation and tween
/// timing, and BattleManager.EnemyResolve's own WaitUntil(IsIdle) already waits for it before the next
/// enemy acts.
///
/// Negation is unconditional whenever a charge is available - the sidestep is best-effort on top of
/// it, never a precondition. GridManager.StepAwayFrom returning null (Rooted, boxed in, no legal
/// neighbour) still fully zeroes this hit; it just means nobody sees the character move.
/// </summary>
public class DodgeStatus : StatusEffect
{
    public DodgeStatus(int stacks, int turnsRemaining)
        : base(StatusType.Dodge, stacks, turnsRemaining) { }

    public override DamageInfo OnTakeDamage(DamageInfo info)
    {
        if (stacks <= 0 || !info.consumeCharges) { return info; }

        stacks--;

        Character carrier = info.target;

        if (carrier != null && GridManager.Instance != null && ActionManager.Instance != null)
        {
            GridTile awayFrom = info.attacker != null ? info.attacker.Tile : null;
            GridTile destination = GridManager.Instance.StepAwayFrom(carrier, awayFrom);

            if (destination != null)
            {
                ActionManager.Instance.AddAction(
                    new MoveAction(), new ActionContext(card: null, source: carrier, target: destination));
            }
        }

        return info.Negated();
    }

    public override string Describe() => $"Dodge x{stacks}";
}
