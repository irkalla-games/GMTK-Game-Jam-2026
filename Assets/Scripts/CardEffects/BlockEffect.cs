using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Gain Block")]
public class BlockEffect : CardEffect
{
    [SerializeField] private int blockAmount;
    [SerializeField] private int blockCount;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new BlockAction(blockAmount, blockCount), ctx);
    }

    /// Block needs somebody to land on, and it is always meant for your own side. Usually the card's
    /// entry has aimsAt = Source, in which case Card.Refusal skips this entirely.
    public override string Refusal(Character source, GridTile target) =>
        RefuseByOccupant(source, target, wantAlly: true);
}
