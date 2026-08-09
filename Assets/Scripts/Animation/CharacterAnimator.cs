using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// One row of a character's cue table - what Animator state plays a given AnimationCue, and for how
/// long. Declared alongside CharacterAnimator the same way CardKeywordEntry sits alongside CardData.
/// </summary>
[System.Serializable]
public struct CueBinding
{
    public AnimationCue cue;

    [Tooltip("Animator state name to play for this cue.")]
    public string stateName;

    [Tooltip("How long to hold this state before returning to idle. 0 reads the clip's own length off "
             + "the animator controller, so this only needs setting when a duration should differ from "
             + "the clip itself.")]
    public float duration;
}

/// <summary>
/// The character's own vocabulary: which AnimatorController state answers each AnimationCue, and how
/// this particular body flips to face a target. A GameAction asks for a cue by name - Attack, Move,
/// Hurt - and never learns the state name or clip length behind it, which is what lets one DamageAction
/// drive a Knight's sword swing and a SkeletonWarrior's claw swipe with the same call.
///
/// A cue this character has no row for - or a character with no Animator at all, like Totem - simply
/// animates nothing and returns immediately. That is what keeps every card playable on a body nobody
/// has authored animations for yet.
/// </summary>
[RequireComponent(typeof(Character))]
public class CharacterAnimator : MonoBehaviour
{
    [SerializeField] private Animator animator;

    [Tooltip("State to return to once a cue's hold time elapses.")]
    [SerializeField] private string idleStateName = "Idle";

    [SerializeField] private List<CueBinding> cues = new();

    [Tooltip("Root SpriteRenderer to mirror when facing left - enemies, which are a single sprite. "
             + "Leave empty on a multi-part puppet; use counterFlip instead.")]
    [SerializeField] private SpriteRenderer flipRenderer;

    [Tooltip("A child to counter-flip when this transform itself is mirrored - heroes, whose puppet is "
             + "several SpriteRenderers with no single one to flip, and whose overhead Canvas must not "
             + "mirror along with the rest of the body. Leave empty on a single-sprite enemy; use "
             + "flipRenderer instead.")]
    [SerializeField] private Transform counterFlip;

    [Tooltip("Where a projectile launches from and melee impact VFX centres. Falls back to this "
             + "transform when empty.")]
    [SerializeField] private Transform muzzle;

    [Tooltip("Which way this character's art already faces before any mirroring. Untick for a sprite "
             + "set drawn facing left. Purely about the source art - the two asset packs in this "
             + "project do not agree with each other, so this is per character rather than a global "
             + "rule, and it is never overwritten by the animation setup tool.")]
    [SerializeField] private bool facesRightByDefault = true;

    private const float SpawnDuration = 0.3f;
    private const float DeathFadeDuration = 0.5f;

    private Character character;

    /// Set the moment Died fires, before the death coroutine even starts - so a Hurt raised in the
    /// same frame (e.g. a killing blow that also triggers a reflected parry tick) cannot stomp the
    /// death clip that is about to play.
    private bool dying;

    public Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position;

    private void Awake()
    {
        character = GetComponent<Character>();
        if (animator == null) { animator = GetComponent<Animator>(); }
    }

    private void Start()
    {
        character.Damaged += OnDamaged;
        character.Died += OnDied;
    }

    private void OnDestroy()
    {
        if (character == null) { return; }

        character.Damaged -= OnDamaged;
        character.Died -= OnDied;
    }

    private void OnDamaged(Character _, int amount)
    {
        if (dying) { return; }

        StartCoroutine(Play(AnimationCue.Hurt));
    }

    private void OnDied(Character _)
    {
        dying = true;
        StartCoroutine(PlayDeath());
    }

    /// <summary>
    /// Plays one cue and waits for it, returning the Animator to idle afterward.
    ///
    /// stateOverride lets a card redirect which clip actually plays - a card asking a mage for Cast
    /// instead of the RangedAttack a plain DamageAction would default to - without this character
    /// needing a row in its own table for a state it did not otherwise author. yield breaks
    /// immediately with no Animator, no binding for `cue`, and no override: the caller's queue is
    /// never held up by a body that has nothing to show.
    ///
    /// durationOverride holds the state for a caller-supplied time instead of the clip's own length.
    /// MoveAction is the reason: a walk has to last exactly as long as the tile-to-tile tween, and a
    /// looping walk clip that happens to be shorter would otherwise drop back to idle mid-slide.
    /// </summary>
    public IEnumerator Play(AnimationCue cue, string stateOverride = null, float durationOverride = 0f)
    {
        // Silent is an authored "perform nothing" and stops here exactly like an unset cue - a card
        // asking for it still gets its projectile and impact, from CardAnimation.Perform.
        if (cue == AnimationCue.None || cue == AnimationCue.Silent || animator == null) { yield break; }

        CueBinding? binding = Find(cue);

        if (binding == null && string.IsNullOrEmpty(stateOverride)) { yield break; }

        string state = !string.IsNullOrEmpty(stateOverride) ? stateOverride : binding.Value.stateName;

        float duration;

        if (durationOverride > 0f)
        {
            duration = durationOverride;
        }
        else if (binding != null && string.IsNullOrEmpty(stateOverride) && binding.Value.duration > 0f)
        {
            duration = binding.Value.duration;
        }
        else
        {
            duration = ClipLength(state);
        }

        animator.Play(state, 0, 0f);

        if (duration > 0f) { yield return new WaitForSeconds(duration); }

        animator.Play(idleStateName, 0, 0f);
    }

    /// <summary>
    /// Faces this body toward a world position - purely which way it looks, not where it stands.
    ///
    /// Two strategies, picked by which field is wired. A single-sprite body (the enemies) sets
    /// flipRenderer and mirrors through SpriteRenderer.flipX, which leaves the transform - and so the
    /// overhead health-bar Canvas hanging off it - completely alone. A rigged puppet (the heroes) has
    /// no one renderer to flip and its Body's own localScale is animated by the attack/cast/walk clips
    /// every frame, so the character root mirrors instead, and counterFlip (the overhead Canvas) is
    /// mirrored right back so the health bar does not read backwards.
    /// </summary>
    public void SetFacing(Vector3 worldTarget)
    {
        float delta = worldTarget.x - transform.position.x;

        // Directly above or below on an isometric board - two tiles can share a world x. Keep whatever
        // way we were already facing rather than snapping to an arbitrary side.
        if (Mathf.Approximately(delta, 0f)) { return; }

        bool mirrored = (delta > 0f) != facesRightByDefault;

        if (flipRenderer != null)
        {
            flipRenderer.flipX = mirrored;
            return;
        }

        Vector3 scale = transform.localScale;
        scale.x = mirrored ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
        transform.localScale = scale;

        // Cancels the root's mirror on this child specifically: -1 against the root's -1 comes out
        // upright. Without it the health bar mirrors along with the body.
        if (counterFlip != null)
        {
            Vector3 counter = counterFlip.localScale;
            counter.x = mirrored ? -Mathf.Abs(counter.x) : Mathf.Abs(counter.x);
            counterFlip.localScale = counter;
        }
    }

    /// Grows a freshly summoned body in from nothing - the same trick CardViewer.PlaySpawnIn uses for
    /// a card dealt into hand. Called by SummonAction right after the body is instantiated and placed.
    public IEnumerator SpawnIn()
    {
        Vector3 rest = transform.localScale;
        transform.localScale = Vector3.zero;
        transform.DOScale(rest, SpawnDuration);
        yield return new WaitForSeconds(SpawnDuration);
    }

    private IEnumerator PlayDeath()
    {
        yield return Play(AnimationCue.Die);

        // DOTween.To rather than the SpriteRenderer.DOFade shortcut - that extension only exists
        // when DOTween's optional Sprite module has been enabled via its setup utility, which this
        // project has not done (nothing else here tweens a colour). DOTween.To is core and always
        // available regardless.
        foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>())
        {
            Color transparent = renderer.color;
            transparent.a = 0f;
            DOTween.To(() => renderer.color, c => renderer.color = c, transparent, DeathFadeDuration);
        }

        yield return new WaitForSeconds(DeathFadeDuration);

        Destroy(gameObject);
    }

    private CueBinding? Find(AnimationCue cue)
    {
        foreach (CueBinding binding in cues)
        {
            if (binding.cue == cue) { return binding; }
        }

        return null;
    }

    /// <summary>
    /// Falls back to the animator's own clip length when a binding leaves duration at 0, so a row
    /// added by hand needs only a cue and a state name.
    ///
    /// Only a clip *name* is reachable at runtime - there is no way to ask a RuntimeAnimatorController
    /// what motion a given state holds - and the two naming schemes in play disagree: the pack's hero
    /// clips are named exactly like their states ("attack"), while generated ones are prefixed with
    /// the character to stay unique in one folder ("SkeletonWarrior_MeleeAttack"). Hence the suffix
    /// pass. The animation setup tool resolves and writes real durations up front precisely so this
    /// guessing is a safety net rather than the mechanism.
    /// </summary>
    private float ClipLength(string stateName)
    {
        if (animator == null || animator.runtimeAnimatorController == null) { return 0f; }

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;

        foreach (AnimationClip clip in clips)
        {
            if (clip != null && clip.name == stateName) { return clip.length; }
        }

        foreach (AnimationClip clip in clips)
        {
            if (clip != null && clip.name.EndsWith("_" + stateName)) { return clip.length; }
        }

        return 0f;
    }
}
