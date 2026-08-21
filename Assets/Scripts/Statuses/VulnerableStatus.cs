/// <summary>
/// Adds a flat amount to every hit the carrier takes, for a number of the carrier's own turns. Weaken's
/// mirror on the other side of the swing, and built the same way: `stacks` is the duration, `Amount` is
/// the size, and the size travels with the application because equipment retunes it - see Status.Amount.
///
/// Merges only with a Vulnerable of the same size, and where two stay apart only the biggest applies -
/// see WeakenStatus, which documents the merge rule and ranking this shares, and
/// DamageInfo.vulnerableAmount.
/// </summary>
public class VulnerableStatus : StatusEffect
{
    /// What a Vulnerable adds to each hit before anybody's equipment has a say. See
    /// WeakenStatus.BaseAmount for why this is a base rather than a flat constant.
    public const int BaseAmount = 3;

    private readonly int amount;

    public VulnerableStatus(int stacks, int amount = BaseAmount) : base(StatusType.Vulnerable, stacks)
    {
        this.amount = amount;
    }

    public override int Amount => amount;

    /// Folds into another Vulnerable only when it hits exactly as hard - see WeakenStatus.
    public override bool MergesWith(StatusEffect incoming) => incoming.Amount == amount;

    public override DamageInfo OnTakeDamage(DamageInfo info)
    {
        // Only a Vulnerable bigger than whatever already worsened this hit contributes, and only the
        // difference - so the hit ends up raised by the largest single Vulnerable rather than by all of
        // them added together. See DamageInfo.vulnerableAmount.
        if (info.negated || stacks <= 0 || amount <= info.vulnerableAmount) { return info; }

        return info.WithVulnerableAmount(amount);
    }

    /// Self-ticking, and only while it is the one in force - see WeakenStatus.OnTurnEnd, which
    /// documents why a masked instance holds its clock instead of burning it. The counter used to be
    /// spent by hits instead, one charge per blow; a Vulnerable now worsens *every* attack that lands
    /// while it is up and expires on time rather than on use.
    public override void OnTurnEnd(Character carrier)
    {
        if (carrier == null || !ReferenceEquals(carrier.FindStatus(type), this)) { return; }

        stacks--;
    }

    public override string Describe() => $"Vulnerable {amount} x{stacks}";

    /// Fills the Glossary's "{amount}" with this instance's own size rather than a constant - see
    /// WeakenStatus.Describe(string).
    public override string Describe(string template)
    {
        return base.Describe(template)?.Replace(Glossary.AmountToken, amount.ToString());
    }
}
