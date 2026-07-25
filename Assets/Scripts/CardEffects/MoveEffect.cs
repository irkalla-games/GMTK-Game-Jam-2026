using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Move")]
public class MoveEffect : CardEffect
{
    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new MoveAction(), ctx);
    }

    // The same method MoveCharacter itself consults, so the pre-flight check and the last line of
    // defence can never disagree about where a character may stand.
    public override string Refusal(Character source, GridTile target) =>
        GridManager.MoveRefusal(source, target);
}
