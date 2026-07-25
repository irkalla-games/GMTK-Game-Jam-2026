using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Draw Cards")]
public class DrawEffect : CardEffect
{
    [SerializeField] private int drawAmount = 1;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new DrawAction(drawAmount), ctx);
    }

    /// No refusal and no aiming to get wrong: DrawAction always draws for ctx.source, so this cannot
    /// be pointed at anybody else however the asset is authored.
}
