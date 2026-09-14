using UnityEngine;

/// <summary>
/// Opens CardChoicePanel over the acting character's hand and discards whichever card the player
/// picks - Foresight's "draw a card, discard a card". Always resolves after any Draw entry on the same
/// card, since CardData.effectEntries run in authored order, so the freshly drawn card is a legal
/// choice by the time this opens.
/// </summary>
[CreateAssetMenu(menuName = "Card Effects/Discard")]
public class DiscardEffect : CardEffect
{
    public override bool SupportsArea => false;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new DiscardAction(), ctx);
    }

    /// See CardEffect.ActsOnSource: DiscardAction always discards from ctx.source's own hand, so this
    /// cannot be pointed at anybody else however the asset is authored.
    public override bool ActsOnSource => true;
}
