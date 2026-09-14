using UnityEngine;

/// <summary>
/// Grants the acting character energy - Adrenaline's "take 5 damage, gain 1 energy". Character.GainEnergy
/// is deliberately uncapped by maxEnergy, so this is worth playing even on a turn with a full pool.
/// </summary>
[CreateAssetMenu(menuName = "Card Effects/Gain Energy")]
public class EnergyEffect : CardEffect
{
    [SerializeField] private int amount = 1;

    public override bool SupportsArea => false;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new EnergyAction(amount), ctx);
    }

    /// See CardEffect.ActsOnSource: EnergyAction always grants ctx.source, so this cannot be pointed
    /// at anybody else however the asset is authored.
    public override bool ActsOnSource => true;
}
