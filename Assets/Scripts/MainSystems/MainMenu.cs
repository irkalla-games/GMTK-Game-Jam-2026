using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
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

    [Tooltip("Runs the tutorial before the run proper when ticked. Remembered between sessions - the "
             + "state authored here is only what the button looks like before GameSettings is read.")]
    [SerializeField] private Toggle tutorialToggle;

    [Tooltip("The tutorial prologue: one scripted level with its own fixed party and decks. Played "
             + "instead of the character-select screen while the toggle is on, and followed "
             + "automatically by a normal run built from Campaign's levels and the party below.\n\n"
             + "Leave empty to disable the tutorial path entirely - Play then always goes to character "
             + "select, whatever the toggle says.")]
    [SerializeField] private RunData tutorialRun;

    [Tooltip("Who the player is handed once the tutorial is cleared, and on which decks. The tutorial "
             + "teaches Fireball, Slash, Teleport and Sap Totem, so this wants to be the Knight and Mage "
             + "on the starter decks that hold them - otherwise it has taught cards the player does not "
             + "have.")]
    [SerializeField] private List<PartyEntry> tutorialFollowOnParty = new();

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
        if (TryStartTutorial()) { return; }

        SetMenuButtonsActive(false);

        selectPanel.Show(campaign, roster);
    }

    /// <summary>
    /// Sends a first-time player through the tutorial prologue instead of the select screen, and queues
    /// the real run behind it so clearing the tutorial hands them a party rather than the menu.
    ///
    /// The party is not chosen here on purpose: the tutorial scripts specific cards in specific hands,
    /// so it cannot run with whoever happened to be picked. The follow-on run is built with the same
    /// RunData.CreateRuntime factory CharacterSelectPanel uses, from this same campaign's levels - so
    /// there is one answer to "what levels does a run play", and it is Campaign.
    ///
    /// StartRun before the load, matching CharacterSelectPanel.OnStartButtonClicked - it resets any
    /// previous run that ended in defeat before BattleManager could resume from it.
    /// </summary>
    private bool TryStartTutorial()
    {
        if (!GameSettings.TutorialEnabled || tutorialRun == null) { return false; }

        if (campaign == null)
        {
            Debug.LogError($"{name}: no campaign RunData - there is nowhere for the tutorial to hand "
                           + "over to. Falling through to character select.");
            return false;
        }

        RunData followOn = RunData.CreateRuntime(
            campaign.Levels, tutorialFollowOnParty, campaign.CarryDamageBetweenLevels);

        RunManager.StartRun(tutorialRun, showTutorial: true, followOn: followOn);

        SceneManager.LoadScene("Game");

        return true;
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
