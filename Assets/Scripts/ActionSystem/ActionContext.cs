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

    /// The tile the effect was actually aimed at - the played tile, or the caster's own tile for a
    /// Source-aimed entry - whether or not it survived into `targets`. An area effect's footprint can
    /// filter the aim tile itself out (Fireball centred on empty ground, say), but the projectile still
    /// has to fly somewhere and the caster still has to face something, so the animation aim point
    /// reads this instead of picking an arbitrary entry out of `targets`.
    public readonly GridTile epicenter;

    public ActionContext(Card card, Character source, IReadOnlyList<GridTile> targets, GridTile epicenter)
    {
        this.card = card;
        this.source = source;
        this.targets = targets ?? Array.Empty<GridTile>();
        this.epicenter = epicenter;
    }

    /// Convenience for the common single-tile case - the target is also the epicenter.
    public ActionContext(Card card, Character source, GridTile target)
        : this(card, source, target != null ? new[] { target } : Array.Empty<GridTile>(), target) { }

    public ActionContext(Card card, Character source) : this(card, source, Array.Empty<GridTile>(), null) { }
}
