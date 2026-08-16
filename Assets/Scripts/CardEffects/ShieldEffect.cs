using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Gain Shield")]
public class ShieldEffect : CardEffect
{
    [SerializeField] private int shieldAmount;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new ShieldAction(ctx.Amount(shieldAmount)), ctx);
    }

    /// Shield needs somebody to land on, and it is always meant for your own side. Usually the card's
    /// entry has aimsAt = Source, in which case Card.Refusal skips the check entirely.
    public override TargetAudience Audience => TargetAudience.Ally;
}
