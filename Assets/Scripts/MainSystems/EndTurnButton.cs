using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The End Turn button's look. Dimmed while the party still has something it could play, full colour
/// once everybody is spent.
///
/// The dim is information, not a lock. The button stays clickable the whole time PlayerActing is
/// live - banking energy is a decision the player is allowed to make, and since nothing ends the turn
/// on its own any more (see BattleManager.RunBattle) a button that refused the click would be a
/// hard stall. All it says is "you have not finished yet".
///
/// Which is why this does not touch `interactable` for that: a non-interactable Button eats the
/// pointer. interactable is reserved for the times the click genuinely would do nothing - any phase
/// but PlayerActing, and while a reward panel owns the screen.
/// </summary>
[RequireComponent(typeof(Button))]
public class EndTurnButton : MonoBehaviour
{
    [Tooltip("The button's label. Tinted alongside the background, because Unity's ColorTint " +
        "transition only reaches the target graphic and would leave the text at full brightness on " +
        "a dimmed button.")]
    [SerializeField] private TMP_Text label;

    [Tooltip("What the button's colours are multiplied by while there is still something playable.")]
    [SerializeField] private Color dimTint = new(0.55f, 0.55f, 0.55f, 1f);

    private Button button;

    /// The ColorBlock the button was authored with. Captured once, because the dim is applied by
    /// replacing the whole block and the way back has to be the original rather than a guess.
    private ColorBlock restColors;

    private Color restLabelColor;

    /// Whether the dim is currently on. Nullable so the first evaluation always paints, whichever way
    /// it lands.
    private bool? dimmed;

    private void Awake()
    {
        button = GetComponent<Button>();
        restColors = button.colors;

        if (label != null) { restLabelColor = label.color; }
    }

    private void Start()
    {
        BattleManager battle = BattleManager.Instance;

        if (battle != null) { battle.PlayabilityChanged += RefreshDim; }

        RefreshDim();
    }

    private void OnDestroy()
    {
        if (BattleManager.Instance != null) { BattleManager.Instance.PlayabilityChanged -= RefreshDim; }
    }

    /// <summary>
    /// The cheap half of the state, per frame.
    ///
    /// Polled rather than driven by an event because the phase is the thing that moves here, and the
    /// battle announces every phase but the ones that matter: PlayerActing ends from inside
    /// RunBattle's own loop, so by the time anything is raised the phase has usually not flipped yet.
    /// Two field reads and a bool compare is a fair price for never showing a live button over an
    /// enemy's turn. The expensive half - CanAnyoneAct, which walks every hand - stays on the event.
    /// </summary>
    private void Update()
    {
        BattleManager battle = BattleManager.Instance;

        button.interactable = battle != null
            && battle.Phase == BattlePhase.PlayerActing
            && !battle.InputLocked;
    }

    private void RefreshDim()
    {
        BattleManager battle = BattleManager.Instance;
        bool wanted = battle != null && battle.CanAnyoneAct();

        if (dimmed == wanted) { return; }

        dimmed = wanted;

        // The whole ColorBlock, not the Image's colour directly. With ColorTint transition the
        // Selectable rewrites its target graphic every time the pointer enters or leaves, so a colour
        // written straight onto the Image survives exactly until the first hover.
        button.colors = wanted ? Dimmed(restColors) : restColors;

        if (label != null) { label.color = wanted ? Multiply(restLabelColor, dimTint) : restLabelColor; }
    }

    /// Every interactive entry multiplied down. disabledColor is left alone - it is the "you cannot
    /// click this" colour and has its own job, which is not the one being said here.
    private ColorBlock Dimmed(ColorBlock colors)
    {
        colors.normalColor = Multiply(colors.normalColor, dimTint);
        colors.highlightedColor = Multiply(colors.highlightedColor, dimTint);
        colors.pressedColor = Multiply(colors.pressedColor, dimTint);
        colors.selectedColor = Multiply(colors.selectedColor, dimTint);

        return colors;
    }

    /// Straight per-channel multiply, alpha included - so leaving dimTint's alpha at 1, as the default
    /// does, keeps the dim purely a matter of colour. Drop it below 1 only if the button really should
    /// fade out rather than mute.
    private static Color Multiply(Color color, Color tint) =>
        new(color.r * tint.r, color.g * tint.g, color.b * tint.b, color.a * tint.a);
}
