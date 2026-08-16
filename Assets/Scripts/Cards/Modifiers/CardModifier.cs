using UnityEngine;

/// <summary>
/// One well-defined change to a Card's own copy of its cost, range, area, effect magnitude, entries or
/// keywords - the shared vocabulary an equipment's CardTuningModifier and an upgraded card variant both
/// draw from (see CardData.upgradedForm and CardTuningModifier).
///
/// A CardModifier never touches CardData. Everything it writes lives on the per-copy Card, through the
/// same handful of write paths ResolveEffects and the highlight/preview machinery already read from -
/// see Card.SetEntry/AddEntry/RemoveEntriesWhere/AddKeyword/RemoveKeyword. That is what makes a modifier
/// safe to apply more than once in a run: Card.ResetToAuthored puts a copy back to its authored state,
/// and re-applying every equipped modifier on top is how re-equipping mid-battle stays correct without
/// this class needing to know it is being re-run.
/// </summary>
public abstract class CardModifier : ScriptableObject
{
    public abstract void Apply(Card card);

    /// One tooltip line - what an equipment chip or an upgrade preview shows for this change.
    public abstract string Describe();
}
