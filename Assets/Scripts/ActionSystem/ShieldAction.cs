using NUnit.Framework;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ShieldAction : GameAction
{
    private Character shieldSource;
    private List<Tiles> shieldTargets;
    private int shieldAmount;

    public override IEnumerator Execute()
    {
        //Runs a loop that adds Shield to each target.
        foreach (var target in shieldTargets)
        {
            target.GainShield(shieldAmount);
        }
        yield return new WaitForSeconds(0.15f);
    }
}
