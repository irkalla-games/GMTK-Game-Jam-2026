/// <summary>
/// Adds flat energy and/or hand-size bonuses that apply every turn - Ring of Focus's "+1 energy each
/// turn", Astrologer's Loop's "+1 card drawn each turn". Read by Character.BonusEnergy/BonusHandSize,
/// which ResetEnergy and BattleManager's turn-start draw fold in respectively - see Status.BonusEnergy.
///
/// Equipment leaves `type` at StatusType.None for the same reason FlatDamageStatus and GainBonusStatus
/// do: it is not meant to render as its own status chip, since equipment has its own UI.
/// </summary>
public class ResourceStatus : StatusEffect
{
    private readonly int bonusEnergy;
    private readonly int bonusHandSize;

    public ResourceStatus(int bonusEnergy, int bonusHandSize) : base(StatusType.None, 1)
    {
        this.bonusEnergy = bonusEnergy;
        this.bonusHandSize = bonusHandSize;
    }

    public override int BonusEnergy => bonusEnergy;
    public override int BonusHandSize => bonusHandSize;
}
