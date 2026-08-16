/// <summary>
/// Scales the stack count of whatever status type `subject` names, whenever that type is applied to
/// the carrier - a totem aura that doubles every Strength card played on whoever stands in range, or
/// every Weaken a Mage lands on a nearby enemy.
///
/// Aura-only: nothing carries this itself, so it is never built by StatusEffect.Create - only by
/// AuraData.CreateEffect, the same way TauntStatus is only ever built by TauntEffect. Reacts through
/// OnGainStatus, which Character.AddStatus runs before merging or appending the incoming StatusEffect -
/// see Status.OnGainStatus.
///
/// `stacks` (the base StatusEffect counter) is inert here, same as every aura-only status: Totem
/// rebuilds this fresh on every query, so there is nothing for a charge-spending hook to spend even if
/// this reacted to one. The number that matters is `multiplier`, authored on AuraData.magnitude.
/// </summary>
public class GainMultiplierStatus : StatusEffect
{
    private readonly StatusType subject;
    private readonly int multiplier;

    public GainMultiplierStatus(StatusType subject, int multiplier, int stacks)
        : base(StatusType.GainMultiplier, stacks)
    {
        this.subject = subject;
        this.multiplier = multiplier;
    }

    public override StatusGainInfo OnGainStatus(StatusGainInfo info)
    {
        if (info.type != subject) { return info; }

        // A totem authored with magnitude 0 or negative would otherwise erase the very status it is
        // meant to amplify - floor at x1 so a misconfigured aura is inert rather than a silent curse.
        int safeMultiplier = multiplier > 1 ? multiplier : 1;

        return info.WithStacks(info.stacks * safeMultiplier);
    }

    public override string Describe() => $"{subject} applications x{multiplier}";
}
