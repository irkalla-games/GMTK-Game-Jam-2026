using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;

public class DrawAction : GameAction
{
    private Character drawSource;
    private List<Character> drawTargets;
    private int drawAmount;


    public override IEnumerator Execute()
    {
        //Runs a loop that has every target in the list draw a certain amount of cards
        foreach (var target in drawTargets)
        {
            target.DrawCards(drawAmount);
        }
        yield return new WaitForSeconds(0.15f);
    }
}
