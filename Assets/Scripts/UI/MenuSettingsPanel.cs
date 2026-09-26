using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The main menu's Settings screen: display, audio, and the three game options that used to sit as
/// loose toggles on the menu itself.
///
/// A plain MonoBehaviour rather than a Singleton, unlike most panels here - MainMenu holds the only
/// reference and nothing else needs to reach it. The in-game twin (PauseSettingsPanel) is a separate
/// component with its own layout; what the two share is SettingsBinding, so they can differ in shape
/// without ever differing about what a setting means.
///
/// Every widget reference is assigned by Tools/Main Menu/Wire Settings Panel. Nothing is found by name
/// at runtime, so renaming an object in the scene breaks the wiring visibly in the Inspector rather
/// than silently at play time.
/// </summary>
public class MenuSettingsPanel : MonoBehaviour
{
    [SerializeField] private GameObject root;

    [SerializeField] private Button backButton;

    [Header("Display")]
    [SerializeField] private Toggle fullscreenToggle;

    [SerializeField] private TMP_Dropdown resolutionDropdown;

    [Header("Audio")]
    [SerializeField] private Slider masterSlider;

    [SerializeField] private Slider musicSlider;

    [SerializeField] private Slider sfxSlider;

    [Tooltip("Short blip played when the Sound Effects slider moves, so that slider has something to "
             + "demonstrate. Without it the control is unverifiable by ear until something else in the "
             + "game plays an effect.")]
    [SerializeField] private AudioClip sfxPreview;

    [Header("Game")]
    [SerializeField] private Toggle tutorialToggle;

    [SerializeField] private Toggle intentDamageToggle;

    [SerializeField] private Toggle debugRunToggle;

    [Tooltip("The whole Debug Run row, hidden in a Playable build (BuildMode.DebugTools). Its toggle "
             + "would do nothing there - GameSettings.DebugRunEnabled ignores the saved value - and a "
             + "setting that does nothing should not be on screen.")]
    [SerializeField] private GameObject debugRunRow;

    /// When the last SFX preview played, on unscaled time. A slider drag raises onValueChanged
    /// every frame it moves, and firing a blip on each one is a machine-gun rather than a preview.
    private float lastSfxPreview;

    /// Shortest gap between two preview blips, in unscaled seconds.
    private const float SfxPreviewInterval = 0.12f;

    public bool IsOpen { get; private set; }

    /// Raised on close so MainMenu can bring its own buttons back, the same one-way shape
    /// CharacterSelectPanel.BackClicked already uses - this panel does not know what covered what.
    public event System.Action Closed;

    /// Starts hidden regardless of how the scene was authored, matching CardPilePanel.Awake's contract:
    /// a panel left visible in the scene view must not be visible on load.
    private void Awake()
    {
        if (root != null) { root.SetActive(false); }

        if (backButton != null) { backButton.onClick.AddListener(Close); }

        // Only ever hidden, never forced on - a Debug build leaves the row however the scene authored it.
        if (!BuildMode.DebugTools && debugRunRow != null) { debugRunRow.SetActive(false); }
    }

    private void Update()
    {
        if (!IsOpen || Keyboard.current == null) { return; }

        // Safe to take Escape here: this scene has no pause menu to compete with, and the main menu
        // binds Escape to nothing else.
        if (Keyboard.current.escapeKey.wasPressedThisFrame) { Close(); }
    }

    public void Show()
    {
        if (IsOpen) { return; }

        IsOpen = true;

        if (root != null) { root.SetActive(true); }

        Bind();
    }

    public void Close()
    {
        if (!IsOpen) { return; }

        IsOpen = false;

        if (root != null) { root.SetActive(false); }

        Closed?.Invoke();
    }

    /// <summary>
    /// Rebuilds every binding from the saved values, on each open.
    ///
    /// Bound on Show rather than once in Awake because the resolution list is built from
    /// Screen.resolutions, which changes when a monitor is plugged in or the game moves to another
    /// display - a list captured at startup would go stale without anything saying so.
    /// </summary>
    private void Bind()
    {
        SettingsBinding.BindToggle(fullscreenToggle,
            static () => GameSettings.Fullscreen,
            static value => GameSettings.Fullscreen = value,
            DisplaySettings.Apply);

        SettingsBinding.BindDropdown(resolutionDropdown,
            DisplaySettings.ResolutionLabels(),
            DisplaySettings.CurrentIndex,
            static value => GameSettings.ResolutionIndex = value,
            DisplaySettings.Apply);

        SettingsBinding.BindSlider(masterSlider,
            static () => GameSettings.MasterVolume,
            static value => GameSettings.MasterVolume = value,
            ApplyVolumes);

        SettingsBinding.BindSlider(musicSlider,
            static () => GameSettings.MusicVolume,
            static value => GameSettings.MusicVolume = value,
            ApplyVolumes);

        SettingsBinding.BindSlider(sfxSlider,
            static () => GameSettings.SfxVolume,
            static value => GameSettings.SfxVolume = value,
            PreviewSfx);

        SettingsBinding.BindToggle(tutorialToggle,
            static () => GameSettings.TutorialEnabled,
            static value => GameSettings.TutorialEnabled = value);

        SettingsBinding.BindToggle(intentDamageToggle,
            static () => GameSettings.ShowIntentDamage,
            static value => GameSettings.ShowIntentDamage = value);

        SettingsBinding.BindToggle(debugRunToggle,
            static () => GameSettings.DebugRunEnabled,
            static value => GameSettings.DebugRunEnabled = value);
    }

    /// Null-checked because a scene entered directly in the Editor may not have run AudioManager's
    /// bootstrap yet; the saved value still persists, so the next launch is correct either way.
    /// <summary>
    /// Applies the new level, then plays a short blip so the change is audible.
    ///
    /// Throttled on unscaled time because a drag raises onValueChanged on every frame the handle
    /// moves, and PlayOneShot layers - unthrottled, dragging the slider fires dozens of overlapping
    /// copies. Unscaled specifically so this still works from the pause menu, where time is frozen.
    /// </summary>
    private void PreviewSfx()
    {
        ApplyVolumes();

        if (sfxPreview == null || AudioManager.Instance == null) { return; }

        if (Time.unscaledTime - lastSfxPreview < SfxPreviewInterval) { return; }

        lastSfxPreview = Time.unscaledTime;

        AudioManager.Instance.PlaySfx(sfxPreview);
    }

    private static void ApplyVolumes()
    {
        if (AudioManager.Instance != null) { AudioManager.Instance.ApplyVolumes(); }
    }
}
