using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BlockAction : GameAction
{
    private Character blockSource;
    private List<Tiles> blockTargets;
    private int blockAmount;
    private int blockCount;

    //Block is going to be a reduction in each damage instance 
    public override IEnumerator Execute()
    {
        //Runs a loop that adds block to each target in the list
        foreach (var target in blockTargets)
        {
            //Having a certain amount of block, each will lower the damage taken by a certain value (this is different than shield which adds essentially extra health)
            //Will need to add code for characters to gain status effects such as block (and maybe poison down the line)
            target.GainBlock(blockAmount, blockCount);

        }
        yield return new WaitForSeconds(0.15f);
    }

}
