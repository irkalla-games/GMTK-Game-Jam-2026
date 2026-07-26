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
        // Drawing is about the character resolving the action, not about a tile on the board. This
        // used to loop ctx.targets and draw for whoever was standing there - so Quick Attack, aimed
        // at a goblin, drew a card for the goblin. Every character owns a deck, so it succeeded
        // silently and the attacker got nothing.
        if (ctx.source != null) { ctx.source.DrawCards(drawAmount); }

        yield return new WaitForSeconds(ResolveDelay);
    }
}
