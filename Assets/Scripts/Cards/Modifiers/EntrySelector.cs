using System;

/// <summary>
/// Which of a card's effect entries a CardModifier touches.
///
/// Deliberately not "-1 means every entry": a struct authored before allEntries existed, or a fresh one
/// dropped into a list and never touched, deserializes to allEntries:false, index:0 - "just the first
/// entry" - which is a specific, safe answer rather than a magic number that silently means "everything".
/// A modifier that really does want to touch every entry has to say so explicitly.
/// </summary>
[Serializable]
public struct EntrySelector
{
    [UnityEngine.Tooltip("On, this ignores index and matchesEffect and touches every entry on the card.")]
    public bool allEntries;

    [UnityEngine.Tooltip("Used when allEntries is off. Which entry, by position in the card's effect "
                          + "entries. Out of range matches nothing rather than throwing, so a modifier "
                          + "authored for a 3-entry card does not explode against a 1-entry one.")]
    public int index;

    [UnityEngine.Tooltip("If set, only entries using this exact CardEffect asset match - ANDed with "
                          + "allEntries/index, so 'every Damage entry' and 'entry 0, but only if it "
                          + "happens to be Damage' are both expressible.")]
    public CardEffect matchesEffect;

    public bool Matches(CardEffectEntry entry, int i)
    {
        if (!allEntries && index != i) { return false; }
        if (matchesEffect != null && entry.effect != matchesEffect) { return false; }
        return true;
    }
}
