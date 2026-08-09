using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One entry in a CardAnimation: redirects the cue a GameAction would normally ask for, and optionally
/// sends a projectile from the caster to the target on the way.
///
/// `when` names the action's own default (DamageAction asks for MeleeAttack or RangedAttack depending
/// on the card's range, MoveAction for Move) rather than the action itself, so one override list can
/// redirect several different actions on the same card without knowing their class names - see
/// GameAction.Cue. Note a ranged card's entry has to say RangedAttack, not MeleeAttack: `when` is the
/// cue actually being asked for, which the card's own range already decided.
/// </summary>
[Serializable]
public struct CueOverride
{
    [Tooltip("The action's own default cue this entry replaces. For a damage card this is decided by "
             + "the card's range - RangedAttack past one tile, MeleeAttack at one - so a ranged card's "
             + "entry must say RangedAttack here even though it is redirecting to something else.")]
    public AnimationCue when;

    [Tooltip("What the caster performs instead. Leave at None to keep whatever the action already "
             + "asked for - which is what an entry that only exists to attach a projectile or an "
             + "impact wants. Set it to genuinely change the animation, or to Silent for a projectile "
             + "with no caster animation at all.")]
    public AnimationCue play;

    [Tooltip("Plays this exact Animator state instead of whatever the caster's own table binds `play` "
             + "to. Leave empty to use the caster's own binding - this is only for a card that wants a "
             + "specific clip a character's table does not otherwise expose.")]
    public string stateOverride;

    [Tooltip("Sprite for a travelling shot. Empty means no projectile - the caster's own animation is "
             + "the whole delivery, which is what a melee override wants.")]
    public Sprite projectileSprite;

    [Tooltip("Seconds the shot spends in the air. Unused when projectileSprite is empty; left at 0 "
             + "with a sprite set, a sensible default is used rather than the shot arriving instantly.")]
    public float travelDuration;

    [Tooltip("Uniform scale for the travelling sprite. 0 means 1, so leave it alone unless this card's "
             + "shot should read bigger or smaller than the art's natural size. To resize the art "
             + "everywhere it is used instead, change its Pixels Per Unit in the importer.")]
    public float projectileScale;

    [Tooltip("Degrees added to the shot's facing. The art is assumed to point right (+X) at rest, so "
             + "use -90 for a sprite drawn pointing up, 180 for one drawn pointing left.")]
    public float projectileRotation;

    [Tooltip("Stops the shot turning to face where it is going, which makes Projectile Rotation its "
             + "fixed angle rather than an offset. Off by default - that is the arrow behaviour, and "
             + "it is invisible on a round sprite anyway.")]
    public bool lockProjectileRotation;

    [Tooltip("Spawned on the target tile once the projectile lands, or immediately after the caster's "
             + "cue for a melee override. Optional.")]
    public GameObject impactPrefab;

    [Tooltip("Spawned on the target tile before a summon's body appears - the ritual VFX. Optional.")]
    public GameObject targetSpawnPrefab;
}

/// <summary>
/// A shared, reusable recipe for how a card's animation plays - the same relationship CardEffect has
/// to CardData. ArrowShot.asset is authored once and pointed at by every bow card; Explosion looking
/// different from a plain Fireball is just a second asset, no code change.
///
/// A card with no CardAnimation, or one with no entry for a given cue, animates exactly as the action
/// that plays it intended - see GameAction.Cue and Perform. Nothing here is required for a card to work.
/// </summary>
[CreateAssetMenu(menuName = "Card Animation")]
public class CardAnimation : ScriptableObject
{
    [SerializeField] private List<CueOverride> overrides = new();

    /// <summary>
    /// The entry replacing `defaultCue`, or a pass-through of it with no projectile and no state
    /// override. Never null, so a caller never needs its own "was this authored" branch.
    ///
    /// An entry that leaves `play` at None is treated as not redirecting at all, rather than as
    /// "animate nothing". That is what lets a card supply only a projectile sprite and still get the
    /// character's own animation for whatever the action asked for - Fireball and Ice Shard differ by
    /// their art, not by needing to restate the mage's cast. Suppressing the caster outright is
    /// AnimationCue.Silent, which has to be asked for by name.
    /// </summary>
    public CueOverride For(AnimationCue defaultCue)
    {
        foreach (CueOverride authored in overrides)
        {
            if (authored.when != defaultCue) { continue; }

            // A copy - CueOverride is a struct, so filling in the default here can never write back
            // into the shared asset.
            CueOverride entry = authored;

            if (entry.play == AnimationCue.None) { entry.play = defaultCue; }

            return entry;
        }

        return new CueOverride { when = defaultCue, play = defaultCue };
    }

    /// <summary>
    /// Face the target, perform the caster's cue, send a projectile if one is authored, then spawn the
    /// impact. Yields until the hit should actually land, so the GameAction calling this can do its
    /// mechanical work on exactly the right frame - a DamageAction's health-bar drop lands together
    /// with the impact VFX, never before the swing connects and never after.
    /// </summary>
    public IEnumerator Perform(ActionContext ctx, AnimationCue defaultCue)
    {
        CueOverride entry = For(defaultCue);

        CharacterAnimator animation = ctx.source != null ? ctx.source.Animation : null;
        GridTile aimedAt = ctx.targets.Count > 0 ? ctx.targets[0] : null;

        if (animation != null && aimedAt != null) { animation.SetFacing(aimedAt.transform.position); }

        IEnumerator casterCue = animation != null
            ? animation.Play(entry.play, entry.stateOverride)
            : null;

        if (casterCue != null) { yield return casterCue; }

        // The summon ritual, not the impact - a Summon card authors this instead of a projectile, and
        // the two are never expected together.
        if (entry.targetSpawnPrefab != null && aimedAt != null)
        {
            UnityEngine.Object.Instantiate(entry.targetSpawnPrefab, aimedAt.transform.position,
                                            Quaternion.identity);
        }

        if (entry.projectileSprite != null && aimedAt != null && animation != null)
        {
            Vector3 from = animation.MuzzlePosition;
            yield return Projectile.Travel(entry.projectileSprite, from, aimedAt.transform.position,
                                            entry.travelDuration, entry.projectileScale,
                                            entry.projectileRotation, entry.lockProjectileRotation);
        }

        if (entry.impactPrefab != null && aimedAt != null)
        {
            UnityEngine.Object.Instantiate(entry.impactPrefab, aimedAt.transform.position,
                                            Quaternion.identity);
        }
    }
}
