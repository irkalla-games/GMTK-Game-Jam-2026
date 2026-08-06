using System.Collections.Generic;

/// <summary>
/// One heading and its explanation - PARRY and what Parry does.
///
/// A readonly struct because these are built fresh on every hover and never edited afterwards, the
/// same reasoning as DamageInfo: the churn allocates nothing and nothing can half-write one.
/// </summary>
public readonly struct TooltipEntry
{
    public readonly string title;

    public readonly string body;

    public TooltipEntry(string title, string body)
    {
        this.title = title;
        this.body = body;
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
/// readout and anything else added later reuse the same popup without touching TooltipManager.
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

        entries.Add(new TooltipEntry(title, body));

        return this;
    }
}
