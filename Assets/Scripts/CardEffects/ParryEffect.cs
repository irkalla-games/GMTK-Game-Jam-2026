using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Gain Parry")]
public class ParryEffect : CardEffect
{
    [SerializeField] private int parryCharges;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new ParryAction(ctx.Amount(parryCharges)), ctx);
    }

    /// Parry needs somebody to land on, and it is always meant for your own side. Usually the card's
    /// entry has aimsAt = Source, in which case Card.Refusal skips the check entirely.
    public override TargetAudience Audience => TargetAudience.Ally;
}
