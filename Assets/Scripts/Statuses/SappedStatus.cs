/// <summary>
/// Takes one energy off the carrier's pool for the next few of its turns - the Reaper's Soul Drain.
///
/// Rides BonusEnergy, which Character.EnergyCapacity already sums and ResetEnergy already fills to, so
/// nothing new had to be written for it to bite: a negative bonus simply means a smaller pool on the
/// turn that matters.
///
/// Deferred for the same reason Pilfered is. Taking energy the instant the card resolves does nothing
/// - TurnStart calls ResetEnergy before the hero spends a single point, so a drain during the enemy's
/// phase is refilled before it is ever felt. Lowering the capacity is what makes the hero budget
/// around it.
///
/// One energy per turn rather than a size on the status: the pool is small enough that -1 is already a
/// missing card, and a stacking size would swing between "nothing" and "you do not get a turn".
/// </summary>
public class SappedStatus : StatusEffect
{
    public SappedStatus(int stacks) : base(StatusType.Sapped, stacks) { }

    public override int BonusEnergy => -1;

    public override void OnTurnEnd(Character carrier)
    {
        stacks--;
    }

    public override string Describe() => $"-1 energy for {stacks} turn(s)";
}
