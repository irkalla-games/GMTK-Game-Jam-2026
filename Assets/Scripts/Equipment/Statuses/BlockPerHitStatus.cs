/// <summary>
/// Adds to what each of the carrier's Block takes off a hit - Rimeguard Band. Only a number for
/// Character.BonusBlockPerHit to sum; BlockStatus.PerHitFor is where it takes effect, so a carried
/// Block and a totem's Block aura on the wearer both block the larger amount.
///
/// Reports StatusType.None for the same reason ResourceStatus does: equipment has its own UI.
/// </summary>
public class BlockPerHitStatus : StatusEffect
{
    private readonly int bonus;

    public BlockPerHitStatus(int bonus) : base(StatusType.None, 1)
    {
        this.bonus = bonus;
    }

    public override int BonusBlockPerHit => bonus;

    public override string Describe() => $"Your Block blocks {BlockStatus.AmountPerHit + bonus} per hit";
}
