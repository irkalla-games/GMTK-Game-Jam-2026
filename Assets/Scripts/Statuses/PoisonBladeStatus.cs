/// <summary>
/// For as long as it lasts, every hit the carrier lands also poisons the victim. The template for
/// on-hit riders - see OnHitStatus.
///
/// Fires on any connecting hit, mitigated or not: a hit Shield or Block reduces to zero still counts
/// as landing, because the blade touched skin either way. Dodge and Parry sidestep the hit entirely -
/// see Character.TakeDamage, which only calls the rider when the hit was not negated.
/// </summary>
public class PoisonBladeStatus : OnHitStatus
{
    /// <summary>
    /// Poison applied per landed hit, the same for every Poison Blade in the game.
    ///
    /// A constant rather than a second field, same reasoning as StrengthStatus.AmountPerHit - stacks
    /// is already spoken for as the remaining duration.
    /// </summary>
    public const int PoisonPerHit = 1;

    public PoisonBladeStatus(int stacks) : base(StatusType.PoisonBlade, stacks) { }

    public override void OnDamageDealt(DamageInfo info)
    {
        if (info.target == null) { return; }

        info.target.AddStatus(StatusType.Poison, PoisonPerHit);
    }

    public override string Describe() => $"Poison Blade x{stacks}";

    /// Still overridden even though the per-hit amount is fixed: the Glossary's authored body spells
    /// "{amount}", and this is what fills it - see StrengthStatus.Describe(string).
    public override string Describe(string template)
    {
        return base.Describe(template)?.Replace(Glossary.AmountToken, PoisonPerHit.ToString());
    }
}
