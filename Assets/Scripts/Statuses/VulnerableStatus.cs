/// <summary>
/// Adds a flat amount to each of the next few hits the carrier takes, then spends a charge. The
/// incoming-damage mirror of Strength - same charge-spent shape, opposite side of the swing. The
/// counter is how many hits it still worsens; how much it adds is AmountPerHit, the same for every
/// Vulnerable in the game.
/// </summary>
public class VulnerableStatus : StatusEffect
{
    /// <summary>
    /// How much each worsened hit gains, for every Vulnerable in the game.
    ///
    /// A constant rather than authoring on the effect asset, same reasoning as StrengthStatus.AmountPerHit
    /// - "Vulnerable 3" means one thing everywhere, not a number that varies by which card granted it.
    /// </summary>
    public const int AmountPerHit = 3;

    public VulnerableStatus(int stacks) : base(StatusType.Vulnerable, stacks) { }

    public override DamageInfo OnTakeDamage(DamageInfo info)
    {
        if (info.negated || stacks <= 0) { return info; }

        // Looking is free. Only an actual hit spends the charge - see DamageInfo.consumeCharges.
        if (info.consumeCharges) { stacks--; }

        return info.WithAmount(info.amount + AmountPerHit);
    }

    public override string Describe() => $"Vulnerable {AmountPerHit} x{stacks}";

    /// Still overridden even though the amount is fixed: the Glossary's authored body spells "{amount}",
    /// and this is what fills it - see StrengthStatus.Describe(string).
    public override string Describe(string template)
    {
        return base.Describe(template)?.Replace(Glossary.AmountToken, AmountPerHit.ToString());
    }
}
