using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Draw Cards")]
public class DrawEffect : CardEffect
{
    [SerializeField] private int drawAmount = 1;

    /// See CardEffect.RiderKind - an enemy drawing extra cards is worth telegraphing the same way a
    /// status rider is.
    public override IntentRiderKind RiderKind => IntentRiderKind.Draw;

    public override int RiderAmount => drawAmount;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new DrawAction(ctx.Amount(drawAmount)), ctx);
    }

    /// See CardEffect.ActsOnSource: DrawAction always draws for ctx.source, so this cannot be pointed
    /// at anybody else however the asset is authored.
    public override bool ActsOnSource => true;
}
