using TMPro;
using UnityEngine;

/// <summary>
/// The small "Press E to rotate" prompt shown near the HUD while a rotatable card is armed - see
/// Card.CanRotateAim. Built into the scene by Assets/Editor/AimHintWiring.cs (Tools/Board/Wire Aim
/// Hint), not hand-placed, for the same reason every other scene addition in this project goes through
/// a wiring script: the Editor holds Game.unity in memory while it is open, so a hand edit to the
/// .unity file would be silently discarded.
///
/// Driven from CardPlayManager.Select/Deselect, which are the single door a card selection opens and
/// closes through - nothing else needs to know this label exists.
/// </summary>
public class AimHintLabel : Singleton<AimHintLabel>
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text label;

    private void Reset()
    {
        root = gameObject;
        label = GetComponentInChildren<TMP_Text>();
    }

    public void Show(string text)
    {
        if (label != null) { label.text = text; }
        if (root != null) { root.SetActive(true); }
    }

    public void Hide()
    {
        if (root != null) { root.SetActive(false); }
    }
}
