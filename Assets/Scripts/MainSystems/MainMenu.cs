using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    [Tooltip("Where Play's run reads its levels and carry-damage rule from. The party no longer comes "
             + "from here - see CharacterSelectPanel, which reads this same asset's Levels and "
             + "CarryDamageBetweenLevels but builds the party from what was chosen on screen.")]
    [SerializeField] private RunData campaign;

    [Tooltip("Who Play may offer at the character-select screen, and how large a party it allows.")]
    [SerializeField] private CharacterRoster roster;

    [Tooltip("Play/Settings/Exit and the title - hidden individually while the "
             + "select screen is up, restored on Back. A list of the existing objects rather than a "
             + "shared container so no scene reparenting was needed to introduce this screen - the "
             + "select screen has no idea any of this exists, it only fires BackClicked.")]
    [SerializeField] private List<GameObject> menuButtons = new();

    [SerializeField] private CharacterSelectPanel selectPanel;

    [Tooltip("The Settings screen, opened by the Settings button. Holds display, audio and the three "
             + "game options that used to sit as loose toggles on the menu itself.")]
    [SerializeField] private MenuSettingsPanel settingsPanel;

    [Tooltip("Looping track for the menu, started on Start and handed to AudioManager - which survives "
             + "the scene load, so it keeps playing rather than restarting.")]
    [SerializeField] private AudioClip menuMusic;


    [Tooltip("The debug testbed: one 8x8 board with the whole party, every class's totems in its Test "
             + "deck and a hand of 7. Played instead of both the tutorial and character select while "
             + "the toggle above is on.\n\n"
             + "Leave empty to disable the debug path entirely - Play then behaves as it always did, "
             + "whatever the toggle says. Same escape hatch TutorialRun has.")]
    [SerializeField] private RunData debugRun;

    [Tooltip("The tutorial prologue: one scripted level with its own fixed party and decks. Played "
             + "instead of the character-select screen while the toggle is on, and followed "
             + "automatically by a normal run built from Campaign's levels and the party below.\n\n"
             + "Leave empty to disable the tutorial path entirely - Play then always goes to character "
             + "select, whatever the toggle says.")]
    [SerializeField] private RunData tutorialRun;

    [Tooltip("Who the player is handed once the tutorial is cleared, and on which decks. The tutorial "
             + "teaches Smite, Shield, Slash, Move and Heal Totem, so this wants to be the Knight and "
             + "Cleric on the starter decks that hold them - otherwise it has taught cards the player "
             + "does not have.")]
    [SerializeField] private List<PartyEntry> tutorialFollowOnParty = new();

    public void exitButton(){
        Application.Quit();
        Debug.Log("Game Closed");
    }

    /// <summary>
    /// Opens the Settings screen, hiding the menu buttons behind it exactly as Play hides them for
    /// character select.
    ///
    /// The panel raises Closed when the player backs out; this restores the buttons then, rather than
    /// the panel reaching back to do it - same one-way shape as CharacterSelectPanel.BackClicked.
    /// </summary>
    public void settingsButton()
    {
        if (settingsPanel == null)
        {
            Debug.LogWarning($"{name}: no settings panel assigned - run Tools/Main Menu/Wire Settings Panel.");
            return;
        }

        SetMenuButtonsActive(false);

        settingsPanel.Show();
    }

    /// <summary>
    /// Opens the character-select screen instead of starting a run outright - picking a party size,
    /// heroes and starting decks now happens there. StartRun and the scene load themselves moved to
    /// CharacterSelectPanel.OnStartButtonClicked, which is the same two calls this method used to make
    /// directly, in the same order and for the same reason (see that method's doc comment).
    /// </summary>
    public void playButton()
    {
        if (TryStartDebugRun()) { return; }

        if (TryStartTutorial()) { return; }

        SetMenuButtonsActive(false);

        selectPanel.Show(campaign, roster);
    }

    /// <summary>
    /// Sends Play into the debug testbed, skipping both the tutorial and character select.
    ///
    /// Checked before TryStartTutorial rather than after: the tutorial defaults on, so a debug toggle
    /// that lost to it would appear to do nothing until you also turned the tutorial off.
    ///
    /// The party is fixed for the same reason the tutorial's is - the point of this level is that all
    /// four classes are on the board with their own totems, and a party chosen at the select screen
    /// could not guarantee that. Its heroes and decks live on the debugRun asset, authored by
    /// Tools/Debug/Generate Debug Testbed, so what Play starts is a thing you can open and read.
    ///
    /// No follow-on: a testbed hands you back to the menu when it ends rather than starting a campaign.
    /// </summary>
    private bool TryStartDebugRun()
    {
        if (!GameSettings.DebugRunEnabled || debugRun == null) { return false; }

        RunManager.StartRun(debugRun, showTutorial: false);

        SceneManager.LoadScene("Game");

        return true;
    }

    /// <summary>
    /// Sends a first-time player through the tutorial prologue instead of the select screen, and queues
    /// the real run behind it so clearing the tutorial hands them a party rather than the menu.
    ///
    /// The party is not chosen here on purpose: the tutorial scripts specific cards in specific hands,
    /// so it cannot run with whoever happened to be picked. The follow-on run is built with the same
    /// RunData.CreateRuntimeFrom factory CharacterSelectPanel uses, from this same campaign - so
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

        // CreateRuntimeFrom, so the follow-on inherits the campaign's ladder and its award flag along
        // with its levels - a player who learns the game through the tutorial and then clears the real
        // run behind it has cleared the real run, and must earn what that is worth.
        RunData followOn = RunData.CreateRuntimeFrom(campaign, tutorialFollowOnParty);

        RunManager.StartRun(tutorialRun, showTutorial: true, followOn: followOn);

        SceneManager.LoadScene("Game");

        return true;
    }

    private void Start()
    {
        if (selectPanel != null) { selectPanel.BackClicked += OnSelectPanelBackClicked; }

        if (settingsPanel != null) { settingsPanel.Closed += OnSettingsPanelClosed; }

        // Null-checked rather than assumed: AudioManager builds itself before the first scene
        // loads, but a scene entered some other way should not throw over background music.
        if (AudioManager.Instance != null) { AudioManager.Instance.PlayMusic(menuMusic); }
    }

    /// The select screen has already hidden itself before raising this - see
    /// CharacterSelectPanel.OnBackButtonClicked - so all that is left here is restoring the buttons it
    /// covered.
    /// Settings covers the same buttons Play does, and gives them back the same way.
    private void OnSettingsPanelClosed()
    {
        SetMenuButtonsActive(true);
    }

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
}
