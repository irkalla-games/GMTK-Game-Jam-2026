/// <summary>
/// A status application on its way through the OnGainStatus hooks - what Character.AddStatus is about
/// to add, before it is added.
///
/// The gain-side counterpart to ShieldInfo, and the same reasoning applies: immutable, passed
/// *through* the hooks rather than handed to them, so `info = status.OnGainStatus(info)` is the whole
/// pipeline and no status can quietly half-modify a shared object.
///
/// No consumeCharges flag, unlike DamageInfo/ShieldInfo: there is no preview path through
/// Character.AddStatus the way ComputeOutgoingDamage previews a swing without spending anything - an
/// application either happens or the caller never builds a StatusGainInfo at all. A status that reacts
/// here (GainMultiplierStatus) is aura-only and rebuilt fresh per query, so it has nothing of its own
/// to spend regardless.
/// </summary>
public readonly struct StatusGainInfo
{
    /// Who is about to receive the status.
    public readonly Character carrier;

    /// Which status is being applied - what a GainMultiplierStatus checks before it reacts.
    public readonly StatusType type;

    /// The stack count as it stands after every hook so far.
    public readonly int stacks;

    public StatusGainInfo(Character carrier, StatusType type, int stacks)
    {
        this.carrier = carrier;
        this.type = type;
        this.stacks = stacks;
    }

    /// The same application carrying a different stack count. Everything else rides along untouched.
    public StatusGainInfo WithStacks(int newStacks) => new(carrier, type, newStacks);
}
