/// <summary>
/// Negates each of the next few hits outright and sidesteps the carrier to a nearby tile. `stacks` is
/// the charge count, the same shape as Parry - except the charge itself is paid at the next TurnStart
/// rather than on the hit, so one stack covers every hit for the rest of the phase it is spent in. See
/// spentThisPhase below for why.
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
///
/// Every dodged hit also locks any enemy that was aiming at the carrier onto the tile it just left -
/// see BattleManager.LockAimsOn - so the rest of the enemy phase keeps swinging at empty ground instead
/// of re-acquiring wherever the carrier ends up.
/// </summary>
public class DodgeStatus : StatusEffect
{
    /// <summary>
    /// Whether a charge has already been spoken for this phase. `stacks` cannot be decremented on the
    /// hit itself: Character.ComputeIncomingDamage calls PruneExpired() the instant the OnTakeDamage
    /// hooks return, so a Dodge x1 hitting zero mid-phase would delete this status before the rest of
    /// the phase's free negations ever happened. Paying the charge at the next OnTurnStart instead -
    /// the same hook ShieldStatus wipes itself in - keeps `stacks` positive for the whole phase and
    /// lets that OnTurnStart's own PruneExpired drop it once it is actually spent.
    /// </summary>
    private bool spentThisPhase;

    public DodgeStatus(int stacks) : base(StatusType.Dodge, stacks) { }

    /// First of the incoming chain. A hit this fully avoids should never reach Block, Shield or Parry -
    /// none of them should spend a charge negating a hit that was never going to land anyway.
    public override int Order => -20;

    public override DamageInfo OnTakeDamage(DamageInfo info)
    {
        if (stacks <= 0) { return info; }

        // Negation is unconditional whenever a charge is available (see the class comment) - a preview
        // has to see the same zero a real hit would land as. Only the charge spend, the lock and the
        // queued sidestep are gated behind consumeCharges, or displaying a number would burn the dodge
        // and move the character without an attack ever happening - see DamageInfo.consumeCharges.
        if (info.consumeCharges)
        {
            spentThisPhase = true;

            Character carrier = info.target;

            if (carrier != null && GridManager.Instance != null && ActionManager.Instance != null)
            {
                GridTile vacated = carrier.Tile;

                // Asked before the move below is even queued, so Decide still sees `carrier` standing
                // on `vacated` - that is what makes the tile it freezes the right one.
                if (BattleManager.Instance != null) { BattleManager.Instance.LockAimsOn(carrier, vacated); }

                GridTile awayFrom = info.attacker != null ? info.attacker.Tile : null;
                GridTile destination = GridManager.Instance.StepAwayFrom(carrier, awayFrom);

                if (destination != null)
                {
                    ActionManager.Instance.AddAction(
                        new MoveAction(), new ActionContext(card: null, source: carrier, target: destination));
                }
            }
        }

        return info.Negated();
    }

    public override void OnTurnStart(Character carrier)
    {
        if (!spentThisPhase) { return; }

        spentThisPhase = false;
        stacks--;
    }

    public override string Describe() => $"Dodge x{stacks}";
}
