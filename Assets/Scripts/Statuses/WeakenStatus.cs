/// <summary>
/// Takes a flat amount off every attack the carrier makes, for a number of the carrier's own turns.
/// Strength's mirror on the outgoing side, and Vulnerable's on the other side of the swing.
///
/// Two numbers, unlike almost everything else here: `stacks` is the duration and `Amount` is the size of
/// the cut. The size used to be the counter and every Weaken in the game used to be worth whatever the
/// card said; it is a per-application number now because equipment retunes it - a Weaken applied by
/// somebody wearing the Weaken Ring is a *deeper* cut, not a longer one. See Status.Amount.
///
/// Merges only with a Weaken of the same size. Two Weaken 3s are the same debuff applied twice, so they
/// fold into one that simply lasts longer; a Weaken 3 and a Weaken 5 stay two separate statuses, each
/// with its own clock, because there is no single number that means both.
///
/// Where they stay apart they queue rather than compete. Only the biggest applies - Character.FindStatus
/// ranks them and DamageInfo.weakenAmount is what makes the pipeline apply just the winner - and only
/// the biggest ticks, so the ones underneath hold their clocks until their turn comes rather than
/// burning down unused beneath something stronger. A Weaken 5 for three turns landing on top of a Weaken
/// 3 for three turns is therefore six turns of Weaken: three at 5, then three at 3. The status row
/// badges that total, which is why it reads the carried sum and not the winner's own counter.
/// </summary>
public class WeakenStatus : StatusEffect
{
    /// <summary>
    /// What a Weaken takes off each attack before anybody's equipment has a say - the number every card
    /// that applies Weaken starts from.
    ///
    /// A base rather than the flat constant BlockStatus.AmountPerHit still is: this one is meant to be
    /// added to. Character.AppliedPotency asks the applier what it contributes on top, and the total is
    /// baked into the instance at application time - see StatusEffect.Create's amountBonus.
    /// </summary>
    public const int BaseAmount = 3;

    private readonly int amount;

    public WeakenStatus(int stacks, int amount = BaseAmount) : base(StatusType.Weaken, stacks)
    {
        this.amount = amount;
    }

    public override int Amount => amount;

    /// Folds into another Weaken only when it cuts exactly as deep - see the class doc. Equal sizes
    /// extend one another; different sizes stay apart and are ranked.
    public override bool MergesWith(StatusEffect incoming) => incoming.Amount == amount;

    public override DamageInfo OnDealDamage(DamageInfo info)
    {
        // Only a Weaken bigger than whatever already blunted this swing has anything to say, and it
        // contributes just the difference - so the swing ends up cut by the largest single Weaken
        // rather than by all of them added together. See DamageInfo.weakenAmount.
        if (stacks <= 0 || amount <= info.weakenAmount) { return info; }

        return info.WithWeakenAmount(amount);
    }

    /// <summary>
    /// Self-ticking: the counter is remaining turns, so a turn passing spends one - Frozen and Rooted's
    /// shape. It used to zero itself instead, back when the counter was the size of the cut and ageing
    /// it by one would have shrunk the debuff rather than expiring it; now that size lives in Amount,
    /// decrementing is exactly right.
    ///
    /// Only the Weaken actually in force spends anything. A weaker one sitting underneath is doing
    /// nothing to the carrier, so burning its turns would throw it away unused - it holds its clock and
    /// starts counting when the one above it runs out. That is what makes two Weakens last the sum of
    /// their durations rather than the longer of the two, and it is why the status row badges that sum.
    ///
    /// Identity against FindStatus rather than a "am I the biggest" comparison so that ties resolve the
    /// same way everywhere - one winner per type, chosen in one place. An Aura reaching here always
    /// fails the test, since FindStatus hands back the Aura wrapper and never the effect inside it;
    /// that is correct twice over, because an aura's counter is a throwaway with no turns to spend.
    /// </summary>
    public override void OnTurnEnd(Character carrier)
    {
        if (carrier == null || !ReferenceEquals(carrier.FindStatus(type), this)) { return; }

        stacks--;
    }

    public override string Describe() => $"Weaken {amount} x{stacks}";

    /// Fills the Glossary's "{amount}" with this instance's own size rather than a constant, which is
    /// the whole reason a tooltip can tell a ring-boosted Weaken from a plain one.
    public override string Describe(string template)
    {
        return base.Describe(template)?.Replace(Glossary.AmountToken, amount.ToString());
    }
}
