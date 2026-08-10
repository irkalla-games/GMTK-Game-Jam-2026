using System.Collections;
using UnityEngine;

/// <summary>
/// Declines the offer and does nothing else - the generic "no thanks" for an occasion (a level clear)
/// where Heal would not make sense as the only alternative to a card. Exists as an asset like every
/// other SkipReward rather than a hardcoded button, so LootManager's skip lists stay uniform - see
/// SkipReward's class doc.
/// </summary>
[CreateAssetMenu(menuName = "Loot/Skip Rewards/None")]
public class NoneSkipReward : SkipReward
{
    public override string Label => "Skip";

    public override IEnumerator Grant(RewardContext context)
    {
        yield break;
    }
}
