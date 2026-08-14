using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The one place the health/shield bar's arithmetic lives. CharacterOverheadViewer and
/// SelectedCharacterPanel draw the same bar from the same numbers, and two copies of this would be
/// two answers to one question the moment either grew a rule.
///
/// Both fills grow from the *left*, and shield is capped at current health. That is what makes the
/// dark region on the right mean exactly one thing - missing health - so the player can read how much
/// healing a character can still take. Filling shield from the right, as this used to, laid the blue
/// straight over that gap and made a wounded shielded character read as nearly full.
///
/// Shield beyond current health is deliberately not drawn; a caller that wants to state the true
/// total says so in text, the way SelectedCharacterPanel's "12/20  +9" does.
/// </summary>
public static class HealthBarFill
{
    /// <summary>
    /// Writes both fills for `character`, and returns the shield total it read so a caller that also
    /// renders a text readout does not walk the status list a second time.
    /// </summary>
    public static int Apply(Image healthFill, Image shieldFill, Character character)
    {
        if (character == null) { return 0; }

        // Shield is not a field on Character - it is whatever a ShieldStatus in its list says it is,
        // auras included.
        int shield = character.StatusStacks(StatusType.Shield);

        // A character authored with 0 max health would otherwise divide by zero and blank the bar.
        float max = Mathf.Max(1, character.MaxHealth);

        if (healthFill != null) { healthFill.fillAmount = Mathf.Clamp01(character.Health / max); }

        if (shieldFill != null)
        {
            // Set here rather than authored: the five character prefabs and Game.unity all carry
            // OriginHorizontal.Right, and the Editor holds the scene in memory, so the fix cannot go
            // in the YAML. The setter is equality-guarded, so this costs nothing after the first
            // refresh - and a prefab authored with the wrong origin later corrects itself.
            shieldFill.fillOrigin = (int)Image.OriginHorizontal.Left;

            shieldFill.fillAmount = Mathf.Clamp01(Mathf.Min(shield, character.Health) / max);
            shieldFill.enabled = shield > 0;
        }

        return shield;
    }
}
