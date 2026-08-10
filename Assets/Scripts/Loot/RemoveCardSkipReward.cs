using System.Collections;
using UnityEngine;

/// <summary>
/// Declining the offer opens CardRemovalPanel on the picker's own run deck and waits for a choice - a
/// deck-thinning skip, for making a deck more consistent rather than bigger. Only meaningful where
/// context.record is set (a level-clear offer), since a mid-battle pickup's record still exists but
/// thinning it would not change anything already drawn this battle - nothing currently offers this
/// alongside Heal, but it degrades to a no-op rather than hanging if it ever is.
///
/// A cancel does not consume the offer: it sets context.Reoffer so LootManager's RunOffer shows the
/// same choices again, the same way backing out of anything else on the panel would.
/// </summary>
[CreateAssetMenu(menuName = "Loot/Skip Rewards/Remove Card")]
public class RemoveCardSkipReward : SkipReward
{
    public override string Label => "Skip — Remove a card";

    public override IEnumerator Grant(RewardContext context)
    {
        CardRemovalPanel panel = CardRemovalPanel.Instance;

        if (panel == null || context.record == null || context.record.deck == null) { yield break; }

        panel.Show(context.record.deck, context.character != null ? context.character.name : null);

        yield return new WaitUntil(() => panel.Resolved);

        if (panel.ChosenIndex >= 0 && panel.ChosenIndex < context.record.deck.Count)
        {
            context.record.deck.RemoveAt(panel.ChosenIndex);
        }
        else
        {
            context.Reoffer = true;
        }
    }
}
