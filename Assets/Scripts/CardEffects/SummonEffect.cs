using UnityEngine;

[CreateAssetMenu(menuName = "Card Effects/Summon")]
public class SummonEffect : CardEffect
{
    [SerializeField] private GameObject summonedObject;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new SummonAction(summonedObject), ctx);
    }
}
