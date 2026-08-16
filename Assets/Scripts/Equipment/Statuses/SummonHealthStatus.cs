/// <summary>
/// Raises a freshly summoned character's max health by a flat amount - Totem Anchor's "totems enter
/// play with +7 max health". Reacts to OnSummoned rather than any damage/gain pipeline, since this
/// changes the summon's own ceiling once, at the moment it comes into being, not something that recurs.
///
/// `totemsOnly`, when set, checks for a Totem component on the summon before touching it - a relic
/// themed around totems specifically should not also fatten a summoned Skeleton Warrior. Left off, every
/// summon the wearer makes benefits, matching how DamageBonusModifier and GainBonusModifier apply to
/// every card rather than one kind.
/// </summary>
public class SummonHealthStatus : StatusEffect
{
    private readonly int bonus;
    private readonly bool totemsOnly;

    public SummonHealthStatus(int bonus, bool totemsOnly) : base(StatusType.None, 1)
    {
        this.bonus = bonus;
        this.totemsOnly = totemsOnly;
    }

    public override void OnSummoned(Character summoner, Character summon)
    {
        if (summon == null || bonus == 0) { return; }
        if (totemsOnly && summon.GetComponent<Totem>() == null) { return; }

        summon.AddMaxHealth(bonus);
    }

    public override string Describe() =>
        totemsOnly ? $"Totems: +{bonus} max health" : $"Summons: +{bonus} max health";
}
