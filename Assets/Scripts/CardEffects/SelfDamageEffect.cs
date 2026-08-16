using UnityEngine;

/// <summary>
/// A flat health cost the acting character pays for playing the card - Bloodied Resolve's "take 5
/// damage, draw a card" and Adrenaline's "take 5 damage, gain 1 energy" both spend this alongside
/// their real effect.
///
/// Unblockable by design: SelfDamageAction routes through Character.TakeUnblockableDamage, the same
/// path Poison uses, rather than Character.TakeDamage. A cost has to be paid in full regardless of
/// Shield, Block, Parry or Dodge - otherwise standing behind a wall of mitigation would make these
/// cards free, and a self-Parry would reflect the cost back into the character paying it. It is also
/// not scaled by Strength, since nobody is swinging.
/// </summary>
[CreateAssetMenu(menuName = "Card Effects/Self Damage")]
public class SelfDamageEffect : CardEffect
{
    [SerializeField] private int amount = 5;

    public override bool SupportsArea => false;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new SelfDamageAction(amount), ctx);
    }

    /// No refusal and no aiming to get wrong: SelfDamageAction always costs ctx.source, so this cannot
    /// be pointed at anybody else however the asset is authored - see DrawEffect's identical comment.
}
