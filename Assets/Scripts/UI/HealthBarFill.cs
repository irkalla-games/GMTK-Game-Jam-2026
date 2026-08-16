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
    /// renders a text readout does not walk the status list a second time. No damage preview - the
    /// three plain consumers (SelectedCharacterPanel, HeroPortrait, PartySheetColumn) call this one.
    /// </summary>
    public static int Apply(Image healthFill, Image shieldFill, Character character) =>
        Apply(healthFill, shieldFill, null, character, previewLoss: 0);

    /// <summary>
    /// The same bar, plus a projected loss: `healthFill` stops `previewLoss` short of where it
    /// otherwise would, and `previewFill` - a DamagePreviewFill layered behind it - fills the gap that
    /// opens up, in yellow or a lethal colour if the loss would drop the character to zero.
    /// CharacterOverheadViewer is the only caller that passes a non-null previewFill.
    ///
    /// previewFill's own fillAmount is set to Health/Max, not to the loss - because healthFill (on top)
    /// already stops at (Health - loss)/Max, so whatever previewFill shows through underneath it *is*
    /// the loss, with no separate "how wide is the gap" arithmetic to keep in sync.
    /// </summary>
    public static int Apply(Image healthFill, Image shieldFill, DamagePreviewFill previewFill,
                             Character character, int previewLoss)
    {
        if (character == null) { return 0; }

        // Shield is not a field on Character - it is whatever a ShieldStatus in its list says it is,
        // auras included.
        int shield = character.StatusStacks(StatusType.Shield);

        // A character authored with 0 max health would otherwise divide by zero and blank the bar.
        float max = Mathf.Max(1, character.MaxHealth);

        int afterPreview = Mathf.Max(0, character.Health - Mathf.Max(0, previewLoss));

        if (healthFill != null) { healthFill.fillAmount = Mathf.Clamp01(afterPreview / max); }

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

        if (previewFill != null)
        {
            if (previewLoss > 0)
            {
                previewFill.Show(character.Health / max, lethal: previewLoss >= character.Health);
            }
            else
            {
                previewFill.Hide();
            }
        }

        return shield;
    }
}
