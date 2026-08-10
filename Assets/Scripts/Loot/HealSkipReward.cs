using System.Collections;
using UnityEngine;

/// <summary>
/// Declining the offered cards heals the picker instead. Flat rather than scaled by the drop's rarity
/// - a deliberate call, so skipping a Legendary for a full 5 heal is a real trade rather than a
/// consolation prize that also happens to be small.
/// </summary>
[CreateAssetMenu(menuName = "Loot/Skip Rewards/Heal")]
public class HealSkipReward : SkipReward
{
    [SerializeField] private int amount = 5;

    public override string Label => $"Skip — Heal {amount}";

    public override IEnumerator Grant(RewardContext context)
    {
        if (context.character != null) { context.character.Heal(amount); }

        yield break;
    }
}
