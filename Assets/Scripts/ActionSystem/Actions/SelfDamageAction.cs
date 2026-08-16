using System.Collections;
using UnityEngine;

public class SelfDamageAction : GameAction
{
    private readonly int amount;

    public SelfDamageAction(int amount)
    {
        this.amount = amount;
    }

    public override IEnumerator Execute(ActionContext ctx)
    {
        // Unblockable and unscaled - see SelfDamageEffect's doc comment for why this is
        // TakeUnblockableDamage rather than TakeDamage.
        if (ctx.source != null) { ctx.source.TakeUnblockableDamage(amount); }

        yield return new WaitForSeconds(ResolveDelay);
    }
}
