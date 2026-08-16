/// <summary>
/// Adds a flat amount to each of the next few attacks the carrier makes, then spends a charge. The
/// counter is how many attacks it still boosts; how much it adds is AmountPerHit, the same for every
/// Strength in the game - see BlockStatus, its mirror on the incoming side.
/// </summary>
public class StrengthStatus : StatusEffect
{
    /// <summary>
    /// How much each boosted attack gains, for every Strength in the game.
    ///
    /// A constant rather than authoring on the effect asset, same reasoning as BlockStatus.AmountPerHit
    /// - "Strength 3" means one thing everywhere, not a number that varies by which card granted it.
    /// </summary>
    public const int AmountPerHit = 3;

    public StrengthStatus(int stacks) : base(StatusType.Strength, stacks) { }

    public override DamageInfo OnDealDamage(DamageInfo info)
    {
        if (stacks <= 0) { return info; }

        // Looking is free. Only an actual swing spends the charge - see DamageInfo.consumeCharges.
        if (info.consumeCharges) { stacks--; }

        return info.WithAmount(info.amount + AmountPerHit);
    }

    public override string Describe() => $"Strength {AmountPerHit} x{stacks}";

    /// Still overridden even though the amount is fixed: the Glossary's authored body spells "{amount}",
    /// and this is what fills it - see BlockStatus.Describe(string).
    public override string Describe(string template)
    {
        return base.Describe(template)?.Replace(Glossary.AmountToken, AmountPerHit.ToString());
    }
}
