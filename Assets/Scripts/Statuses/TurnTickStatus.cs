/// <summary>
/// Grants stacks of whatever status type `granted` names to the carrier once per round, at whichever
/// end of the round `timing` says - a totem that hands out Shield 5 at the end of your turn, or Weaken
/// 2 onto an enemy at the start of theirs.
///
/// Aura-only, same reason GainMultiplierStatus and PotencyStatus are - only ever built by
/// AuraData.CreateEffect. Unlike those two this does not react to a pipeline hook; it fires from the
/// same OnTurnStart/OnTurnEnd every self-ticking status uses, just picking one of the two based on
/// `timing` instead of always both.
///
/// Grants through Character.AddStatus (or GainShield for a Shield subject, so DoubleShieldStatus still
/// applies), which means a granted amount runs the OnGainStatus pipeline exactly like a card-applied
/// one - a GainMultiplier totem stacked with this one doubles what it hands out. See TurnTiming for why
/// picking the wrong half of that field silently erases the grant in the same pass it lands.
/// </summary>
public class TurnTickStatus : StatusEffect
{
    private readonly StatusType granted;
    private readonly int grantedStacks;
    private readonly TurnTiming timing;

    public TurnTickStatus(StatusType granted, int grantedStacks, TurnTiming timing, int stacks)
        : base(StatusType.TurnTick, stacks)
    {
        this.granted = granted;
        this.grantedStacks = grantedStacks;
        this.timing = timing;
    }

    public override void OnTurnStart(Character carrier)
    {
        if (timing == TurnTiming.TurnStart) { Grant(carrier); }
    }

    public override void OnTurnEnd(Character carrier)
    {
        if (timing == TurnTiming.TurnEnd) { Grant(carrier); }
    }

    private void Grant(Character carrier)
    {
        if (carrier == null || granted == StatusType.None || grantedStacks <= 0) { return; }

        if (granted == StatusType.Shield)
        {
            carrier.GainShield(grantedStacks);
        }
        else
        {
            carrier.AddStatus(granted, grantedStacks);
        }
    }

    public override string Describe() => $"Grants {granted} {grantedStacks} each turn";
}
