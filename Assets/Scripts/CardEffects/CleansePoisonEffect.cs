using UnityEngine;

/// <summary>
/// Strips every carried Poison stack off the target, then does one of two things with the number
/// removed - hands it to the nearest enemy, or heals the target for it. One asset type for both:
/// they share the same first step and differ only in what happens to the count, the same "one bool
/// picks the behaviour" shape HealEffect.canHitEnemies and ApplyStatusEffect.alliesOnly already use.
/// </summary>
[CreateAssetMenu(menuName = "Card Effects/Cleanse Poison")]
public class CleansePoisonEffect : CardEffect
{
    [Tooltip("On: the stacks removed are applied to the nearest living enemy of the target. Off: the "
             + "target is healed for the number of stacks removed.")]
    [SerializeField] private bool giveToNearestEnemy;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new CleansePoisonAction(giveToNearestEnemy), ctx);
    }

    // Needs a living ally standing there - the same rule Heal and buff cards use. Purge and
    // Cleansing Light both draw poison off your own side, never an enemy's.
    public override TargetAudience Audience => TargetAudience.Ally;
}
