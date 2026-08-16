using System;
using System.Collections.Generic;
using UnityEngine;

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

    /// The CardEffectEntry's own amountDelta/amountPercent - see Amount(). Both 0 for any context not
    /// built from Card.ResolveEffects (a Totem reaction, an editor-authored example), which is the
    /// identity transform and matches every card authored before these fields existed.
    private readonly int amountDelta;
    private readonly int amountPercent;

    public ActionContext(Card card, Character source, IReadOnlyList<GridTile> targets, GridTile epicenter,
                          int amountDelta = 0, int amountPercent = 0)
    {
        this.card = card;
        this.source = source;
        this.targets = targets ?? Array.Empty<GridTile>();
        this.epicenter = epicenter;
        this.amountDelta = amountDelta;
        this.amountPercent = amountPercent;
    }

    /// Convenience for the common single-tile case - the target is also the epicenter.
    public ActionContext(Card card, Character source, GridTile target)
        : this(card, source, target != null ? new[] { target } : Array.Empty<GridTile>(), target) { }

    public ActionContext(Card card, Character source) : this(card, source, Array.Empty<GridTile>(), null) { }

    /// <summary>
    /// `authored` adjusted by this action's entry: amountDelta added first, then amountPercent scaled
    /// (0 means +0%, not "no effect via multiplication by zero"). Never negative - a delta authored or
    /// granted more negative than the base would otherwise flip a heal into damage or the reverse.
    ///
    /// What every CardEffect with a magnitude reads instead of its raw serialized field, so an upgraded
    /// card variant or a card-tuning equipment modifier can change what one specific card deals without
    /// touching the shared CardEffect asset - see CardEffectEntry.amountDelta.
    /// </summary>
    public int Amount(int authored) =>
        Mathf.Max(0, Mathf.RoundToInt((authored + amountDelta) * (100 + amountPercent) / 100f));
}
