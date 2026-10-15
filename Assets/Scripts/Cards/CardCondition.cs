using UnityEngine;

/// <summary>
/// A board condition a card asks about its holder - "next to another hero", "while you have Block",
/// "alone". Authored in two places: CardData.costCondition (the card costs less while it holds) and
/// CardEffectEntry.condition (one effect hits harder, or only fires, while it holds).
///
/// Values are written into .asset files, so append only, never reorder - the same rule as StatusType
/// and RangeShape. None = 0 is what every card authored before this existed deserializes to, and it
/// has to keep meaning "no condition".
/// </summary>
public enum CardCondition
{
    None = 0,

    /// A friendly totem stands on one of the 8 tiles around you.
    NextToTotem = 1,

    /// You carry Shield, auras included.
    HasShield = 2,

    /// You carry Block, auras included.
    HasBlock = 3,

    /// Another hero stands on one of the 8 tiles around you.
    NextToHero = 4,

    /// No other hero within CardConditions.AloneDistance - 1 tiles.
    Alone = 5,
}

/// <summary>
/// Answers CardCondition about a character. The one definition of every condition: the card's cost,
/// its effects, the card face and the balance bot all ask here, so none of them can disagree about
/// whether a hero is "next to" anyone.
///
/// Asked live, every time - never cached and never written into a field. A hero stepping next to an
/// ally changes the answer for every card in both their hands at once, and a stored copy is one more
/// thing that would have to hear about it. See Card.cost for the same rule on the cost side.
///
/// "Hero" means a living, player-controlled character that is not a totem. A summoned ally is AI-
/// resolved (IsPlayerControlled is false), so it neither counts as company nor breaks "alone" - which
/// is what the card labs measured these cards with.
/// </summary>
public static class CardConditions
{
    /// <summary>
    /// "Alone" means every other hero stands at least this many tiles away, Chebyshev (a diagonal step
    /// counts as one) - so at 3, nobody within 2. Card Lab IV tested 2, 3 and 4 and found no difference
    /// in results; 3 is where the condition holds about half the time, which makes it a real choice.
    /// One game-wide number, because the glossary explains it once for every card.
    /// </summary>
    public const int AloneDistance = 3;

    /// Whether `holder` meets `condition` where they stand right now. A card with no holder (on a reward
    /// screen) or a holder off the board meets nothing.
    public static bool Met(CardCondition condition, Character holder) =>
        holder != null && MetAt(condition, holder, holder.Tile);

    /// <summary>
    /// Whether `holder` would meet `condition` if they stood on `at` - the balance bot asks this of a
    /// move's destination without moving anyone. `holder` itself is skipped wherever it actually
    /// stands, so asking about a tile next to its own position does not count it as its own company.
    /// None is always met, so callers can ask unconditionally.
    /// </summary>
    public static bool MetAt(CardCondition condition, Character holder, GridTile at)
    {
        if (condition == CardCondition.None) { return true; }
        if (holder == null || at == null) { return false; }

        switch (condition)
        {
            case CardCondition.NextToTotem:
                foreach (Totem totem in Totem.Active)
                {
                    if (totem == null) { continue; }

                    Character body = totem.GetComponent<Character>();

                    if (body == null || body == holder || body.IsDead || body.IsHostileToParty || body.Tile == null) { continue; }
                    if (Distance(at, body.Tile) <= 1) { return true; }
                }

                return false;

            case CardCondition.HasShield:
                return holder.StatusStacks(StatusType.Shield) > 0;

            case CardCondition.HasBlock:
                return holder.StatusStacks(StatusType.Block) > 0;

            case CardCondition.NextToHero:
                return NearestHero(holder, at) <= 1;

            case CardCondition.Alone:
                return NearestHero(holder, at) >= AloneDistance;
        }

        return false;
    }

    /// <summary>
    /// How far the nearest other hero is from `at`, or int.MaxValue when there is none - which is what
    /// makes the last hero standing "alone" and never "next to" anyone.
    /// </summary>
    public static int NearestHero(Character holder, GridTile at)
    {
        int nearest = int.MaxValue;

        if (at == null || BattleManager.Instance == null) { return nearest; }

        foreach (Character other in BattleManager.Instance.Characters)
        {
            if (!IsHero(other) || other == holder || other.Tile == null) { continue; }

            nearest = Mathf.Min(nearest, Distance(at, other.Tile));
        }

        return nearest;
    }

    /// Whether this condition depends on where heroes stand - what the card face needs re-asked after
    /// any move, and what a bot looking at a move's destination has to evaluate there.
    public static bool IsPositional(CardCondition condition) =>
        condition == CardCondition.NextToTotem
        || condition == CardCondition.NextToHero
        || condition == CardCondition.Alone;

    private static bool IsHero(Character c) =>
        c != null && !c.IsDead && c.IsPlayerControlled && c.GetComponent<Totem>() == null;

    private static int Distance(GridTile a, GridTile b)
    {
        Vector2Int d = a.Coordinates - b.Coordinates;
        return Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y));
    }
}
