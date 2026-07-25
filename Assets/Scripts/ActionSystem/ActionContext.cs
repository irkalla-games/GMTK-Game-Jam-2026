using System;
using System.Collections.Generic;

/// <summary>
/// Per-action runtime data. Each action in a card gets its own context, so one card can point its
/// actions at different things - Bash damages the tile you picked but draws for whoever played it.
///
/// This is also what lets actions stay stateless: an action's own fields are readonly authoring
/// numbers handed in by the CardData subclass that built it, and everything that varies from one play
/// to the next lives here instead.
/// </summary>
public class ActionContext
{
    public readonly Card card;
    /// The character resolving this action.
    public readonly Character source;

    /// The tiles this action lands on. Empty for actions that don't touch the board.
    public readonly IReadOnlyList<GridTile> targets;

    public ActionContext(Card card, Character source, IReadOnlyList<GridTile> targets)
    {
        this.card = card;
        this.source = source;
        this.targets = targets ?? Array.Empty<GridTile>();
    }

    /// Convenience for the common single-tile case.
    public ActionContext(Card card, Character source, GridTile target)
        : this(card, source, target != null ? new[] { target } : Array.Empty<GridTile>()) { }

    public ActionContext(Card card, Character source) : this(card, source, Array.Empty<GridTile>()) { }
}
