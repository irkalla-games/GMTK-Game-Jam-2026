using System.Collections;
using UnityEngine;

public class BlockAction : GameAction
{
    private readonly int blockAmount;
    private readonly int blockCount;

    public BlockAction(int blockAmount, int blockCount)
    {
        this.blockAmount = blockAmount;
        this.blockCount = blockCount;
    }

    //Block is a reduction on each instance of damage, unlike shield which is extra health.
    public override IEnumerator Execute(ActionContext ctx)
    {
        foreach (Tiles target in ctx.targets)
        {
            target.gainBlock(blockAmount, blockCount);
        }
        yield return new WaitForSeconds(0.15f);
    }
}
