using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns a card's area entries into a TooltipFigure - the diagram CardViewer shows under the AREA line
/// when Alt is held. Domain glue only: all the geometry comes from CardAreaIconBuilder.Footprint, which
/// already draws the same footprint into the card face's own corner glyph. One area walk, two
/// renderers, so a tooltip and a card face can never disagree about one card's shape.
///
/// The sibling of CardRulesText - that one puts a card's mechanics into words, this one into a picture,
/// and both read a Card without touching the board.
/// </summary>
public static class CardAreaFigure
{
    /// How far a diagram is allowed to grow before it stops following a card's real reach and shows
    /// the centred window instead. A radius-4 card would otherwise draw an 11x11 grid, wider than the
    /// panel; the AREA stat line above the diagram still states the true number in words.
    private const int MaxReach = 3;

    /// Null for a card that spreads to no extra tiles - CardViewer skips the row entirely rather than
    /// draw a diagram that would only repeat what "Single target" on the AREA line already said.
    public static TooltipFigure Build(Card card)
    {
        if (card == null) { return null; }

        // SelfTile's aim tile is the caster's own, same reasoning as Card.AreaIcon - drawing the
        // caster a cell away from a card like Block would show a target that was never really there.
        Vector2Int aim = card.range.Shape == RangeShape.SelfTile
            ? CardAreaIconBuilder.VirtualCaster
            : CardAreaIconBuilder.VirtualAim;

        HashSet<Vector2Int> covered = new();
        HashSet<Vector2Int> anchors = new();

        int reach = CardAreaIconBuilder.Footprint(card.EffectEntries, aim, covered, anchors);

        if (covered.Count == 0) { return null; }

        reach = Mathf.Min(reach, MaxReach);

        // Anywhere has no fixed caster-to-aim relationship - drawing a caster square one tile from the
        // aim would assert a distance that does not exist for this card.
        bool showCaster = card.range.Shape != RangeShape.Anywhere;

        int span = reach * 2 + 1;
        TooltipCellRole[] cells = new TooltipCellRole[span * span];

        for (int row = 0; row < span; row++)
        {
            int y = reach - row;

            for (int col = 0; col < span; col++)
            {
                int x = col - reach;
                Vector2Int cell = new(x, y);

                TooltipCellRole role;

                if (showCaster && cell == CardAreaIconBuilder.VirtualCaster) { role = TooltipCellRole.Caster; }
                else if (anchors.Contains(cell)) { role = TooltipCellRole.Aim; }
                else if (covered.Contains(cell)) { role = TooltipCellRole.Hit; }
                else { role = TooltipCellRole.Empty; }

                cells[row * span + col] = role;
            }
        }

        return new TooltipFigure(reach, cells);
    }
}
