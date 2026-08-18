using System.Collections.Generic;

/// <summary>
/// What one TooltipEntry renders as. Term is 0 on purpose, the same reasoning RangeShape.Anywhere and
/// AreaKind.Single give for their own zero case: a Term is what every entry used to be before this
/// enum existed, so the old two-string shape keeps meaning what it always meant.
///
/// Never serialized - TooltipContent is built fresh on every hover and thrown away, so there is no
/// asset-ordering hazard the way there is for RangeShape or AreaKind.
/// </summary>
public enum TooltipEntryKind
{
    /// A name and its explanation - PARRY and what Parry does. Title may be null (the standing
    /// "Hold Alt for details" hint has no name, just a body).
    Term = 0,

    /// The panel's own title bar - the card's name. At most one of these is meaningful per content;
    /// TooltipManager reads only the first.
    Header,

    /// Opens a new section - "STATUS EFFECTS", "KEYWORDS". Entries added before the first Section
    /// belong to one implicit unlabelled section, which is what lets a status chip's single Term
    /// render as plain chrome with no heading.
    Section,

    /// A label/value row - RANGE / "Up to 4 tiles (square)". Kept apart from Term so the two can be
    /// styled differently: a stat's value is not a glossary explanation.
    Stat,

    /// An area-of-effect diagram. Body and title are unused; the picture itself lives on `figure`.
    Figure,
}

/// <summary>
/// One heading and its explanation - PARRY and what Parry does - or, since the chrome rewrite, one of
/// a small family of other rows a tooltip panel can show. A readonly struct because these are built
/// fresh on every hover and never edited afterwards, the same reasoning as DamageInfo: the churn
/// allocates nothing and nothing can half-write one.
/// </summary>
public readonly struct TooltipEntry
{
    public readonly TooltipEntryKind kind;

    /// Term: the name. Header: the panel title. Section: the section label. Stat: the key
    /// ("RANGE"). Figure: unused.
    public readonly string title;

    /// Term: the explanation. Stat: the value. Header/Section/Figure: unused.
    public readonly string body;

    /// Figure only; null otherwise.
    public readonly TooltipFigure figure;

    public TooltipEntry(TooltipEntryKind kind, string title, string body, TooltipFigure figure)
    {
        this.kind = kind;
        this.title = title;
        this.body = body;
        this.figure = figure;
    }
}

/// <summary>
/// What a tooltip should say, as structure rather than as a finished string.
///
/// A list because one hover can have several things to explain - a card carrying both Innate and
/// Cooldown wants two headed paragraphs, not one run-on sentence. Kept structured so the *view* owns
/// what a title looks like; a caller that pre-baked its own rich text would be the second answer to
/// that question, and the two would drift.
///
/// Deliberately knows nothing about statuses, cards or keywords. That is what lets the enemy intent
/// readout and anything else added later reuse the same popup without touching TooltipManager. The
/// same is true of the Header/Section/Stat/Figure additions below: they describe panel structure, not
/// game data - CardViewer is the one that knows a Stat's key is "RANGE" or that a Figure comes from a
/// card's area.
/// </summary>
public class TooltipContent
{
    private readonly List<TooltipEntry> entries = new();

    public IReadOnlyList<TooltipEntry> Entries => entries;

    /// A caller with nothing to say builds an empty content and hands it over regardless - the manager
    /// shows no box for it. That keeps "does this card have any keywords?" out of every caller.
    public bool IsEmpty => entries.Count == 0;

    /// Returns itself so a caller can chain, since most build one or two entries inline.
    public TooltipContent Add(string title, string body)
    {
        // A heading with no explanation under it is a glossary entry somebody has not finished
        // authoring yet. Skipped rather than shown, for the same reason StatusIcons answers null.
        if (string.IsNullOrWhiteSpace(body)) { return this; }

        entries.Add(new TooltipEntry(TooltipEntryKind.Term, title, body, null));

        return this;
    }

    /// The panel's title bar. Skipped, not shown blank, if `title` is blank - the same "nothing to
    /// author" reasoning Add's body guard already carries.
    public TooltipContent Header(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) { return this; }

        entries.Add(new TooltipEntry(TooltipEntryKind.Header, title, null, null));

        return this;
    }

    /// Opens a new section under `label`. Every entry added after this and before the next Section (or
    /// the end of the content) belongs to it.
    public TooltipContent Section(string label)
    {
        entries.Add(new TooltipEntry(TooltipEntryKind.Section, label, null, null));

        return this;
    }

    /// A label/value row inside whichever section is currently open. Blank values are dropped, same
    /// reasoning as Add - CardRulesText never actually returns one, but a caller that changes should
    /// not have to remember the guard itself.
    public TooltipContent Stat(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) { return this; }

        entries.Add(new TooltipEntry(TooltipEntryKind.Stat, key, value, null));

        return this;
    }

    /// An area-of-effect diagram inside whichever section is currently open. A null figure - a
    /// single-target card, say - is a no-op rather than an empty picture.
    public TooltipContent Figure(TooltipFigure figure)
    {
        if (figure == null) { return this; }

        entries.Add(new TooltipEntry(TooltipEntryKind.Figure, null, null, figure));

        return this;
    }
}
