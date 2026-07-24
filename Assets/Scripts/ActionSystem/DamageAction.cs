using System.Collections;
using UnityEngine;

public class DamageAction : GameAction
{
    //Still need to define how we will be dealing damage. I imagine we pass a list of tiles to deal damage to. This way we can deal damaage in AOE or in a line, etc.
    //Each damage needs to have a source and amount of damage, though not sure if the source actually matters yet but may be useful for animations down the line
    //Character and Tiles are placehold names for now
    private Character damageSource;
    private List<Tiles> damageTargets;
    private int damageAmount;

    //Not sure why people use IEnumerator so need Andrew to clarify this
    public override IEnumerator Execute()
    {
        //Runs a loop that deals damage to each target in the list
        foreach (var target in damageTargets)
        {
            target.DealDamage(damageAmount);
        }
        yield return new WaitForSeconds(0.15f);
    }
}
