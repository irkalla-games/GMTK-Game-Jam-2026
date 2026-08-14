using UnityEngine;

/// <summary>
/// Grants Block charges. How much each one takes off a hit is BlockStatus.AmountPerHit, the same for
/// every Block in the game, so this authors only the count.
///
/// Now equivalent to an Apply Status asset authored with StatusType.Block - kept as its own type
/// because the three Block assets reference this script's GUID, and collapsing it would mean
/// re-authoring them for no gain.
/// </summary>
[CreateAssetMenu(menuName = "Card Effects/Gain Block")]
public class BlockEffect : CardEffect
{
    [Tooltip("How many incoming hits this Block applies to.")]
    [SerializeField] private int blockCount;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new BlockAction(blockCount), ctx);
    }

    /// Block needs somebody to land on, and it is always meant for your own side. Usually the card's
    /// entry has aimsAt = Source, in which case Card.Refusal skips this entirely.
    public override string Refusal(Character source, GridTile target) =>
        RefuseByOccupant(source, target, wantAlly: true);
}
