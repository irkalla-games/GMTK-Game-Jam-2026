using UnityEngine;

/// <summary>
/// A flat reduction taken off each of the next few hits. `stacks` is how many hits it still applies
/// to; `amountPerHit` is the reduction each of those hits gets.
///
/// Per-hit, not pooled, which is the whole difference from Shield: Block 5 against three hits of 8
/// leaves three hits of 3, where 5 Shield would have absorbed 5 once. Against small frequent hits it
/// is worth far more than the same number of Shield, and it gets better the more enemies there are.
///
/// The only status with two numbers, which is why it cannot be authored through the generic Apply
/// Status asset - BlockEffect has both fields. StatusEffect.Create still handles the type, reading its
/// one number as "reduce the next hit by this much".
/// </summary>
public class BlockStatus : StatusEffect
{
    /// How much comes off each hit. Not `stacks`, which is the charge count.
    public int amountPerHit;

    public BlockStatus(int amountPerHit, int charges, int turnsRemaining)
        : base(StatusType.Block, charges, turnsRemaining)
    {
        this.amountPerHit = amountPerHit;
    }

    public override DamageInfo OnTakeDamage(DamageInfo info)
    {
        if (stacks <= 0) { return info; }

        stacks--;

        return info.Reduced(amountPerHit);
    }

    /// <summary>
    /// Keeps the higher of the two per-hit amounts and adds the charges, so a top-up can never
    /// downgrade what is already there.
    ///
    /// The amount is settled *before* base.Merge runs, because base.Merge is what changes `stacks` and
    /// the "is there any block left" test has to see the old value.
    /// </summary>
    public override void Merge(StatusEffect incoming)
    {
        if (incoming is BlockStatus block)
        {
            amountPerHit = stacks > 0 ? Mathf.Max(amountPerHit, block.amountPerHit) : block.amountPerHit;
        }

        base.Merge(incoming);
    }

    public override string Describe() => $"Block {amountPerHit} x{stacks}";
}
