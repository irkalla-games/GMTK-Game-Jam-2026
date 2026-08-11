using System;
using UnityEngine;

/// <summary>
/// How an area-of-effect footprint is described. Single is deliberately value 0, for the same reason
/// RangeShape.Anywhere is - a CardEffectEntry authored before this field existed deserializes to
/// all-zero, so every effect on every card keeps hitting exactly the one tile it always did.
///
/// These values are written into .asset files as ints - append new kinds, never reorder.
/// </summary>
public enum AreaKind
{
    /// Just the aim tile - today's behaviour, and the only kind Card.Refusal still gates on.
    Single = 0,

    /// A TargetRange's shape (box, diamond, ring, whole board), measured from the aim tile rather than
    /// the caster - the boring-but-common cases without painting a grid.
    Radius = 1,

    /// A hand-painted EffectPattern - a cone, a T, an X, anything a radius can't express.
    Pattern = 2,
}

/// <summary>
/// The footprint one CardEffectEntry covers, measured from wherever that effect is aimed (the played
/// tile, or the caster's own tile for a Source-aimed entry).
///
/// A struct for the same reason TargetRange is one: it can never be null, so an entry authored before
/// this field existed deserializes to AreaKind.Single, and Card holding its own copy means a runtime
/// write can never reach back into the shared CardData asset.
/// </summary>
[Serializable]
public struct AreaShape
{
    [SerializeField] private AreaKind kind;

    [Tooltip("Used when Kind is Radius. Shape and min/max distance, measured from the aim tile.")]
    [SerializeField] private TargetRange radius;

    [Tooltip("Used when Kind is Pattern. The painted footprint to stamp down, rotated to face the aim "
             + "direction.")]
    [SerializeField] private EffectPattern pattern;

    /// A Radius area - box, diamond, ring or whole board, measured from the aim tile. For code that
    /// builds a CardEffectEntry without going through the Inspector - a relic granting a card +1
    /// splash, an Editor script authoring example content.
    public AreaShape(TargetRange radius)
    {
        kind = AreaKind.Radius;
        this.radius = radius;
        pattern = null;
    }

    /// A Pattern area - a painted footprint. See the TargetRange overload above for why this exists.
    public AreaShape(EffectPattern pattern)
    {
        kind = AreaKind.Pattern;
        radius = default;
        this.pattern = pattern;
    }

    public AreaKind Kind => kind;

    public bool IsSingle => kind == AreaKind.Single;

    /// How far this footprint reaches from its aim tile, in tile steps - what an enemy brain reads as
    /// this entry's threat radius and what the card-face icon sizes itself to. 0 for Single.
    public int MaxReach => kind switch
    {
        AreaKind.Radius => radius.MaxDistance,
        AreaKind.Pattern => pattern != null ? pattern.MaxReach() : 0,
        _ => 0,
    };

    /// <summary>
    /// True if `cell` is covered by this footprint, aimed from `caster` toward `aim`. Pure coordinate
    /// math - no GridTile - so EnemyBrain can score a footprint against Board the same way the
    /// player-facing highlight does.
    /// </summary>
    public bool Covers(Vector2Int caster, Vector2Int aim, Vector2Int cell)
    {
        return kind switch
        {
            AreaKind.Radius => radius.Contains(aim, cell),
            AreaKind.Pattern => pattern != null && pattern.Covers(caster, aim, cell),
            _ => cell == aim,
        };
    }
}
