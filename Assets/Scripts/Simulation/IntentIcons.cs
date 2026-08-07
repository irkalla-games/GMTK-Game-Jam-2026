using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What each IntentKind looks like. The single place art is attached to a committed intent.
///
/// An asset rather than a field on Intent, mirroring StatusIcons - this is authoring data about the
/// *kind*, not about any one view of it. Intent is a struct built fresh every time a brain decides, so
/// giving it a Sprite field would be handing per-decision state a job that belongs to the type.
///
/// A missing entry answers null, and CharacterOverheadViewer hides the icon. That is on purpose - a
/// kind with no art yet should cost you the icon, not the whole widget.
/// </summary>
[CreateAssetMenu(menuName = "Enemies/Intent Icons")]
public class IntentIcons : ScriptableObject
{
    [System.Serializable]
    private struct Entry
    {
        public IntentKind kind;

        public Sprite icon;
    }

    [Tooltip("One row per kind that has art. Wait is deliberately left unauthored - no icon means no "
        + "promise. Order here does not matter.")]
    [SerializeField] private List<Entry> entries = new();

    /// The sprite for this intent kind, or null if none has been authored yet.
    public Sprite For(IntentKind kind)
    {
        foreach (Entry entry in entries)
        {
            if (entry.kind == kind) { return entry.icon; }
        }

        return null;
    }
}
