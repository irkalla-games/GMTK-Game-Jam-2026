using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Gain Parry")]
public class ParryEffect : CardEffect
{
    [SerializeField] private int parryCharges;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new ParryAction(parryCharges), ctx);
    }

    /// Parry needs somebody to land on, and it is always meant for your own side. Usually authored
    /// with AimsAt = Source, in which case Card.Refusal skips this entirely.
    public override string Refusal(Character source, GridTile target) =>
        RefuseByOccupant(source, target, wantAlly: true);
}
