using System.Collections;
using UnityEngine;

public class DrawAction : GameAction
{
    private readonly int drawAmount;

    public DrawAction(int drawAmount)
    {
        this.drawAmount = drawAmount;
    }

    public override IEnumerator Execute(ActionContext ctx)
    {
        //Drawing is about the character resolving the action, not about a tile on the board.
        if (ctx.targets != null)
        {
            foreach (var target in ctx.targets) {
                target.DrawCards(drawAmount);
            }
        }
        yield return new WaitForSeconds(0.15f);
    }
}
