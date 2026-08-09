using UnityEngine;

/// <summary>
/// A pure animation beat with no mechanical effect - a wind-up before a bigger hit, a taunt, a victory
/// pose, a pure-VFX card. Placed in a card's effects list exactly like any other CardEffect, so the
/// author controls precisely where in the sequence it plays relative to the card's other actions - a
/// taunt before the swing is two entries in one list, in order.
/// </summary>
[CreateAssetMenu(menuName = "Card Effects/Animate")]
public class AnimateEffect : CardEffect
{
    [Tooltip("What the caster performs. A card's CardAnimation may still redirect this exactly like "
             + "any other action's cue - see CueOverride.when.")]
    [SerializeField] private AnimationCue cue = AnimationCue.None;

    public override void Resolve(ActionContext ctx)
    {
        ActionManager.Instance.AddAction(new AnimateAction(cue), ctx);
    }
}
