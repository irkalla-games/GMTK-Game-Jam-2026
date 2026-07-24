using UnityEngine;
using System.Collections;
using NUnit.Framework;
using System.Collections.Generic;

public class HealAction : GameAction
{
    private Character healSource;
    private List<Tiles> healTargets;
    private int healAmount;
    public override IEnumerator Execute()
    {
        //Runs a loop that heals each target in the list
        foreach (var target in healTargets)
        {
            //Heals each target on the tiles
            target.Heal(healAmount);

        }
        yield return new WaitForSeconds(0.15f);
    }
}
