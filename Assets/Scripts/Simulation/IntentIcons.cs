using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The art an intent readout draws with - not just what each IntentKind looks like, but the ranged
/// swap and the sequence connector the readout needs too. The single place any of it is attached to
/// a committed intent.
///
/// An asset rather than a field on Intent, mirroring StatusIcons - this is authoring data about the
/// *kind*, not about any one view of it. Intent is a struct built fresh every time a brain decides, so
/// giving it a Sprite field would be handing per-decision state a job that belongs to the type.
///
/// A missing entry answers null, and the readout hides whatever it was for. That is on purpose - a
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

    [System.Serializable]
    private struct RiderEntry
    {
        public IntentRiderKind kind;

        public Sprite icon;
    }

    [Tooltip("One row per kind that has art. Wait is deliberately left unauthored - no icon means no "
        + "promise. Order here does not matter.")]
    [SerializeField] private List<Entry> entries = new();

    [Tooltip("Shown instead of the Attack icon when the committed card's range is past melee - see "
        + "TargetRange.IsRanged. Optional; falls back to the plain Attack icon when empty, so this can "
        + "be left unauthored with no other change in behaviour.")]
    [SerializeField] private Sprite rangedAttackIcon;

    [Tooltip("Drawn between one step of an enemy's plan and the next, so the row reads as an ordered "
        + "sequence rather than a size difference. Optional; a strip with this unauthored simply draws "
        + "no connector.")]
    [SerializeField] private Sprite sequenceChevron;

    [Tooltip("One row per IntentRiderKind that has art - the handful of named, non-status effects an "
        + "intent readout badges the same way it badges a status (see IntentRider). A status rider "
        + "reads StatusIcons instead, since every StatusType already has art there.")]
    [SerializeField] private List<RiderEntry> riders = new();

    /// The sprite for this intent kind, or null if none has been authored yet.
    public Sprite For(IntentKind kind)
    {
        foreach (Entry entry in entries)
        {
            if (entry.kind == kind) { return entry.icon; }
        }

        return null;
    }

    /// <summary>
    /// The sprite for a whole committed Intent, not just its kind - an Attack whose card reaches past
    /// melee gets rangedAttackIcon instead of the plain Attack entry, so a Ranger's arrow reads as a
    /// bow rather than the same crossed swords a Warrior swings. Falls back to For(kind) whenever the
    /// ranged swap does not apply: any non-Attack kind, an Attack with no card, a melee-range card, or
    /// rangedAttackIcon left unauthored.
    /// </summary>
    public Sprite For(Intent intent)
    {
        if (intent.kind == IntentKind.Attack && intent.card != null && intent.card.range.IsRanged
            && rangedAttackIcon != null)
        {
            return rangedAttackIcon;
        }

        return For(intent.kind);
    }

    /// The sprite for this named rider effect, or null if none has been authored yet. Not consulted
    /// for a status rider - see IntentRider and Card.OutgoingRiders, which key those off StatusIcons.
    public Sprite ForRider(IntentRiderKind kind)
    {
        foreach (RiderEntry entry in riders)
        {
            if (entry.kind == kind) { return entry.icon; }
        }

        return null;
    }

    /// Drawn between two consecutive steps of a plan - see the field's own tooltip for why this lives
    /// here rather than on the strip that draws it.
    public Sprite Chevron => sequenceChevron;
}
