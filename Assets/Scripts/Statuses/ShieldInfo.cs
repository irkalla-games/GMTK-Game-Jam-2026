using UnityEngine;

/// <summary>
/// One shield-gain event on its way through the status hooks.
///
/// The Shield counterpart to DamageInfo, and the same reasoning applies: immutable, passed *through*
/// the hooks rather than handed to them, so `info = status.OnGainShield(info)` is the whole pipeline
/// and no status can quietly half-modify a shared object.
///
/// A readonly struct rather than a class for the same reason DamageInfo is - produced one per status
/// per gain and discarded immediately, so there is nothing for a value type to collect.
///
/// Only one direction, unlike DamageInfo: shield is granted straight to `carrier`, there is no
/// separate "who is receiving it" to track, since a card that grants shield always targets the tile
/// it lands on. See Character.GainShield.
/// </summary>
public readonly struct ShieldInfo
{
    /// Who is gaining the shield. There is no "source" counterpart to DamageInfo.attacker: shield is
    /// granted to a tile's occupant, and who played the card has no bearing on the amount.
    public readonly Character carrier;

    /// The running total, as it stands after every hook so far.
    public readonly int amount;

    /// False when somebody is only *looking* at this number - a tooltip, a card preview, an enemy
    /// brain scoring a move it has not made yet. Statuses that spend a charge must check this first,
    /// or displaying a number would destroy the buff without a gain ever happening. Same contract as
    /// DamageInfo.consumeCharges.
    public readonly bool consumeCharges;

    public ShieldInfo(Character carrier, int amount, bool consumeCharges = true)
    {
        this.carrier = carrier;
        this.amount = amount;
        this.consumeCharges = consumeCharges;
    }

    /// The same gain carrying a different number. Everything else rides along untouched.
    public ShieldInfo WithAmount(int newAmount) => new(carrier, newAmount, consumeCharges);

    /// Reduced by a flat amount, never past zero. No current caller - kept symmetric with
    /// DamageInfo.Reduced for whatever the first shield-reducing status turns out to be.
    public ShieldInfo Reduced(int reduction) => WithAmount(Mathf.Max(0, amount - reduction));
}
