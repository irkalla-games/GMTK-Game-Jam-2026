using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Bash", menuName = "Card Data/Bash")]
public class BashCardData : CardData
{
    [SerializeField] private int damageAmount = 8;
    [SerializeField] private int drawAmount = 1;

    public override IEnumerable<(GameAction action, ActionContext ctx)> CreateActions(Character source, Tiles target)
    {
        //Damage lands on the tile the player picked...
        yield return (new DamageAction(damageAmount), new ActionContext(source, target));

        //...but the draw is for whoever played the card, not whatever got hit.
        yield return (new DrawAction(drawAmount), new ActionContext(source));
    }
}
