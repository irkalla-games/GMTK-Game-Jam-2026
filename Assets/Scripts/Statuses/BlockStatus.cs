/// <summary>
/// A flat reduction taken off each of the next few hits. The counter is how many hits it still applies
/// to; how much comes off each one is the same for every Block in the game - see AmountPerHit.
///
/// Per-hit, not pooled, which is the whole difference from Shield: Block against three hits of 8 leaves
/// three hits of 3, where 5 Shield would have absorbed 5 once. Against small frequent hits it is worth
/// far more than the same number of Shield, and it gets better the more enemies there are.
///
/// Used to be the one status with two numbers, which is why it needed its own effect asset and its own
/// Merge. Fixing the amount collapsed it to one counter like everything else - "Block 3" now means
/// three charges rather than needing an amount alongside it.
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
    /// Against the current 3-5 enemy damage band one charge fully negates most hits, which is what the
    /// balance note in CLAUDE.md is about: per-hit mitigation beats the pooled kind at these numbers.
    /// </summary>
    public const int AmountPerHit = 5;

    public BlockStatus(int stacks) : base(StatusType.Block, stacks) { }

    public override DamageInfo OnTakeDamage(DamageInfo info)
    {
        // blockApplied gates a second Block source (a carried charge and a totem's Block aura are two
        // separate Status entries) from also taking 5 off the same hit - see DamageInfo.blockApplied.
        // A charge behind that gate is left unspent: standing in a Block aura is what covered this hit,
        // so there is nothing for the carried charge to have done.
        if (stacks <= 0 || info.blockApplied) { return info; }

        // Looking is free. Only an actual hit spends the charge - see DamageInfo.consumeCharges.
        if (info.consumeCharges) { stacks--; }

        return info.ReducedOnce(AmountPerHit);
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
