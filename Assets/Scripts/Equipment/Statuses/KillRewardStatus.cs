/// <summary>
/// Grants the wearer energy and/or a status the instant one of its hits kills - Deathgrip Signet's
/// "killing an enemy grants 1 energy", Dread Sovereign's "kills grant 1 Strength". Fires from
/// OnDamageDealt, which Character.TakeDamage only calls for a hit that actually connected and after
/// the victim's Health has already been reduced - see Character.NotifyDamageDealt - so info.target.IsDead
/// here is exactly "did this blow finish them off", checked before the Died event and loot drop even
/// run.
///
/// Needs no info.consumeCharges gate unlike ThornsStatus: OnDamageDealt is only ever reached through a
/// real TakeDamage call (a damage preview never calls NotifyDamageDealt), so there is no preview path
/// that could trigger this early.
/// </summary>
public class KillRewardStatus : StatusEffect
{
    private readonly int bonusEnergy;
    private readonly StatusType grantedStatus;
    private readonly int grantedStacks;

    public KillRewardStatus(int bonusEnergy, StatusType grantedStatus, int grantedStacks) : base(StatusType.None, 1)
    {
        this.bonusEnergy = bonusEnergy;
        this.grantedStatus = grantedStatus;
        this.grantedStacks = grantedStacks;
    }

    public override void OnDamageDealt(DamageInfo info)
    {
        if (info.attacker == null || info.target == null || !info.target.IsDead) { return; }

        if (bonusEnergy > 0) { info.attacker.GainEnergy(bonusEnergy); }
        if (grantedStatus != StatusType.None && grantedStacks > 0) { info.attacker.AddStatus(grantedStatus, grantedStacks); }
    }

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
