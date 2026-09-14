using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The rolling swap for one step of an intent readout: the incoming sprite falls in from above and
/// shoves the outgoing one out through a mask - the same "new thing lands, old thing gets pushed out"
/// mechanic TurnTransitionViewer plays for the turn-count roll, borrowed here as a placeholder until
/// the intent icon has motion of its own designed for it.
///
/// A plain class, not a MonoBehaviour and not [System.Serializable] - IntentStrip owns a pool of
/// these, one per step of a plan, and every tunable (duration, ease) is a strip-level setting passed
/// into Play rather than a per-instance field, so every step rolls with the same timing regardless of
/// which slot it happens to be. Build() constructs the masked window at runtime around a single Image,
/// so an authored prefab icon becomes a rolling one with no prefab edit, and a follow-up slot gets the
/// identical mechanism built from scratch.
///
/// One Tweener, killed by direct reference rather than by id/target search - the same rule CardViewer's
/// scaleTween/positionTween and TurnTransitionViewer's rollTween/fadeTween follow.
/// </summary>
public class IntentRoll
{
    private RectTransform window;
    private RectTransform stack;
    private Image outgoing;
    private Image incoming;

    private Tweener rollTween;

    /// The masked window Build creates around the authored icon - null until Build has run. Exposed so
    /// IntentDamageLabel can anchor beside the icon's own slot rather than the icon itself: the icon
    /// Image is duplicated into two rolling copies (see Build), so anything parented under it would be
    /// duplicated and roll away too.
    public RectTransform Window => window;

    /// <summary>
    /// Wraps `authored` in a masked window with two stacked copies of it. Call once, from Build, for
    /// both the primary slot (wrapping the icon already authored on the prefab) and every follow-up
    /// slot (wrapping an Image built from scratch to match it) - see IntentStrip.
    /// </summary>
    public void Build(Image authored)
    {
        RectTransform authoredRect = authored.rectTransform;
        Transform parent = authoredRect.parent;
        int siblingIndex = authoredRect.GetSiblingIndex();
        int layer = authored.gameObject.layer;

        GameObject windowGO = new("IntentWindow", typeof(RectTransform), typeof(RectMask2D));
        window = (RectTransform)windowGO.transform;
        window.SetParent(parent, false);
        window.SetSiblingIndex(siblingIndex);
        windowGO.layer = layer;

        // Takes over the authored icon's own slot exactly - same anchors, pivot and size - so the
        // window sits precisely where the single Image used to.
        window.anchorMin = authoredRect.anchorMin;
        window.anchorMax = authoredRect.anchorMax;
        window.pivot = authoredRect.pivot;
        window.anchoredPosition = authoredRect.anchoredPosition;
        window.sizeDelta = authoredRect.sizeDelta;

        GameObject stackGO = new("IntentStack", typeof(RectTransform));
        stack = (RectTransform)stackGO.transform;
        stack.SetParent(window, false);
        stackGO.layer = layer;
        FillParent(stack);

        // A fresh copy becomes the outgoing slot...
        outgoing = UnityEngine.Object.Instantiate(authored, stack);
        outgoing.name = "IntentOutgoing";
        FillParent(outgoing.rectTransform);
        outgoing.enabled = false;

        // ...and the authored Image itself becomes incoming, rather than being thrown away - so
        // whatever material or colour tweak was made to it in the Inspector is what both slots use.
        incoming = authored;
        incoming.transform.SetParent(stack, false);
        incoming.name = "IntentIncoming";
        FillParent(incoming.rectTransform);
        incoming.enabled = false;
    }

    private static void FillParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }

    /// <summary>
    /// Sets the icon straight to `sprite`, no roll. Used both for a host that never rolls at all (the
    /// screen-space enemy plate, whose shown character can change between two refreshes - rolling
    /// there would read as that enemy's intent changing rather than a change of who is being looked
    /// at) and for a slot's first activation on any host, so a freshly spawned enemy's icon or a boss's
    /// newly-visible follow-up does not fall in while its own spawn/appear animation is still playing.
    /// </summary>
    public void Show(Sprite sprite)
    {
        if (window == null) { return; }

        Kill();

        stack.anchoredPosition = Vector2.zero;
        outgoing.rectTransform.anchoredPosition = Vector2.zero;
        outgoing.enabled = false;

        incoming.rectTransform.anchoredPosition = Vector2.zero;
        incoming.sprite = sprite;
        incoming.enabled = sprite != null;
    }

    /// <summary>
    /// Rolls from `from` to `to`. Null on either side means the empty slot - Wait has no authored art,
    /// so clearing an intent rolls the icon away to nothing rather than to a blank sprite.
    ///
    /// Fire and forget: nothing awaits this. A change that lands mid-roll kills the in-flight tween and
    /// resets Stack to its settled position before starting the new one - `from` is always what the
    /// previous roll was carrying toward the centre, so this reads as that roll finishing early and the
    /// next one picking up from there, never as a reversal or a jump backward.
    ///
    /// `duration` and `ease` are the strip's own settings, not this instance's - see IntentStrip.Show,
    /// which is the only caller and passes the same two values for every slot on the strip.
    /// </summary>
    public void Play(Sprite from, Sprite to, float duration, Ease ease)
    {
        if (window == null) { return; }

        rollTween?.Kill();

        float distance = window.rect.height;

        stack.anchoredPosition = Vector2.zero;

        outgoing.rectTransform.anchoredPosition = Vector2.zero;
        outgoing.sprite = from;
        outgoing.enabled = from != null;

        incoming.rectTransform.anchoredPosition = new Vector2(0f, distance);
        incoming.sprite = to;
        incoming.enabled = to != null;

        // Unscaled: NotificationManager.Show can zero Time.timeScale mid-battle, and a scaled tween
        // behind a zeroed timescale would never finish.
        rollTween = stack.DOAnchorPosY(-distance, duration).SetEase(ease).SetUpdate(true);
    }

    /// Stops any in-flight roll without finishing it. Called from IntentStrip.Kill - an enemy is
    /// destroyed the instant it dies, possibly mid-roll.
    public void Kill()
    {
        rollTween?.Kill();
        rollTween = null;
    }

    /// Alpha multiplier on both rolling copies at once - what IntentStrip uses to dim a follow-up
    /// step relative to the primary, without needing to know this class keeps two separate Images to
    /// do it with.
    public void SetAlpha(float alpha)
    {
        if (window == null) { return; }

        Color inColor = incoming.color;
        inColor.a = alpha;
        incoming.color = inColor;

        Color outColor = outgoing.color;
        outColor.a = alpha;
        outgoing.color = outColor;
    }
}
