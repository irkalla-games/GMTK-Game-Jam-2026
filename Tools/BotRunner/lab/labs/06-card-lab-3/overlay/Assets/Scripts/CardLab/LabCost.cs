using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CARD LAB 3 (bot-mirror only, never in the real project). Three lab rules, all asked live so the hand,
/// the energy check, the resolve and the bot's previews see the same answer at the moment they look:
///
///  - card cost: a CardData carrying labCondition costs `labDiscount` less while its holder meets it
///    (the patched Card.cost getter asks Discount);
///  - entry bonus: a CardEffectEntry carrying labCondition AND labBonus adds labBonus to its number while
///    the caster meets the condition (EntryDelta replaces entry.amountDelta at every ActionContext);
///  - entry gate: a CardEffectEntry carrying labCondition and NO labBonus only lands while the caster meets
///    the condition (EntryLive, asked in Card.Landed and Card.EntryFootprint).
///
/// "Next to" is the 8 surrounding tiles, diagonals included.
/// </summary>
public static class LabCost
{
    public const int None = 0, NextToTotem = 1, HasShield = 2, NextToHero = 3, HasBlock = 4;

    public static int Discount(Character holder, CardData data)
    {
        if (holder == null || data == null || data.labDiscount <= 0 || holder.Tile == null) { return 0; }

        return Met(holder, data.labCondition) ? data.labDiscount : 0;
    }

    public static int EntryDelta(CardEffectEntry entry, Character source)
    {
        if (entry.labCondition == None || entry.labBonus == 0) { return entry.amountDelta; }

        return entry.amountDelta + (Met(source, entry.labCondition) ? entry.labBonus : 0);
    }

    public static bool EntryLive(CardEffectEntry entry, Character source) =>
        entry.labCondition == None || entry.labBonus != 0 || Met(source, entry.labCondition);

    public static bool Met(Character holder, int condition)
    {
        if (holder == null || holder.Tile == null) { return false; }

        switch (condition)
        {
            case NextToTotem:
                foreach (Totem totem in Totem.Active)
                {
                    if (totem == null) { continue; }

                    Character body = totem.GetComponent<Character>();

                    if (body != null && !body.IsHostileToParty && Adjacent(holder, body)) { return true; }
                }

                return false;
            case HasShield:
                return holder.StatusStacks(StatusType.Shield) > 0;
            case HasBlock:
                return holder.StatusStacks(StatusType.Block) > 0;
            case NextToHero:
                return HeroesNextTo(holder, holder.Tile).Count > 0;
        }

        return false;
    }

    /// Living, non-totem, player-controlled characters other than `self` on the 8 tiles around `at`.
    public static List<Character> HeroesNextTo(Character self, GridTile at)
    {
        List<Character> found = new();

        if (at == null || BattleManager.Instance == null) { return found; }

        foreach (Character other in BattleManager.Instance.Characters)
        {
            if (other == null || other == self || !other.IsPlayerControlled || other.IsDead || other.Tile == null) { continue; }
            if (other.GetComponent<Totem>() != null) { continue; }
            if (Chebyshev(at.Coordinates, other.Tile.Coordinates) <= 1) { found.Add(other); }
        }

        return found;
    }

    public static int Chebyshev(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

    private static bool Adjacent(Character a, Character b)
    {
        if (b == null || b == a || b.IsDead || b.Tile == null || a.Tile == null) { return false; }

        return Chebyshev(a.Tile.Coordinates, b.Tile.Coordinates) <= 1;
    }

    // ------------------------------------------------------------------ what the bot's Formation term reads

    /// The authored number of an entry's effect, before its delta - 0 for an effect with no number.
    public static int Authored(CardEffect effect) => effect switch
    {
        DamageEffect d => d.Damage,
        ShieldEffect s => s.ShieldAmount,
        HealEffect h => h.RiderAmount,
        BlockEffect b => b.BlockCount,
        DrawEffect r => r.RiderAmount,
        LabAfterMoveEffect a => a.Amount,
        _ => 0,
    };

    /// Rough planner points for `amount` of `effect` - the scale BotWeights works in (about 1 per point of
    /// healing, 0.7 per point of damage or shield, 2.5 per card or energy).
    public static float Points(CardEffect effect, int amount) => effect switch
    {
        DamageEffect => 0.7f * amount,
        ShieldEffect => 0.7f * amount,
        HealEffect => 1f * amount,
        BlockEffect => 2f * amount,
        DrawEffect => 2.5f * amount,
        EnergyEffect => 2.5f * amount,
        _ => 1f * amount,
    };

    /// True for an entry that shares with the heroes beside you: aimed at the caster, a radius of 1.
    public static bool IsShared(CardEffectEntry entry) =>
        entry.effect != null && entry.aimsAt == EffectTarget.Source && entry.area.Kind == AreaKind.Radius
        && entry.area.Radius.MaxDistance == 1;

    /// True for a card whose payoff depends on standing next to another hero.
    public static bool WantsCompany(CardData data)
    {
        if (data == null) { return false; }
        if (data.labCondition == NextToHero && data.labDiscount > 0) { return true; }

        foreach (CardEffectEntry entry in data.effectEntries)
        {
            if (entry.labCondition == NextToHero || IsShared(entry)) { return true; }
        }

        return false;
    }

    /// <summary>
    /// What the party's hands are worth from standing together on the board as it is right now: every
    /// switched-on discount and bonus, every shared entry times the heroes it would reach, plus a share of
    /// next turn's for each hero standing next to someone (how much of their draw pile wants company).
    /// The bot's Formation term is this after a move minus before it.
    /// </summary>
    public static float FormationPayoff(IReadOnlyList<Character> heroes)
    {
        float total = 0f;

        foreach (Character hero in heroes)
        {
            if (hero == null || hero.IsDead || hero.Tile == null) { continue; }

            int neighbours = HeroesNextTo(hero, hero.Tile).Count;
            bool met = neighbours > 0;

            foreach (Card card in hero.Hand)
            {
                if (card == null || card.LockedTurns > 0 || card.Data == null) { continue; }

                if (met && card.Data.labCondition == NextToHero) { total += 2.5f * card.Data.labDiscount; }

                foreach (CardEffectEntry entry in card.EffectEntries)
                {
                    if (entry.effect == null) { continue; }

                    if (met && entry.labCondition == NextToHero)
                    {
                        total += entry.labBonus != 0
                            ? Points(entry.effect, entry.labBonus)
                            : Points(entry.effect, Authored(entry.effect) + entry.amountDelta);
                    }

                    if (IsShared(entry) && entry.area.Radius.MinDistance <= 1)
                    {
                        total += neighbours * Points(entry.effect, Authored(entry.effect) + entry.amountDelta);
                    }
                }
            }

            if (met)
            {
                int wanting = 0, pile = 0;

                foreach (Card card in hero.DrawPile) { pile++; if (card != null && WantsCompany(card.Data)) { wanting++; } }
                foreach (Card card in hero.DiscardPile) { pile++; if (card != null && WantsCompany(card.Data)) { wanting++; } }

                if (pile > 0) { total += 7.5f * wanting / pile; }
            }
        }

        return total;
    }
}
