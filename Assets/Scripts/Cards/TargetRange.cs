using System;
using UnityEngine;

/// <summary>
/// How the tiles a card may be aimed at are measured out from the acting character's tile.
///
/// Anywhere is deliberately value 0. Adding a serialized field to a CardData asset that predates it
/// deserializes as all-zero, so every card authored before ranges existed keeps the old behaviour of
/// "any tile on the board is legal".
///
/// These values are written into .asset files as ints, so they are load-bearing: append new shapes at
/// the end. Reordering them silently re-aims every card already authored.
/// </summary>
public enum RangeShape
{
    /// Any tile on the board. Distances are ignored.
    Anywhere = 0,

    /// Square. A diagonal step costs the same as an orthogonal one, so distance 1 is the 8 surrounding
    /// tiles - this is what Move wants.
    Chebyshev = 1,

    /// Diamond. Orthogonal steps only, so distance 1 is the 4 tiles sharing an edge.
    Manhattan = 2,

    /// The acting character's own tile and nothing else. Distances are ignored.
    SelfTile = 3,
}

/// <summary>
/// Where a card may be played, measured from the acting character's tile.
///
/// One card, one rule: a tile click has to come out as a single yes or no before any energy is spent,
/// so this lives on the card rather than on its individual effects. A card fans the same tile out to
/// every effect anyway, and CardEffect assets are shared between cards - a range on MoveEffect would
/// silently follow it onto every future card that reused it.
///
/// A struct, for two reasons. It can never be null, so a CardData asset that predates this field
/// deserializes to all-zero - RangeShape.Anywhere, exactly what every card did before. And being a
/// value type, Card holds its own copy, so a runtime write can never reach back into the shared asset.
///
/// Not `readonly`, and neither are the fields: Unity's serializer skips readonly fields and they
/// vanish from the Inspector. Immutability comes from the get-only properties.
/// </summary>
[Serializable]
public struct TargetRange
{
    [SerializeField] private RangeShape shape;

    [Tooltip("Nearest tile that may be clicked. 1 excludes the caster's own tile, 0 includes it.")]
    [SerializeField] private int minDistance;

    [Tooltip("Furthest tile that may be clicked. Ignored by Anywhere and SelfTile.")]
    [SerializeField] private int maxDistance;

    public RangeShape Shape => shape;

    public TargetRange(RangeShape shape, int minDistance, int maxDistance)
    {
        this.shape = shape;
        this.minDistance = minDistance;
        this.maxDistance = maxDistance;
    }

    /// <summary>
    /// True if `target` is a legal tile to play at while the actor stands on `origin`.
    ///
    /// Pure integer math on coordinates - answering this never needs the board, only the two tiles the
    /// caller already holds. A null origin (a character that has not been placed yet) fails every
    /// shape except Anywhere, so the play is refused rather than silently allowed.
    /// </summary>
    public bool Contains(GridTile origin, GridTile target)
    {
        if (target == null) { return false; }

        if (shape == RangeShape.Anywhere) { return true; }

        if (origin == null) { return false; }

        Vector2Int step = target.Coordinates - origin.Coordinates;
        int dx = Mathf.Abs(step.x);
        int dy = Mathf.Abs(step.y);

        if (shape == RangeShape.SelfTile) { return dx == 0 && dy == 0; }

        // Chebyshev takes the larger leg, so a diagonal costs the same as a straight step and range 1
        // is all 8 neighbours. Manhattan adds them, so a diagonal costs 2 and range 1 is only 4 tiles.
        int distance = shape == RangeShape.Chebyshev ? Mathf.Max(dx, dy) : dx + dy;

        return distance >= minDistance && distance <= maxDistance;
    }

    /// For the refusal log line.
    public override string ToString() =>
        shape == RangeShape.Anywhere || shape == RangeShape.SelfTile
            ? shape.ToString()
            : $"{shape} {minDistance}-{maxDistance}";
}
