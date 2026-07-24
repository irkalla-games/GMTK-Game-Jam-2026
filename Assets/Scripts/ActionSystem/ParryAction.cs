using NUnit.Framework;
using UnityEngine;
using System.Collections;

public class ParryAction : GameAction
{
    private Character parrySource;
    private List<Tiles> parryTargets;
    private int reflectTotal;
    private int parryCount;

    public override IEnumerator Execute()
    {
        //Runs a loop that adds parry to each target in the list. This would negate damage and then reflect a certain amount back (maybe half, maybe full, unsure at this point)
        foreach (var target in parryTargets)
        {
            //Ask Andrew, may be good to have a generic status effect add. So like we had 4 Parry the characters. However this would be a bit restrictive in what we can add
            //Such as we would be adding the status effect and amount of them (like 8 blocks) but wouldn't be able to add in a total damage reflected to parry if we need parry amount too
            target.gainParry(reflectTotal, parryCount);

        }
        yield return new WaitForSeconds(0.15f);
    }
}
