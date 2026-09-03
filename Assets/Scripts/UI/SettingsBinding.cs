using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Binds a widget to a GameSettings property, both ways.
///
/// This exists because there are two settings screens - one on the main menu, one inside the pause
/// menu - and they are separate panels on purpose: an in-game screen wants a narrower layout and has to
/// grey out the settings a run has already snapshotted. What they must NOT differ in is what a setting
/// means, so the layouts are separate and every read and write goes through here.
///
/// Every binder writes the initial value with SetIsOnWithoutNotify / SetValueWithoutNotify. Assigning
/// isOn or value directly fires onValueChanged, which writes the saved value straight back over itself -
/// harmless while the setter is a plain store, but it makes a read look like a write and would matter
/// the moment anything else listens. MainMenu.Start already documents this for its three toggles; this
/// is that rule with a name.
/// </summary>
public static class SettingsBinding
{
    /// <summary>
    /// Two-way binds a toggle. onChanged runs after the write, for a setting that something has to be
    /// told about - the volume sliders use it to push the new level at AudioManager.
    /// </summary>
    public static void BindToggle(Toggle toggle, Func<bool> read, Action<bool> write, Action onChanged = null)
    {
        if (toggle == null) { return; }

        toggle.SetIsOnWithoutNotify(read());

        // Removed first so a second Bind on the same widget - a panel rebuilding its rows - does not
        // stack up listeners that each write the same value.
        toggle.onValueChanged.RemoveAllListeners();

        toggle.onValueChanged.AddListener(value =>
        {
            write(value);
            onChanged?.Invoke();
        });
    }

    public static void BindSlider(Slider slider, Func<float> read, Action<float> write, Action onChanged = null)
    {
        if (slider == null) { return; }

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;

        slider.SetValueWithoutNotify(read());

        slider.onValueChanged.RemoveAllListeners();

        slider.onValueChanged.AddListener(value =>
        {
            write(value);
            onChanged?.Invoke();
        });
    }

    /// <summary>
    /// Two-way binds a dropdown, replacing its options with the given labels.
    ///
    /// The selected index is clamped into the option list rather than trusted: the resolution dropdown's
    /// saved index can point past the end of a shorter list after the game moves to another monitor -
    /// see DisplaySettings.CurrentIndex, which is where that rule actually lives.
    /// </summary>
    public static void BindDropdown(TMP_Dropdown dropdown, List<string> labels, Func<int> read, Action<int> write,
        Action onChanged = null)
    {
        if (dropdown == null) { return; }

        dropdown.ClearOptions();
        dropdown.AddOptions(labels);

        int selected = read();

        if (selected < 0 || selected >= labels.Count) { selected = labels.Count - 1; }

        dropdown.SetValueWithoutNotify(selected);
        dropdown.RefreshShownValue();

        dropdown.onValueChanged.RemoveAllListeners();

        dropdown.onValueChanged.AddListener(value =>
        {
            write(value);
            onChanged?.Invoke();
        });
    }

    /// <summary>
    /// Greys a row out and stops it responding, for a setting that exists but cannot be changed right
    /// now - the in-game panel's Tutorial row, which a run has already snapshotted at StartRun.
    ///
    /// Interactable rather than hidden: a missing row reads as a missing feature, while a greyed one
    /// reads as "not here, not now", which is the truth.
    /// </summary>
    public static void SetRowInteractable(Selectable widget, TMP_Text label, bool interactable)
    {
        if (widget != null) { widget.interactable = interactable; }

        if (label != null)
        {
            label.color = interactable ? PanelPalette.Ink : PanelPalette.LabelDisabled;
        }
    }
}
