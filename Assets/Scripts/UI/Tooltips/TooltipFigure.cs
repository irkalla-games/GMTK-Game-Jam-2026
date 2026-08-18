/// <summary>
/// Which role one cell of an area-of-effect diagram plays. Ordered so a cell that is more than one
/// thing at once - the caster's own tile caught inside its own blast - resolves to the more specific
/// claim: Caster beats Aim beats Hit beats Empty.
/// </summary>
public enum TooltipCellRole
{
    Empty = 0,
    Hit,
    Aim,
    Caster,
}

/// <summary>
/// A tooltip-ready picture of which tiles a card's area reaches, relative to the caster. Pure
/// geometry - a role per cell, nothing about cards, statuses or keywords - which is what lets
/// TooltipContent stay ignorant of what it is displaying, the same way TooltipEntry already is.
///
/// Built by CardAreaFigure from the same CardAreaIconBuilder walk that draws the card face's own
/// corner glyph, so the two can never disagree about one card. See CardAreaFigure.Build for how the
/// cells are ordered.
/// </summary>
public sealed class TooltipFigure
{
    /// How far the grid reaches from its centre cell in any direction - the grid is (2*reach+1)
    /// cells per side.
    public readonly int reach;

    /// Row-major, row 0 first. Row 0 is the northmost row (largest Y), column 0 is the westmost
    /// column (smallest X) - "north" meaning CardAreaIconBuilder's VirtualAim direction, not a real
    /// compass point.
    public readonly TooltipCellRole[] cells;

    public int Span => reach * 2 + 1;

    public TooltipFigure(int reach, TooltipCellRole[] cells)
    {
        this.reach = reach;
        this.cells = cells;
    }
}
