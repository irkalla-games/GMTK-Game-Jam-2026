using System.Collections;

/// <summary>
/// A single step of game logic, built by a CardEffect each time its card is played.
///
/// Actions are stateless: their fields are readonly authoring numbers handed in at construction, and
/// everything that varies per play arrives through the ActionContext. Nothing is written to a field
/// during resolution - keep per-resolution state in coroutine locals, which are already per-call.
///
/// Deliberately a plain C# class, not a ScriptableObject. Actions are never assets - they are built
/// with `new` on every play, and most of them take constructor arguments, which ScriptableObject
/// cannot do (Unity would demand CreateInstance and only ever call a parameterless constructor).
/// CardEffect is the ScriptableObject in this pair; the action it queues is not.
/// </summary>
public abstract class GameAction
{
    /// <summary>
    /// How long the queue pauses after this action, so effects read one at a time rather than all
    /// landing on the same frame. Comes from ActionManager so the pacing of the whole game is one
    /// serialized number instead of a literal repeated in every action.
    ///
    /// Virtual, so an action that animates for longer - a multi-tile move - can lengthen its own.
    /// Falls back to the old literal if no ActionManager is in the scene, which keeps actions
    /// runnable from a test harness.
    /// </summary>
    protected virtual float ResolveDelay =>
        ActionManager.Instance != null ? ActionManager.Instance.DefaultResolveDelay : 0.15f;

    /// <summary>
    /// What this action looks like by default - DamageAction asks for MeleeAttack or RangedAttack
    /// depending on the card's own range, MoveAction for Move. None (the default) means this action
    /// never animates on its own; a card can still add one through AnimateEffect. A card's
    /// CardAnimation may redirect a declared cue to something else entirely - see CueOverride.when.
    ///
    /// Takes ctx rather than being a bare property because DamageAction's answer depends on
    /// ctx.card.range - the same TargetRange.IsRanged threshold CardViewer uses to pick the sword or
    /// bow icon.
    /// </summary>
    protected virtual AnimationCue Cue(ActionContext ctx) => AnimationCue.None;

    /// <summary>
    /// Runs this action's animation and waits for it: face the target, perform the caster's cue,
    /// send a projectile if the card authored one, spawn the impact. Resolves immediately - no wait,
    /// no queue delay - when the card has no CardAnimation and this action declares no Cue, which is
    /// every action and every card exactly as they behaved before this existed.
    ///
    /// Actions bake this in directly rather than going through a separate queued step, on purpose: a
    /// card that queues several actions (a 5-hit card, an attack followed by a move) gets one
    /// animation per action, played back to back in the same order the actions themselves resolve in
    /// - because ActionManager already resolves one action's whole Execute before starting the next.
    /// </summary>
    protected IEnumerator Perform(ActionContext ctx)
    {
        AnimationCue cue = Cue(ctx);

        if (cue == AnimationCue.None) { yield break; }

        CardAnimation animation = ctx.card != null ? ctx.card.animation : null;

        if (animation != null)
        {
            yield return animation.Perform(ctx, cue);
        }
        else
        {
            // No CardAnimation authored - still perform the caster's own binding for this cue, with
            // no projectile and no override, so an unauthored card still swings on the character's
            // default clip rather than animating nothing.
            CharacterAnimator caster = ctx.source != null ? ctx.source.Animation : null;

            if (caster != null)
            {
                GridTile aimedAt = ctx.targets.Count > 0 ? ctx.targets[0] : null;
                if (aimedAt != null) { caster.SetFacing(aimedAt.transform.position); }

                yield return caster.Play(cue);
            }
        }
    }

    public abstract IEnumerator Execute(ActionContext ctx);
}
