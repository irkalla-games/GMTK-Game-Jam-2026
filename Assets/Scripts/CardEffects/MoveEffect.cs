using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Move")]
public class MoveEffect : CardEffect
{
    public override void Resolve(ActionContext ctx)
    {
        CardPlayManager.Instance.actionManager.AddAction(new MoveAction(), ctx);
    }
}
