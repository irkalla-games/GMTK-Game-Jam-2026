using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The rolling swap for an enemy's overhead intent icon: the incoming sprite falls in from above and
/// shoves the outgoing one out through a mask - the same "new thing lands, old thing gets pushed out"
/// mechanic TurnTransitionViewer plays for the turn-count roll, borrowed here as a placeholder until
/// the intent icon has motion of its own designed for it.
///
/// A plain serializable class, not a MonoBehaviour - CharacterOverheadViewer owns exactly one, and the
/// hierarchy this needs (a masked window plus two stacked icons) does not exist on any prefab today.
/// Build() constructs it at runtime around the single Image already authored there, so every existing
/// intentIcon becomes a rolling one with no prefab edit.
///
/// One Tweener, killed by direct reference rather than by id/target search - the same rule CardViewer's
/// scaleTween/positionTween and TurnTransitionViewer's rollTween/fadeTween follow.
/// </summary>
[System.Serializable]
public class IntentRoll
{
    [Tooltip("Seconds the icon takes to fall in and push the previous one out.")]
    [SerializeField] private float rollDuration = 0.5f;

    [Tooltip("DOTween's configured default (OutQuad) reads floaty for a slam like this - OutCubic makes "
             + "the icon land, the same choice TurnTransitionViewer makes for its roll.")]
    [SerializeField] private Ease rollEase = Ease.OutCubic;

    private RectTransform window;
    private RectTransform stack;
    private Image outgoing;
    private Image incoming;

    private Tweener rollTween;

    /// <summary>
    /// Wraps `authored` - the single Image CharacterOverheadViewer already exposes in the Inspector -
    /// in a masked window with two stacked copies of it. Call once, from Awake, and only when authored
    /// is not null; heroes and the Totem leave intentIcon empty and never call this.
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
    /// Sets the icon straight to `sprite`, no roll. The initial state on spawn, so a wave enemy's
    /// first committed intent does not fall in while its own spawn scale-in is still playing.
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
    /// </summary>
    public void Play(Sprite from, Sprite to)
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
        rollTween = stack.DOAnchorPosY(-distance, rollDuration).SetEase(rollEase).SetUpdate(true);
    }

    /// Stops any in-flight roll without finishing it. Called from CharacterOverheadViewer.OnDestroy -
    /// an enemy is destroyed the instant it dies, possibly mid-roll.
    public void Kill()
    {
        rollTween?.Kill();
        rollTween = null;
    }
}
