using System.Collections;
using UnityEngine;

/// <summary>
/// Declining the offer opens CardRemovalPanel on the picker's own run deck, greying out every card
/// with no CardData.upgradedForm, and replaces whichever eligible one the player picks with its upgraded
/// form outright - RemoveCardSkipReward's sibling, sharing the same panel (see CardRemovalPanel.Show's
/// `eligible` parameter) rather than a second grid screen. Only meaningful where context.record is set
/// (a level-clear offer) - same reasoning RemoveCardSkipReward already documents for why nothing
/// currently offers this mid-battle.
///
/// A cancel does not consume the offer: it sets context.Reoffer so LootManager's RunOffer shows the
/// same choices again, the same way backing out of anything else on the panel would.
/// </summary>
[CreateAssetMenu(menuName = "Loot/Skip Rewards/Upgrade Card")]
public class UpgradeCardSkipReward : SkipReward
{
    public override string Label => "Skip — Upgrade a card";

    public override IEnumerator Grant(RewardContext context)
    {
        CardRemovalPanel panel = CardRemovalPanel.Instance;

        if (panel == null || context.record == null || context.record.deck == null) { yield break; }

        panel.Show(
            context.record.deck,
            context.character != null ? context.character.DisplayName : null,
            "choose a card to upgrade",
            data => data != null && data.upgradedForm != null);

        yield return new WaitUntil(() => panel.Resolved);

        int index = panel.ChosenIndex;
        CardData chosen = index >= 0 && index < context.record.deck.Count ? context.record.deck[index] : null;

        if (chosen != null && chosen.upgradedForm != null)
        {
            context.record.deck[index] = chosen.upgradedForm;
        }
        else
        {
            context.Reoffer = true;
        }
    }
}
