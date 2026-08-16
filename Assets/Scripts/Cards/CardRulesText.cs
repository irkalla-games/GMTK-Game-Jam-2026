/// <summary>
/// Which colour band a card's audience stripe reads as.
///
/// Not the same list as TargetAudience, on purpose. An effect answers "who may I land on", which is a
/// rules question; the stripe answers "who is this for", which is a reading question - and those two
/// differ in exactly one place. A pure self-buff's effect says Ally, because the caster is their own
/// ally and the refusal has to let them through; the player wants to be told "yourself". Self is that
/// distinction, and Card.AffectsSelfOnly is what draws it.
/// </summary>
public enum CardTargetBand
{
    /// Nobody but the caster - SelfTile range with nothing spreading off it.
    Self,

    /// Your own side, including you.
    Ally,

    /// The other side.
    Enemy,

    /// Either side, but a character - Swap.
    AnyCharacter,

    /// The tile itself rather than whoever stands on it. Summons, movement and the wall effects.
    Ground,
}

/// <summary>
/// Puts a card's mechanical shape into words - what it reaches, who it is for, and how far it
/// spreads.
///
/// Generated rather than authored, so a card's description never has to repeat "within 5 tiles" and
/// can never disagree with the TargetRange it is actually played by. Card copies its range per-copy
/// precisely so a relic can change it, and a hand-typed sentence would go stale the moment one did.
///
/// The sibling of Glossary.SummonRangeLine, which does this same job for a totem's aura. Both answer
/// "what does this footprint mean" from the same TargetRange struct; this one has a card's aim and
/// area to describe as well.
///
/// Static and stateless - it reads a Card and returns strings. Nothing here touches the board, so the
/// hand, the reward screen and a future compendium can all call it with no scene loaded.
/// </summary>
public static class CardRulesText
{
    /// <summary>
    /// Which band this card's stripe should use.
    ///
    /// Self is asked first because it is a narrower claim than the audience alone can make - see
    /// CardTargetBand.Self. Unrestricted and EmptyTile both land on Ground: the difference between
    /// "any tile" and "an empty tile" is a sentence in the expanded panel, not a colour.
    /// </summary>
    public static CardTargetBand Band(Card card)
    {
        if (card == null) { return CardTargetBand.Ground; }

        if (card.AffectsSelfOnly) { return CardTargetBand.Self; }

        return card.Audience switch
        {
            TargetAudience.Ally => CardTargetBand.Ally,
            TargetAudience.Enemy => CardTargetBand.Enemy,
            TargetAudience.AnyCharacter => CardTargetBand.AnyCharacter,
            _ => CardTargetBand.Ground,
        };
    }

    /// <summary>
    /// The number the card face shows beside its range glyph.
    ///
    /// Always the furthest tile, and always shown - a bare bow with no digit would leave "melee" and
    /// "the number failed to draw" looking identical. SelfTile and Anywhere have no distance worth
    /// printing, so they report 0 and CardViewer swaps the glyph instead.
    /// </summary>
    public static int RangeDigit(Card card)
    {
        if (card == null) { return 0; }

        return card.range.Shape switch
        {
            RangeShape.SelfTile => 0,
            RangeShape.Anywhere => 0,
            _ => card.range.MaxDistance,
        };
    }

    /// "1-5 tiles (square)" - how far the card may be aimed, and under which metric.
    public static string RangeLine(Card card)
    {
        if (card == null) { return null; }

        TargetRange range = card.range;

        if (range.Shape == RangeShape.SelfTile) { return "Your own tile"; }

        if (range.Shape == RangeShape.Anywhere) { return "Anywhere on the board"; }

        string metric = range.Shape == RangeShape.Manhattan ? "diamond" : "square";

        // At reach 1 the metric is a distinction without a visible difference on most boards, and
        // naming it only invites the reader to work out which four or eight tiles are meant.
        if (range.MaxDistance <= 1) { return "Adjacent tiles"; }

        if (range.MinDistance <= 0) { return $"Up to {range.MaxDistance} tiles ({metric})"; }

        if (range.MinDistance == range.MaxDistance)
        {
            return $"Exactly {range.MaxDistance} tiles ({metric})";
        }

        return $"{range.MinDistance}-{range.MaxDistance} tiles ({metric})";
    }

    /// "An enemy" - who the card is for, in the same words the stripe is coloured by.
    public static string TargetsLine(Card card)
    {
        if (card == null) { return null; }

        if (card.AffectsSelfOnly) { return "Yourself"; }

        return card.Audience switch
        {
            TargetAudience.Ally => "An ally",
            TargetAudience.Enemy => "An enemy",
            TargetAudience.AnyCharacter => "Any character",
            TargetAudience.EmptyTile => "An empty tile",
            _ => "Any tile",
        };
    }

    /// <summary>
    /// "3x3 around the target" - how far the effect spreads once it lands, or "Single target" when it
    /// does not.
    ///
    /// Describes the first entry that actually spreads. A card mixing two different footprints is
    /// under-described here on purpose: the expanded panel draws the real union as a diagram, and one
    /// sentence trying to name two shapes at once reads worse than the picture beside it.
    /// </summary>
    public static string AreaLine(Card card)
    {
        if (card == null) { return null; }

        foreach (CardEffectEntry entry in card.EffectEntries)
        {
            if (entry.effect == null || entry.area.IsSingle || !entry.effect.SupportsArea) { continue; }

            string anchor = entry.aimsAt == EffectTarget.Source ? "you" : "the target";

            if (entry.area.Kind == AreaKind.Pattern)
            {
                EffectPattern pattern = entry.area.Pattern;

                return pattern != null ? $"{pattern.name} around {anchor}" : $"A set shape around {anchor}";
            }

            TargetRange radius = entry.area.Radius;

            if (radius.MinDistance > 0)
            {
                return $"Ring, {radius.MinDistance}-{radius.MaxDistance} tiles from {anchor}";
            }

            if (radius.Shape == RangeShape.Manhattan)
            {
                return $"Diamond, {radius.MaxDistance} tiles from {anchor}";
            }

            int span = radius.MaxDistance * 2 + 1;

            return $"{span}x{span} around {anchor}";
        }

        return "Single target";
    }
}
