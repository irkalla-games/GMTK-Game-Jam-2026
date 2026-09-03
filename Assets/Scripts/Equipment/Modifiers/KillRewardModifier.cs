using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grants energy and/or a status the instant the wearer lands a killing blow - Deathgrip Signet, Dread
/// Sovereign. Projects a KillRewardStatus; see that class for why info.target.IsDead is checked at
/// exactly the right moment.
/// </summary>
[CreateAssetMenu(menuName = "Equipment Modifiers/Kill Reward")]
public class KillRewardModifier : EquipmentModifier
{
    [SerializeField] private int bonusEnergy;
    [SerializeField] private StatusType grantedStatus;
    [SerializeField] private int grantedStacks;

    public override void Project(Character carrier, List<Status> into) =>
        into.Add(new KillRewardStatus(bonusEnergy, grantedStatus, grantedStacks));

    public override string Describe()
    {
        if (bonusEnergy > 0 && grantedStatus != StatusType.None)
        {
            return $"Kills grant {bonusEnergy} energy and {grantedStatus} {grantedStacks}";
        }

        if (bonusEnergy > 0) { return $"Kills grant {bonusEnergy} energy"; }
        if (grantedStatus != StatusType.None) { return $"Kills grant {grantedStatus} {grantedStacks}"; }
        return "No change";
    }
}
