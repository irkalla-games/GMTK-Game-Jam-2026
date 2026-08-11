using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    [Tooltip("Where Play's run reads its levels and carry-damage rule from. The party no longer comes "
             + "from here - see CharacterSelectPanel, which reads this same asset's Levels and "
             + "CarryDamageBetweenLevels but builds the party from what was chosen on screen.")]
    [SerializeField] private RunData campaign;

    [Tooltip("Who Play may offer at the character-select screen, and how large a party it allows.")]
    [SerializeField] private CharacterRoster roster;

    [Tooltip("Play/Settings/Exit/TutorialToggle - hidden individually while the select screen is up, "
             + "restored on Back. A list of the existing objects rather than a shared container so no "
             + "scene reparenting was needed to introduce this screen - the select screen has no idea "
             + "any of this exists, it only fires BackClicked.")]
    [SerializeField] private List<GameObject> menuButtons = new();

    [SerializeField] private CharacterSelectPanel selectPanel;

    [Tooltip("Runs the tutorial on the first level when ticked. Remembered between sessions - the "
             + "state authored here is only what the button looks like before GameSettings is read.")]
    [SerializeField] private Toggle tutorialToggle;

    public void exitButton(){
        Application.Quit();
        Debug.Log("Game Closed");
    }

    /// <summary>
    /// Opens the character-select screen instead of starting a run outright - picking a party size,
    /// heroes and starting decks now happens there. StartRun and the scene load themselves moved to
    /// CharacterSelectPanel.OnStartButtonClicked, which is the same two calls this method used to make
    /// directly, in the same order and for the same reason (see that method's doc comment).
    /// </summary>
    public void playButton()
    {
        SetMenuButtonsActive(false);

        selectPanel.Show(campaign, roster);
    }

    private void Start()
    {
        if (selectPanel != null) { selectPanel.BackClicked += OnSelectPanelBackClicked; }

        if (tutorialToggle == null) { return; }

        // SetIsOnWithoutNotify, not isOn: assigning isOn fires onValueChanged, which would write the
        // saved value straight back over itself - harmless today, but it makes the read look like a
        // write and would matter the moment anything else listens.
        tutorialToggle.SetIsOnWithoutNotify(GameSettings.TutorialEnabled);

        tutorialToggle.onValueChanged.AddListener(OnTutorialToggled);
    }

    /// The select screen has already hidden itself before raising this - see
    /// CharacterSelectPanel.OnBackButtonClicked - so all that is left here is restoring the buttons it
    /// covered.
    private void OnSelectPanelBackClicked()
    {
        SetMenuButtonsActive(true);
    }

    private void SetMenuButtonsActive(bool active)
    {
        foreach (GameObject button in menuButtons)
        {
            if (button != null) { button.SetActive(active); }
        }
    }

    private static void OnTutorialToggled(bool enabled) => GameSettings.TutorialEnabled = enabled;
}
