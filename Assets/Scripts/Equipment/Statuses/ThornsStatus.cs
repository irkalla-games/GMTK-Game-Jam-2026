/// <summary>
/// Deals a flat amount back to whoever attacks the carrier - Bramblewreath's "attackers take 2
/// damage", and the Evil King's standing punishment for swinging at him. Fires from OnTakeDamage,
/// which equipment resolves ahead of every carried status (see Character.ActiveStatuses), so this
/// reacts to an incoming hit before the carrier's own Dodge, Parry, Shield or Block gets a say - the
/// same "equipment resolves first" trade FlatDamageStatus's incomingReduction already makes.
///
/// Retaliates through TakeUnblockableDamage, not TakeDamage: routing back through the ordinary
/// pipeline would run the attacker's own OnTakeDamage hooks, and two thorns-wearing characters
/// trading blows would then retaliate against each other's retaliation without end. Poison already
/// settles for the same shape for the same reason - this is not an attack the attacker can defend
/// against.
///
/// Gated on info.consumeCharges: that flag is false during a damage preview - a tooltip, an enemy
/// brain scoring a move it has not made yet - so hovering an attack never deals real thorns damage.
///
/// stacks IS the damage returned, and never ages: nothing decrements it, so a boss wearing Thorns
/// wears it for the whole fight. Two applications merge into a bigger number rather than a longer
/// clock, which is the only reading that makes sense for a status with no duration.
/// </summary>
public class ThornsStatus : StatusEffect
{
    public ThornsStatus(int amount) : base(StatusType.Thorns, amount) { }

    public override DamageInfo OnTakeDamage(DamageInfo info)
    {
        if (info.consumeCharges && stacks > 0 && info.attacker != null)
        {
            info.attacker.TakeUnblockableDamage(stacks);
        }

        return info;
    }

    public override string Describe() => $"Attackers take {stacks} damage";
}
