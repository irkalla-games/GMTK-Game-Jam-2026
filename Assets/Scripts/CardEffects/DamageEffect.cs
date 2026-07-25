using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Deal Damage")]
public class DamageEffect : CardEffect
{
    [SerializeField] int damageAmount;
    public override void Resolve(ActionContext ctx)
    {
        CardPlayManager.Instance.actionManager.AddAction(new DamageAction(damageAmount), ctx);
    }
}
