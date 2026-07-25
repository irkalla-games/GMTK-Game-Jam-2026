using System.Collections;
using System.Collections.Generic;
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
        foreach (GridTile target in ctx.targets)
        {
            //Having a certain amount of block, each will lower the damage taken by a certain value (this is different than shield which adds essentially extra health)
            //Will need to add code for characters to gain status effects such as block (and maybe poison down the line)
            target.GainBlock(blockAmount, blockCount);

        }
        yield return new WaitForSeconds(ResolveDelay);
    }
}
