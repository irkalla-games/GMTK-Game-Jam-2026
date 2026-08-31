/// <summary>
/// Heals its counter at the end of the carrier's own phase, then decays by one. Regeneration 4 heals
/// 4, then 3, then 2, then 1, and is gone - Poison's exact mirror on the healing side, and proof the
/// same one-counter shape (front-loaded, self-limiting, no second field) works for a buff as cleanly
/// as it does for a curse.
///
/// Built the ordinary way through StatusEffect.Create, so any card can grant it via ApplyStatusEffect
/// exactly like Strength or Weaken - there is nothing totem-only about it. A totem that wants to heal
/// whoever stands in range projects this as a plain aura (Sap Totem's shape, not Bastion's TurnTick):
/// its effect fires once, inside the single OnTurnEnd call it is asked about, so a fresh throwaway
/// instance built per query is enough - unlike Shield, there is no turn-start wipe to reset a
/// re-granted pool, so a TurnTick regrant would compound without bound instead of staying flat.
/// </summary>
public class RegenerationStatus : StatusEffect
{
    public RegenerationStatus(int stacks) : base(StatusType.Regeneration, stacks) { }

    public override void OnTurnEnd(Character carrier)
    {
        // Heal first, decay second, so the last point of Regeneration still heals before expiring -
        // the same order PoisonStatus bites in.
        carrier.Heal(stacks);

        stacks--;
    }
}
