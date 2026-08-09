using System;
using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// The giant turn-count roll between the enemy's turn and the next player turn. The old number holds
/// centre-screen, then Stack carries both labels down together so the new one arrives as the old one
/// is shoved out the bottom of Window's mask - one motion, so the two numbers cannot drift apart the
/// way two independently-tweened labels could. onRoll fires at the moment the roll starts, which is
/// also the frame BattleManager uses to flip the small HUD counter, so the two stay in lockstep.
///
/// Everything here runs on unscaled time (SetUpdate(true) / WaitForSecondsRealtime), never scaled -
/// NotificationManager.Show can zero Time.timeScale while the round is still advancing (BattleManager
/// documents that timeScale 0 does not block OnMouseDown), and a scaled wait behind a zeroed timescale
/// would never elapse, hanging RunBattle forever.
///
/// Optional on BattleManager, same as turnCounter - a scene with no viewer wired up just skips the
/// beat. Holds at most one live Tweener per channel, killed by direct reference, never by id/target -
/// the same rule CardViewer's scaleTween/positionTween/rotationTween follow.
/// </summary>
public class TurnTransitionViewer : MonoBehaviour
{
    [Tooltip("Switched on for the duration of the roll and off between turns. Carries the CanvasGroup.")]
    [SerializeField] private GameObject root;

    [Tooltip("Fades the whole slot in and out.")]
    [SerializeField] private CanvasGroup group;

    [Tooltip("Carries the RectMask2D. Its height is the roll distance - fixed canvas units, so the "
             + "roll looks the same at every aspect ratio regardless of screen size.")]
    [SerializeField] private RectTransform window;

    [Tooltip("The only thing that moves. Both labels are its children, so one tween carries the pair - "
             + "two independent tweens have two start times and two ease curves, and any drift opens a "
             + "gap or overlaps the digits mid-roll.")]
    [SerializeField] private RectTransform stack;

    [Tooltip("The turn that just ended.")]
    [SerializeField] private TextMeshProUGUI outgoing;

    [Tooltip("The turn about to start.")]
    [SerializeField] private TextMeshProUGUI incoming;

    [Tooltip("Seconds to fade the old number in.")]
    [SerializeField] private float fadeInDuration = 0.12f;

    [Tooltip("Seconds the old number holds before the roll.")]
    [SerializeField] private float holdOldDuration = 0.25f;

    [Tooltip("Seconds Stack takes to slide the old number out and the new one in.")]
    [SerializeField] private float rollDuration = 0.40f;

    [Tooltip("Seconds the new number holds after the roll.")]
    [SerializeField] private float holdNewDuration = 0.45f;

    [Tooltip("Seconds to fade the new number out.")]
    [SerializeField] private float fadeOutDuration = 0.25f;

    [Tooltip("Roll distance in window heights. 1 = the old number is fully out of the window exactly "
             + "as the new one arrives at centre.")]
    [SerializeField] private float rollSpacing = 1f;

    [Tooltip("DOTween's configured default (OutQuad) reads floaty for a slam like this one - OutCubic "
             + "makes the number land.")]
    [SerializeField] private Ease rollEase = Ease.OutCubic;

    private Tweener rollTween;
    private Tweener fadeTween;

    public bool IsPlaying { get; private set; }

    private void Awake()
    {
        // Log and degrade rather than throw, the same as TooltipManager.Awake - a half-wired viewer
        // should cost you the countdown, not every turn boundary in the game.
        if (root == null || group == null || window == null || stack == null
            || outgoing == null || incoming == null)
        {
            Debug.LogError($"{name}: TurnTransitionViewer is missing a reference - no turn countdown " +
                "will play");
            enabled = false;
            return;
        }

        group.interactable = false;
        group.blocksRaycasts = false;
        group.alpha = 0f;
        root.SetActive(false);
    }

    private void OnDisable() => KillTweens();

    private void OnDestroy() => KillTweens();

    private void KillTweens()
    {
        // Tweener is a plain C# class, not a UnityEngine.Object, so ?. is the real null check here -
        // none of the fake-null hazard BattleManager.HandleCharacterDied warns about applies.
        rollTween?.Kill();
        rollTween = null;
        fadeTween?.Kill();
        fadeTween = null;
    }

    /// <summary>
    /// Plays the roll from `from` down to `to` and returns once it has fully faded out. onRoll fires
    /// exactly once, at the moment the roll itself starts (after the old number has held, before the
    /// new one arrives) - every exit path fires it, including the disabled-viewer guard, so a caller
    /// relying on it to flip its own display never goes stale.
    /// </summary>
    public IEnumerator Play(int from, int to, Action onRoll)
    {
        if (!isActiveAndEnabled)
        {
            onRoll?.Invoke();
            yield break;
        }

        KillTweens();
        IsPlaying = true;

        float distance = window.rect.height * rollSpacing;

        outgoing.text = from.ToString();
        incoming.text = to.ToString();

        stack.anchoredPosition = Vector2.zero;

        Vector2 outgoingPos = outgoing.rectTransform.anchoredPosition;
        outgoingPos.y = 0f;
        outgoing.rectTransform.anchoredPosition = outgoingPos;

        Vector2 incomingPos = incoming.rectTransform.anchoredPosition;
        incomingPos.y = distance;
        incoming.rectTransform.anchoredPosition = incomingPos;

        group.alpha = 0f;
        root.SetActive(true);

        yield return Fade(1f, fadeInDuration);

        yield return new WaitForSecondsRealtime(holdOldDuration);

        onRoll?.Invoke();
        yield return Roll(distance, rollDuration);

        yield return new WaitForSecondsRealtime(holdNewDuration);

        yield return Fade(0f, fadeOutDuration);

        KillTweens();
        root.SetActive(false);
        IsPlaying = false;
    }

    private IEnumerator Fade(float target, float duration)
    {
        if (duration <= 0f)
        {
            group.alpha = target;
            yield break;
        }

        fadeTween?.Kill();
        fadeTween = group.DOFade(target, duration).SetUpdate(true);
        yield return new WaitForSecondsRealtime(duration);
    }

    private IEnumerator Roll(float distance, float duration)
    {
        if (duration <= 0f)
        {
            stack.anchoredPosition = new Vector2(stack.anchoredPosition.x, -distance);
            yield break;
        }

        rollTween?.Kill();
        rollTween = stack.DOAnchorPosY(-distance, duration).SetEase(rollEase).SetUpdate(true);
        yield return new WaitForSecondsRealtime(duration);
    }
}
