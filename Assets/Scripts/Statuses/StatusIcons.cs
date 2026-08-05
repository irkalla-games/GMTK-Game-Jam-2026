using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What each StatusType looks like. The single place art is attached to a status.
///
/// An asset rather than an array on the panel because this is authoring data about the status *type*,
/// not about any one view of it - the same CardData-is-the-type split the cards use. A tooltip, a
/// card description or an enemy intent readout all want this same mapping, and none of them should
/// own it.
///
/// Deliberately not a field on Status: a Status is a plain C# object built at runtime, one per
/// application, and giving each copy a Sprite reference would be handing per-copy state a job that
/// belongs to the type. It is also why this is keyed by the enum rather than by subclass.
///
/// A missing entry answers null, and the panel skips it. That is on purpose - a status with no art
/// yet should cost you the icon, not the whole row.
/// </summary>
[CreateAssetMenu(menuName = "Statuses/Status Icons")]
public class StatusIcons : ScriptableObject
{
    [System.Serializable]
    private struct Entry
    {
        public StatusType type;

        public Sprite icon;
    }

    [Tooltip("One row per status that has art. Order here does not matter - the panel walks the " +
        "StatusType enum, not this list.")]
    [SerializeField] private List<Entry> entries = new();

    /// The sprite for this status, or null if none has been authored yet.
    public Sprite For(StatusType type)
    {
        foreach (Entry entry in entries)
        {
            if (entry.type == type) { return entry.icon; }
        }

        return null;
    }
}
