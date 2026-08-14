using System.Collections;
using UnityEngine;

/// Grants Block charges to whoever is standing on each target tile. Block is a reduction on each
/// instance of damage, unlike Shield which is extra health - see BlockStatus.
public class BlockAction : GameAction
{
    private readonly int blockCount;

    public BlockAction(int blockCount)
    {
        this.blockCount = blockCount;
    }

    public override IEnumerator Execute(ActionContext ctx)
    {
        foreach (GridTile target in ctx.targets)
        {
            target.GainBlock(blockCount);
        }

        yield return new WaitForSeconds(ResolveDelay);
    }
}
