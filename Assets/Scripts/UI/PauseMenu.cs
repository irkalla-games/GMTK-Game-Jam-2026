using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The in-battle pause menu: Resume, Settings, Debug, Abandon Run, Quit to Menu.
///
/// Escape opens and closes it, and Escape now means only this - CardPlayManager, CardPilePanel and
/// PartySheetPanel each moved to Backspace so one key press cannot deselect a card AND close a panel
/// AND open the pause menu in the same frame. Both of those still have a second way in (right-click,
/// Tab, their own close buttons), so nothing became unreachable.
///
/// Unlike PartySheetPanel this does NOT refuse to open while InputLocked. Pause has to work over a
/// reward screen or a card-pile panel, because quitting from one is the whole point of having it. The
/// single exception is a notification, which owns the screen with a Continue button and must be
/// acknowledged before anything else happens.
///
/// Freezes time through TimeFreeze rather than writing Time.timeScale, so a notification appearing
/// underneath cannot thaw the game when it is dismissed. It also joins BattleManager.InputLocked as a
/// pulled clause - freezing time alone does not stop a click, because board clicks are OnMouseDown
/// physics raycasts that no uGUI panel intercepts.
/// </summary>
public class PauseMenu : Singleton<PauseMenu>
{
    [SerializeField] private GameObject root;

    [SerializeField] private Button resumeButton;

    [SerializeField] private Button settingsButton;

    [SerializeField] private Button abandonButton;

    [SerializeField] private Button quitButton;

    [Tooltip("A hint that the debug tools exist and which key opens them. Shown only while "
             + "GameSettings.DebugRunEnabled is on - there is no button here, because the panel "
             + "opens on backquote and deliberately does not freeze time the way this menu does.")]
    [SerializeField] private GameObject debugSection;

    [SerializeField] private PauseSettingsPanel settingsPanel;

    [Tooltip("The debug tools. Referenced only so Escape can close them and so closing this menu "
             + "does not leave them orphaned - it is not opened from here.")]
    [SerializeField] private DebugPanel debugPanel;

    public bool IsOpen { get; private set; }

    /// Starts hidden regardless of the scene's authored state - same contract CardPilePanel and
    /// PartySheetPanel keep, so a panel left visible in the scene view is not visible on load.
    protected override void Awake()
    {
        base.Awake();

        if (root != null) { root.SetActive(false); }

        if (resumeButton != null) { resumeButton.onClick.AddListener(Close); }

        if (settingsButton != null) { settingsButton.onClick.AddListener(OpenSettings); }

        if (abandonButton != null) { abandonButton.onClick.AddListener(AbandonRun); }

        if (quitButton != null) { quitButton.onClick.AddListener(QuitToMenu); }

    }
    private void Update()
    {
        if (Keyboard.current == null) { return; }

        if (!Keyboard.current.escapeKey.wasPressedThisFrame) { return; }

        // Escape steps down through whatever is open rather than jumping straight to the board:
        // a sub-panel first, then the menu itself. Debug is checked before Settings only because
        // the two are never open together - the order between them is arbitrary, the fact that
        // both come before the menu is not.
        if (debugPanel != null && debugPanel.IsOpen)
        {
            debugPanel.Close();
            return;
        }

        if (settingsPanel != null && settingsPanel.IsOpen)
        {
            settingsPanel.Close();
            return;
        }

        if (IsOpen) { Close(); } else { Show(); }
    }

    public void Show()
    {
        if (IsOpen) { return; }

        // A notification owns the screen until its Continue button is pressed; opening over it would
        // put two modals up with no defined order between them.
        if (NotificationManager.Instance != null && NotificationManager.Instance.IsShowing) { return; }

        IsOpen = true;

        if (root != null) { root.SetActive(true); }

        if (debugSection != null) { debugSection.SetActive(GameSettings.DebugRunEnabled); }

        TimeFreeze.Acquire();
    }

    public void Close()
    {
        if (!IsOpen) { return; }

        if (settingsPanel != null && settingsPanel.IsOpen) { settingsPanel.Close(); }

        if (debugPanel != null && debugPanel.IsOpen) { debugPanel.Close(); }

        IsOpen = false;

        if (root != null) { root.SetActive(false); }

        TimeFreeze.Release();
    }

    /// <summary>
    private void OpenSettings()
    {
        if (settingsPanel != null) { settingsPanel.Show(); }
    }

    /// <summary>
    /// Ends the run outright and returns to the menu, discarding the party and its progress.
    ///
    /// Distinct from QuitToMenu, which leaves the run intact. Nothing resumes a run today - there is no
    /// save - so the difference is currently one of intent rather than outcome, but EndRun is what
    /// clears RunManager's party and level index, and leaving those set means the next StartRun is
    /// overwriting live state rather than starting clean.
    /// </summary>
    private void AbandonRun()
    {
        if (RunManager.Instance != null) { RunManager.Instance.EndRun(); }

        LeaveToMenu();
    }

    private void QuitToMenu()
    {
        LeaveToMenu();
    }

    /// <summary>
    /// The one exit path. ReleaseAll before the load is load-bearing: TimeFreeze's count is a static
    /// that survives a scene change while this menu does not, so a claim left behind would freeze the
    /// Main Menu with nothing alive to release it.
    /// </summary>
    private void LeaveToMenu()
    {
        IsOpen = false;

        TimeFreeze.ReleaseAll();

        SceneManager.LoadScene("MainMenu");
    }
}
