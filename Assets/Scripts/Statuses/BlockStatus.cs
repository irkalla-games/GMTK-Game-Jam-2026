/// <summary>
/// A flat reduction taken off every hit for as long as it is active. The counter is turns remaining,
/// not hits - a turn passing spends one whether or not anything landed; how much comes off each hit is
/// the same for every Block in the game - see AmountPerHit.
///
/// Turn-scoped, not pooled, which is the whole difference from Shield: Block does not care whether a
/// turn brings one hit or five, each is reduced by the same 3, where Shield would drain its pool faster
/// the more hits arrive. Against a single hard-hitting enemy Shield is the better buy; against being
/// swarmed, Block is.
///
/// Used to be the one status with two numbers, which is why it needed its own effect asset and its own
/// Merge. Fixing the amount collapsed it to one counter like everything else - "Block 3" now means
/// three turns rather than needing an amount alongside it.
/// </summary>
public class BlockStatus : StatusEffect
{
    /// <summary>
    /// How much comes off each hit, for every Block in the game.
    ///
    /// A constant rather than authoring on the effect asset so "Block 3" means one thing everywhere. It
    /// lives here, next to the rule that spends it, rather than in a tuning asset - a ScriptableObject
    /// would buy retuning without a recompile at the cost of threading a reference to wherever statuses
    /// are built, and of the runtime-mutation trap every SO here carries.
    ///
    /// Against the current 3-5 enemy damage band it fully negates most hits for as long as it lasts,
    /// which is what the balance note in CLAUDE.md is about.
    /// </summary>
    public const int AmountPerHit = 3;

    public BlockStatus(int stacks) : base(StatusType.Block, stacks) { }

    // No Order override: the default 0 already sits between Vulnerable (-10) and Shield (10), which is
    // where Block belongs in the incoming chain - see Status.Order.

    public override DamageInfo OnTakeDamage(DamageInfo info)
    {
        // blockApplied gates a second Block source (a carried charge and a totem's Block aura are two
        // separate Status entries) from also taking 3 off the same hit - see DamageInfo.blockApplied.
        if (stacks <= 0 || info.blockApplied) { return info; }

        // No charge to spend here - the turn is what pays for this, in OnTurnStart, so every hit this
        // turn gets the same reduction regardless of consumeCharges.
        return info.ReducedOnce(AmountPerHit);
    }

    /// Self-ticking: the counter is turns remaining, so a turn passing spends one - "Block 3" lasts
    /// three turns. At OnTurnStart rather than OnTurnEnd so a Block gained this round stays live through
    /// EnemyResolve before its first turn is spent, the same reason ShieldStatus wipes there - see
    /// TurnTiming.
    public override void OnTurnStart(Character carrier)
    {
        stacks--;
    }

    public override string Describe() => $"Block {AmountPerHit} x{stacks}";

    /// Still overridden even though the amount is fixed: the Glossary's authored bodies spell
    /// "{amount}", and this is what fills it. Without it every Block tooltip would fall back to the
    /// entry's defaultAmount rather than the number the status actually applies.
    public override string Describe(string template)
    {
        return base.Describe(template)?.Replace(Glossary.AmountToken, AmountPerHit.ToString());
    }
}
