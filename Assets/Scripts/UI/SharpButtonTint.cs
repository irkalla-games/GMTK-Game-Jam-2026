using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Per-state tinting for a Sharp GUI button, covering both the background Image and its TMP label.
///
/// Unity's own ColorBlock transition tints exactly one Graphic - the Selectable's targetGraphic - so a
/// button styled with it has a background that lights on hover and a label that stays flat. This is the
/// one idea worth taking from the Sharp GUI package's decorator components, minus their UniRx event
/// model: the four pointer interfaces below are what uGUI already raises, so no reactive framework is
/// needed to hear them.
///
/// The sibling Button's transition must be set to None wherever this is used - see
/// SharpSkin.ApplyButton, which does exactly that. Left on ColorTint, the two fight over
/// background.color and which one wins depends on call order.
///
/// Colours come from PanelPalette rather than being serialized per instance, so restyling every button
/// in the game stays the one-file edit it is for the rest of the panel chrome. Note that the Button*
/// colours multiply the sprite while the Label* colours replace the text colour outright - PanelPalette
/// documents why the two sets look so different.
/// </summary>
[RequireComponent(typeof(Button))]
[DisallowMultipleComponent]
public class SharpButtonTint : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("The Image tinted by the Button* colours. Usually this object's own Image.")]
    [SerializeField] private Image background;

    [Tooltip("The label tinted by the Label* colours. Optional - a button with only an icon leaves "
             + "this empty and is tinted on its background alone.")]
    [SerializeField] private TMP_Text label;

    private Button button;

    private bool hovered;

    private bool pressed;

    /// Last interactable state applied. Selectable raises no event when interactable changes and we
    /// have turned its own transition off, so this is polled - see Update.
    private bool wasInteractable;

    private void Awake()
    {
        button = GetComponent<Button>();

        wasInteractable = button.interactable;

        Apply();
    }

    /// A button hidden mid-hover never receives OnPointerExit, so it would come back lit. Clearing here
    /// rather than in OnDisable keeps the reset next to the Apply that acts on it.
    private void OnEnable()
    {
        hovered = false;
        pressed = false;

        Apply();
    }

    /// <summary>
    /// EndTurnButton rewrites interactable as the party's playable cards change, and CardPileHud does
    /// the same - both would otherwise leave a greyed-out button still painted as enabled. Compared
    /// rather than applied unconditionally so this costs one bool read per button per frame.
    /// </summary>
    private void Update()
    {
        if (button == null) { return; }

        if (button.interactable == wasInteractable) { return; }

        wasInteractable = button.interactable;

        // A button greyed out from under the cursor is no longer being hovered in any sense that
        // matters, and would come back pressed if the pointer went down on it first.
        if (!wasInteractable)
        {
            hovered = false;
            pressed = false;
        }

        Apply();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;

        Apply();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;

        // Releasing outside the button is a cancelled click, not a click - uGUI will not raise
        // OnPointerUp here, so the pressed state has to be dropped on the way out or it sticks.
        pressed = false;

        Apply();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pressed = true;

        Apply();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        pressed = false;

        Apply();
    }

    /// <summary>
    /// Repaints from the current state. Public so an editor wiring command can preview the resting
    /// look after assigning the fields, without entering Play Mode.
    /// </summary>
    public void Apply()
    {
        bool interactable = button != null && button.interactable;

        Color fill;
        Color ink;

        if (!interactable)
        {
            fill = PanelPalette.ButtonDisabled;
            ink = PanelPalette.LabelDisabled;
        }
        else if (pressed)
        {
            fill = PanelPalette.ButtonPressed;
            ink = PanelPalette.LabelPressed;
        }
        else if (hovered)
        {
            fill = PanelPalette.ButtonHover;
            ink = PanelPalette.LabelHover;
        }
        else
        {
            fill = PanelPalette.ButtonNormal;
            ink = PanelPalette.LabelNormal;
        }

        if (background != null) { background.color = fill; }

        if (label != null) { label.color = ink; }
    }

    /// <summary>
    /// Assigns the two graphics this tints. Used by SharpSkin.ApplyButton so an editor command can wire
    /// a button it has just skinned without reaching through SerializedObject for two public-by-intent
    /// fields.
    /// </summary>
    public void SetGraphics(Image backgroundImage, TMP_Text labelText)
    {
        background = backgroundImage;
        label = labelText;
    }
}
