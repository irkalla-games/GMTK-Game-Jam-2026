using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One thing a piece of equipment does, in one of two shapes - the same split CardModifier's write
/// paths mirror on the Card side:
///
///   Project  a permanent combat rule, pulled fresh every Character.ActiveStatuses call exactly like a
///            Totem's aura - see Character.ActiveStatuses. Never merged, never pruned, because there is
///            nothing carried to merge or prune: equipment simply keeps being asked.
///   Apply    a rewrite of a specific Card's own copy - see CardTuningModifier, which is where the
///            CardModifier vocabulary (cost, range, area, magnitude, entries, keywords) plugs in.
///
/// Both are no-ops by default so a concrete modifier only overrides the half it needs - a stat modifier
/// never touches Apply, a card-tuning modifier never touches Project.
/// </summary>
public abstract class EquipmentModifier : ScriptableObject
{
    /// Appends whatever permanent Status this modifier contributes to `carrier`'s active list. Called
    /// from Character.ActiveStatuses once per equipped copy, every time - see that method for why a
    /// fresh build every call is what makes an un-depletable, un-prunable buff correct rather than a
    /// bug.
    public virtual void Project(Character carrier, List<Status> into) { }

    /// Rewrites `card`'s own copy - never `data`, the shared asset. `data` is handed alongside so a
    /// filter (CardTuningModifier's CardFilter) can decide whether this card is even a match before
    /// touching it.
    public virtual void Apply(Card card, CardData data) { }

    /// One tooltip line - what an equipment chip's expanded tooltip shows for this modifier.
    public abstract string Describe();
}
