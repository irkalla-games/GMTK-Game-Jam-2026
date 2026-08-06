using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Every term the game explains to the player, and the words that trigger the explanation.
///
/// An asset rather than strings in code so wording is a designer edit, not a recompile - the same
/// reasoning as CardData.description, and the sibling of StatusIcons. Those two answer different
/// questions on purpose: StatusIcons says what a status *looks* like, this says what it *means*.
///
/// Keyed by the enums rather than by string so a typo is a compile error and a renamed status cannot
/// quietly lose its text. The terms below are the one place strings appear, and they are matched
/// against prose, not used as keys.
///
/// Bodies carry tokens rather than numbers: "{stacks}" and "{amount}" are filled from whatever the
/// character actually has when the tooltip is built, which is what makes one authored sentence read
/// correctly for Parry 2 and Parry 7 alike.
/// </summary>
[CreateAssetMenu(menuName = "UI/Glossary")]
public class Glossary : ScriptableObject
{
    public const string StacksToken = "{stacks}";
    public const string AmountToken = "{amount}";
    public const string MagnitudeToken = "{magnitude}";

    /// Prefixes on the <link> ids Tag writes, so the two enums cannot collide and TooltipLinkText knows
    /// which table to look the rest up in.
    private const string StatusLink = "status:";
    private const string KeywordLink = "keyword:";

    /// <summary>
    /// The parts every glossary entry has. A base class rather than two independent structs because
    /// Tag, the term matching and the fallback numbers are identical for both - only the key differs.
    /// </summary>
    [System.Serializable]
    public class Entry
    {
        [Tooltip("Shouted heading at the top of the box - PARRY.")]
        public string title;

        [Tooltip("The explanation. Use {stacks}, {amount} and {magnitude} where a number belongs; they " +
            "are filled from the live status or keyword when the tooltip is built.")]
        [TextArea(2, 5)]
        public string body;

        [Tooltip("Words that become hoverable when they appear in a card's description. Leave empty to " +
            "use the enum's own name. Add the variants that name does not cover - 'Double Attack' for " +
            "DoubleNextAttack, 'Poisoned' for Poison.")]
        public List<string> terms = new();

        [Tooltip("Numbers shown when there is nothing live to read them off - hovering 'Block' on a " +
            "card held by a character who has no Block yet. Stops a raw {token} ever reaching the screen.")]
        public int defaultStacks = 1;

        public int defaultAmount = 1;
    }

    [System.Serializable]
    public class StatusEntry : Entry
    {
        public StatusType type;
    }

    [System.Serializable]
    public class KeywordEntry : Entry
    {
        public CardKeywordType type;
    }

    [SerializeField] private List<StatusEntry> statuses = new();

    [SerializeField] private List<KeywordEntry> keywords = new();

    [Tooltip("What a hoverable term looks like inside a card's description. This tint is the only thing " +
        "telling the player the word can be hovered at all, so it has to be visibly different from the " +
        "surrounding text.")]
    [SerializeField] private Color termColor = new(1f, 0.83f, 0.48f);

    /// Built on first use from every entry's terms and thrown away by OnValidate, so editing the asset
    /// mid-play re-tags rather than serving a stale table. Null means "not built yet".
    private Regex termPattern;

    /// Lower-cased term -> link id. Matching is case-insensitive, so the key has to be too.
    private Dictionary<string, string> termLinks;

    public Color TermColor => termColor;

    private void OnValidate()
    {
        termPattern = null;
        termLinks = null;
    }

    /// <summary>
    /// What this status means, with its numbers filled in.
    /// </summary>
    /// <param name="stacks">The count the caller is displaying, or a negative number to take whatever
    /// `live` says. The status panel passes its own total here because one chip sums a carried status
    /// and an aura projecting the same type - the words and the badge have to agree.</param>
    /// <param name="live">The character's actual status, if they have one. The only source of the
    /// second number Block carries.</param>
    /// <param name="into">Appends to this content instead of starting a new one, so a caller with
    /// several things to explain - a card carrying two keywords - builds one box rather than fighting
    /// itself for the slot.</param>
    public TooltipContent StatusContent(StatusType type, int stacks = -1, Status live = null,
        TooltipContent into = null)
    {
        StatusEntry entry = FindStatus(type);

        TooltipContent content = into ?? new TooltipContent();

        if (entry == null) { return content; }

        string body = entry.body;

        // The caller's number first, so a live status filling {stacks} afterwards cannot overwrite the
        // total the chip is actually showing.
        if (stacks >= 0) { body = body.Replace(StacksToken, stacks.ToString()); }

        // Then the status itself - Block is the reason this is a virtual on Status rather than a
        // string.Format here, since only it knows it has an amountPerHit to contribute.
        if (live != null) { body = live.Describe(body); }

        return content.Add(Title(entry, type.ToString()), FillDefaults(entry, body));
    }

    /// What this keyword means. `magnitude` is Cooldown's length; Innate ignores it.
    public TooltipContent KeywordContent(CardKeywordType type, int magnitude, TooltipContent into = null)
    {
        KeywordEntry entry = FindKeyword(type);

        TooltipContent content = into ?? new TooltipContent();

        if (entry == null) { return content; }

        string body = entry.body.Replace(MagnitudeToken, magnitude.ToString());

        return content.Add(Title(entry, type.ToString()), FillDefaults(entry, body));
    }

    /// <summary>
    /// The entry behind a link id written by Tag - "status:Poison" or "keyword:Cooldown".
    ///
    /// The id carries the *enum name*, never the matched word, so the sentence can say "Poisoned" and
    /// still resolve to StatusType.Poison.
    /// </summary>
    public TooltipContent ContentForLink(string linkId, Character context)
    {
        if (string.IsNullOrEmpty(linkId)) { return new TooltipContent(); }

        if (linkId.StartsWith(StatusLink) &&
            System.Enum.TryParse(linkId[StatusLink.Length..], out StatusType status))
        {
            // Read the hovering player's own status when they have one, so "Reduces the next 3 hits"
            // matches the Block they are actually carrying rather than a generic number.
            Status live = context != null ? context.FindStatus(status) : null;

            return StatusContent(status, live != null ? live.stacks : -1, live);
        }

        if (linkId.StartsWith(KeywordLink) &&
            System.Enum.TryParse(linkId[KeywordLink.Length..], out CardKeywordType keyword))
        {
            KeywordEntry entry = FindKeyword(keyword);

            return KeywordContent(keyword, entry != null ? entry.defaultStacks : 0);
        }

        return new TooltipContent();
    }

    /// <summary>
    /// Marks up every glossary term in a run of prose so it can be hovered.
    ///
    /// Auto-tagging rather than hand-authoring &lt;link&gt; tags into every CardData.description: card
    /// assets stay plain readable text, a new glossary term lights up on the cards that already mention
    /// it without anyone editing them, and there is no way to author a link pointing at a term that
    /// does not exist.
    ///
    /// The matched text is written back verbatim inside the tag, so "Poisoned" still reads "Poisoned".
    /// </summary>
    public string Tag(string prose)
    {
        if (string.IsNullOrEmpty(prose)) { return prose; }

        BuildTerms();

        if (termPattern == null) { return prose; }

        string hex = ColorUtility.ToHtmlStringRGB(termColor);

        return termPattern.Replace(prose, match =>
        {
            if (!termLinks.TryGetValue(match.Value.ToLowerInvariant(), out string link)) { return match.Value; }

            return $"<link=\"{link}\"><color=#{hex}>{match.Value}</color></link>";
        });
    }

    private void BuildTerms()
    {
        if (termLinks != null) { return; }

        termLinks = new Dictionary<string, string>();

        List<string> all = new();

        foreach (StatusEntry entry in statuses)
        {
            CollectTerms(entry, entry.type.ToString(), StatusLink + entry.type, all);
        }

        foreach (KeywordEntry entry in keywords)
        {
            CollectTerms(entry, entry.type.ToString(), KeywordLink + entry.type, all);
        }

        if (all.Count == 0) { return; }

        // Longest first so a term that contains another wins - "Double Attack" must not be matched as a
        // bare "Attack" if that is ever added. Regex alternation takes the first branch that matches,
        // so the order in the pattern *is* the precedence.
        all.Sort((a, b) => b.Length.CompareTo(a.Length));

        for (int i = 0; i < all.Count; i++) { all[i] = Regex.Escape(all[i]); }

        termPattern = new Regex($@"\b(?:{string.Join("|", all)})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private void CollectTerms(Entry entry, string fallback, string link, List<string> all)
    {
        // Both enums use None as the never-set sentinel. An entry left on it is a half-authored row,
        // and registering "None" as a hoverable word would tag the word "none" in any description.
        if (fallback == nameof(StatusType.None)) { return; }

        bool any = false;

        foreach (string term in entry.terms)
        {
            if (string.IsNullOrWhiteSpace(term)) { continue; }

            Register(term, link, all);
            any = true;
        }

        // An entry that never listed its terms still matches its own name, which covers most statuses
        // outright - "Parry", "Poison", "Frozen" all read the same in a sentence as in the enum.
        if (!any) { Register(fallback, link, all); }
    }

    private void Register(string term, string link, List<string> all)
    {
        string key = term.ToLowerInvariant();

        // First entry to claim a word keeps it. Two entries fighting over one term is an authoring
        // mistake, and silently letting the later one win would make which tooltip you get depend on
        // list order.
        if (termLinks.ContainsKey(key)) { return; }

        termLinks[key] = link;
        all.Add(term);
    }

    private static string Title(Entry entry, string fallback)
    {
        return string.IsNullOrWhiteSpace(entry.title) ? fallback.ToUpperInvariant() : entry.title;
    }

    /// Last pass before the text reaches the screen: anything still holding a token had nothing live to
    /// fill it, so the authored default stands in. A visible "{stacks}" is never acceptable.
    private static string FillDefaults(Entry entry, string body)
    {
        return body
            .Replace(StacksToken, entry.defaultStacks.ToString())
            .Replace(AmountToken, entry.defaultAmount.ToString())
            .Replace(MagnitudeToken, entry.defaultStacks.ToString());
    }

    private StatusEntry FindStatus(StatusType type)
    {
        foreach (StatusEntry entry in statuses)
        {
            if (entry.type == type) { return entry; }
        }

        return null;
    }

    private KeywordEntry FindKeyword(CardKeywordType type)
    {
        foreach (KeywordEntry entry in keywords)
        {
            if (entry.type == type) { return entry; }
        }

        return null;
    }
}
