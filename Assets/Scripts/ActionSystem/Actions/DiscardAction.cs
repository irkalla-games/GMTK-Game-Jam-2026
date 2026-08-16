using System.Collections;
using UnityEngine;

/// <summary>
/// Opens CardChoicePanel over ctx.source's hand and blocks until the player picks one, then discards
/// it - the same "coroutine waits on a panel's Resolved flag" shape BattleManager.EnemyResolve already
/// uses for LootManager.
///
/// The card that played this effect is never a candidate: CardPlayManager.PlaySelectedOn discards the
/// played card into the discard pile immediately after Card.ResolveEffects returns, and that happens
/// before this action - queued by that same ResolveEffects call - ever reaches the front of
/// ActionManager's queue. By the time the panel opens, only cards still actually in hand are offered.
/// </summary>
public class DiscardAction : GameAction
{
    public override IEnumerator Execute(ActionContext ctx)
    {
        if (ctx.source == null || ctx.source.Hand.Count == 0 || CardChoicePanel.Instance == null)
        {
            yield break;
        }

        CardChoicePanel.Instance.Show(ctx.source.Hand, "Choose a card to discard");

        yield return new WaitUntil(() => CardChoicePanel.Instance.Resolved);

        int index = CardChoicePanel.Instance.ChosenIndex;

        if (index >= 0 && index < ctx.source.Hand.Count)
        {
            ctx.source.Discard(ctx.source.Hand[index]);
        }
    }
}
