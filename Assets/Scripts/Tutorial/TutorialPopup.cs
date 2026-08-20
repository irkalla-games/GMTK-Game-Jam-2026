using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The tutorial's instruction box: a title, a sentence saying what to do, and the two buttons that can
/// ever end a step - Continue and Skip Tutorial.
///
/// **Built on the tooltip's pattern, not the notification's.** A notification is always centre-screen
/// and always modal; this has to sit *beside* whatever is currently lit, because the whole point is to
/// say "that thing there, click it." So it takes a TooltipAnchor and places itself through
/// AnchoredPlacement, exactly as the tooltip box does, and re-resolves it every frame so the box follows
/// a card that is scaling up or a character that is walking.
///
/// **It never zeroes Time.timeScale**, though NotificationManager.Show does. BattleManager.InputLocked's
/// doc comment sets out why that is the wrong tool: a zeroed timescale stalls the WaitForSeconds inside
/// EnemyResolve, and does not actually stop a click anyway, since OnMouseDown is a physics raycast no
/// uGUI panel intercepts. TutorialGate is what stops clicks here, and it stops exactly the wrong ones.
///
/// A popup is up on *every* step, not only the ones with a Continue button. The two kinds differ only in
/// what takes the box down: a read step waits for Continue, an action step waits for the player to do
/// the thing and dismisses itself. That is why `showContinue` is a parameter rather than the box
/// deciding for itself.
/// </summary>
public class TutorialPopup : Singleton<TutorialPopup>
{
    [Tooltip("The canvas this box lives on. Positioning is done in its local space, so panel must be a "
             + "direct child of it. Ordered above TutorialSpotlight so the box is never dimmed.")]
    [SerializeField] private Canvas canvas;

    [Tooltip("The outer bordered rect, built by Tools > Tutorial > Wire Tutorial Overlay.")]
    [SerializeField] private RectTransform panel;

    [Tooltip("Hides the box without deactivating it, so its ContentSizeFitter has already settled by the "
             + "time it is shown - the same reason TooltipManager fades rather than toggling.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [SerializeField] private TMP_Text titleText;

    [SerializeField] private TMP_Text bodyText;

    [Tooltip("Advances a read step. Hidden on action steps, where doing the thing is what advances.")]
    [SerializeField] private Button continueButton;

    [Tooltip("Abandons the tutorial. Always available - a step that fails to advance must never be able "
             + "to trap someone in it.")]
    [SerializeField] private Button skipButton;

    [Tooltip("Gap between the box and the thing it is explaining.")]
    [SerializeField] private float gap = 18f;

    [Tooltip("Closest the box may get to the edge of the screen before it is pushed back inside.")]
    [SerializeField] private float edgePadding = 24f;

    /// What the box is pointing at, or null for a centre-screen beat. Held rather than snapshotted for
    /// the reason TooltipAnchor exists - see the class doc.
    private TooltipAnchor? anchor;

    /// Latched by the buttons and drained by the director's coroutine. Latched rather than an event
    /// because the director is a coroutine polling a WaitUntil, and an event would fire on a frame it
    /// was not waiting.
    private bool continuePressed;

    public bool IsShowing { get; private set; }

    /// Sticky once set: every step's wait checks it, so the sequence unwinds from wherever it had got
    /// to. Cleared only when a tutorial starts.
    public bool SkipRequested { get; private set; }

    protected override void Awake()
    {
        base.Awake();

        // Log and degrade rather than throw, the same as TooltipManager.Awake.
        if (canvas == null || panel == null || canvasGroup == null || titleText == null
            || bodyText == null || continueButton == null || skipButton == null)
        {
            Debug.LogError($"{name}: TutorialPopup is missing part of its skeleton - run "
                           + "Tools > Tutorial > Wire Tutorial Overlay, or no tutorial copy will show");
            enabled = false;
            return;
        }

        continueButton.onClick.AddListener(() => continuePressed = true);
        skipButton.onClick.AddListener(() => SkipRequested = true);

        SetVisible(false);
    }

    /// Clears the latch so a second run of the tutorial in one session does not start already skipped.
    public void BeginTutorial()
    {
        SkipRequested = false;
        continuePressed = false;
    }

    public void Show(string title, string body, TooltipAnchor? pointsAt, bool showContinue)
    {
        titleText.text = title;
        bodyText.text = body;

        anchor = pointsAt;
        continuePressed = false;

        continueButton.gameObject.SetActive(showContinue);

        // Forced now, not next frame: the box is about to be positioned from its own height, and a
        // ContentSizeFitter that has not run yet still reports the previous step's size. Twice, for the
        // reason TooltipManager.Render gives - a row reactivated this frame reports last frame's size on
        // the first pass.
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        IsShowing = true;
        SetVisible(true);

        Reposition();
    }

    public void Hide()
    {
        IsShowing = false;
        anchor = null;
        SetVisible(false);
    }

    /// Returns true once per press, and clears it. The director's steps each wait on this, so a press
    /// left latched would advance the next step too.
    public bool ConsumeContinue()
    {
        if (!continuePressed) { return false; }

        continuePressed = false;
        return true;
    }

    /// LateUpdate so the anchor has finished moving for this frame - the same timing rule
    /// TooltipManager and TutorialSpotlight both follow, which is also what keeps the box and the hole
    /// it belongs to agreeing with each other rather than one trailing the other.
    private void LateUpdate()
    {
        if (!IsShowing) { return; }

        Reposition();
    }

    private void Reposition()
    {
        RectTransform canvasRect = (RectTransform)canvas.transform;

        // No anchor is a beat about the game as a whole rather than about one object - centre screen,
        // which is also where the box lands if its anchor has just been destroyed.
        if (anchor == null || !anchor.Value.TryResolve(out Rect screenRect))
        {
            panel.localPosition = new Vector3(0f, 0f, panel.localPosition.z);
            return;
        }

        Vector2 position = AnchoredPlacement.Place(
            screenRect, anchor.Value.side, canvasRect, canvas, panel.rect.size, gap, edgePadding);

        // localPosition, not anchoredPosition - see TooltipManager.Position for why.
        panel.localPosition = new Vector3(position.x, position.y, panel.localPosition.z);
    }

    private void SetVisible(bool visible)
    {
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

}
