using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Settings screen reached from the pause menu.
///
/// A separate component from MenuSettingsPanel rather than one shared panel used in both scenes. The
/// two differ in more than layout: this one has no Debug Run row (that setting only decides what the
/// menu's Play button does, so changing it mid-battle means nothing), and it has to grey out Tutorial,
/// which RunManager snapshots at StartRun and therefore cannot change under a run in progress.
///
/// What they must NOT differ in is what a setting means, so every read and write goes through
/// SettingsBinding and every row is built by SettingsRowBuilder - separate layouts, one definition.
///
/// No Escape handling here: PauseMenu.Update owns Escape for both levels, so one press closes this and
/// a second closes the pause menu rather than one press skipping straight to the board.
/// </summary>
public class PauseSettingsPanel : MonoBehaviour
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

    [Header("Game")]
    [SerializeField] private Toggle tutorialToggle;

    [SerializeField] private TMP_Text tutorialLabel;

    [SerializeField] private Toggle intentDamageToggle;

    [SerializeField] private AudioClip sfxPreview;

    /// See MenuSettingsPanel - a drag raises onValueChanged every frame the handle moves, and PlayOneShot
    /// layers, so the preview blip is throttled.
    private float lastSfxPreview;

    private const float SfxPreviewInterval = 0.12f;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (root != null) { root.SetActive(false); }

        if (backButton != null) { backButton.onClick.AddListener(Close); }
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
    }

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
            PreviewSfx);

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

        // Greyed rather than hidden while a run is live: RunManager snapshots TutorialEnabled at
        // StartRun, so toggling it here would appear to do nothing at all. A greyed row says "not now";
        // a missing one says "no such setting", and only one of those is true.
        SettingsBinding.SetRowInteractable(tutorialToggle, tutorialLabel, RunManager.Instance == null);
    }

    private static void ApplyVolumes()
    {
        if (AudioManager.Instance != null) { AudioManager.Instance.ApplyVolumes(); }
    }

    /// <summary>
    /// Applies the new level and blips, so the slider is audible from here too.
    ///
    /// Unscaled time specifically: the pause menu freezes the game, and a throttle on scaled time would
    /// never advance, so the very first blip would be the only one.
    /// </summary>
    private void PreviewSfx()
    {
        ApplyVolumes();

        if (sfxPreview == null || AudioManager.Instance == null) { return; }

        if (Time.unscaledTime - lastSfxPreview < SfxPreviewInterval) { return; }

        lastSfxPreview = Time.unscaledTime;

        AudioManager.Instance.PlaySfx(sfxPreview);
    }
}
