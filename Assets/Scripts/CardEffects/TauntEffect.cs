using UnityEngine;

/// <summary>
/// Taunts whoever is standing on the target tile: for the next few turns they come after the character
/// who played this, ignoring their own targeting pattern.
///
/// Its own effect type rather than an Apply Status asset authored with StatusType.Taunt: the generic
/// path runs through StatusEffect.Create(type, stacks), and a taunt needs the taunter, which that
/// signature has nowhere to put. The taunter is ctx.source - the character resolving the action - so it
/// arrives through ActionContext like every other per-play value, and nothing has to be threaded
/// through Character.AddStatus for it.
/// </summary>
[CreateAssetMenu(menuName = "Card Effects/Apply Taunt")]
public class TauntEffect : CardEffect
{
    [Tooltip("Turns the target keeps chasing the caster. Duration is the only lever on this one - the "
             + "effect is binary, so stacking it would mean nothing.")]
    [SerializeField] private int turnsRemaining = 2;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new TauntAction(ctx.Amount(turnsRemaining)), ctx);
    }

    /// Needs somebody there, and it is always aimed at the other side - the same rule DamageEffect
    /// uses, which is what keeps the tile highlight from ever offering empty ground or an ally.
    public override TargetAudience Audience => TargetAudience.Enemy;
}
