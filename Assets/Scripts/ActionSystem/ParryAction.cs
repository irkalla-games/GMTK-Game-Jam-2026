using NUnit.Framework;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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
            target.GainParry(reflectTotal, parryCount);
        }
        yield return new WaitForSeconds(0.15f);
    }
}
