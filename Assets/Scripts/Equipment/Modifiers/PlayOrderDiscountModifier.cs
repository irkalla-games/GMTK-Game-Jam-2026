using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "The Nth matching card you play each turn costs less" - Opaline Charm's "the first card with a
/// Draw effect you play each turn costs 0", Manaflow Ring's "the first Defence card you play each turn
/// costs 1 less". Projects a PlayOrderDiscountStatus; see that class and Character.PlayedThisTurn.
///
/// Which cards: `filter` (tags / named cards), widened by `anyCard` or `drawCards`. A "Draw card" is
/// any card holding a DrawEffect entry - there is no Draw CardTag, and the effect is what the card
/// actually does. Which plays: matching plays number from 1 each turn, and plays `fromPlay` through
/// `fromPlay + plays - 1` are discounted - 1/1 is "the first", 2/1 "the second", 1/2 "the first two".
/// How much: `discount` energy off, or the whole cost when `free`.
///
/// A card already printed at 0 neither gets the discount nor counts as a play, so a free Block played
/// first does not use up the ring.
/// </summary>
[CreateAssetMenu(menuName = "Equipment Modifiers/Play Order Discount")]
public class PlayOrderDiscountModifier : EquipmentModifier
{
    [Tooltip("Cards matched by tag or by name.")]
    [SerializeField] private CardFilter filter;
    [Tooltip("Match every card, whatever the filter says.")]
    [SerializeField] private bool anyCard;
    [Tooltip("Also match any card with a Draw effect.")]
    [SerializeField] private bool drawCards;
    [Tooltip("The first matching play each turn that is discounted - 1 is the first.")]
    [SerializeField] private int fromPlay = 1;
    [Tooltip("How many matching plays in a row are discounted, starting at fromPlay.")]
    [SerializeField] private int plays = 1;
    [Tooltip("Energy taken off a discounted play. Ignored when free.")]
    [SerializeField] private int discount = 1;
    [Tooltip("A discounted play costs 0, whatever its printed cost.")]
    [SerializeField] private bool free;

    public override void Project(Character carrier, List<Status> into) =>
        into.Add(new PlayOrderDiscountStatus(this));

    /// Whether `card` is one this item watches - both what gets discounted and what counts as a play.
    /// A 0-cost card is never one: there is nothing to take off it.
    public bool Watches(Card card)
    {
        if (card == null || card.baseCost <= 0) { return false; }

        return anyCard || (drawCards && card.HasEffect<DrawEffect>()) || filter.Matches(card.Data);
    }

    /// What `card` costs less for `carrier` right now: nothing unless the next matching play this turn
    /// falls inside the discounted window.
    public int DiscountFor(Character carrier, Card card)
    {
        if (carrier == null || !Watches(card)) { return 0; }

        int played = 0;
        foreach (Card earlier in carrier.PlayedThisTurn)
        {
            if (Watches(earlier)) { played++; }
        }

        int first = Mathf.Max(1, fromPlay);
        int next = played + 1;
        if (next < first || next >= first + Mathf.Max(1, plays)) { return 0; }

        return free ? card.baseCost : Mathf.Max(0, discount);
    }

    public override string Describe()
    {
        int first = Mathf.Max(1, fromPlay);
        int count = Mathf.Max(1, plays);
        string which = count == 1 ? $"{Ordinal(first)} {Subject()}" : $"{Ordinal(first)} {count} {Subject()}s";
        string verb = count == 1 ? "costs" : "cost";
        string amount = free ? $"{verb} 0" : $"{verb} {Mathf.Max(0, discount)} less";
        return $"{which} each turn {amount}";
    }

    private string Subject() =>
        anyCard ? "card"
        : drawCards && (filter.tags == null || filter.tags.Count == 0) && (filter.cards == null || filter.cards.Count == 0) ? "Draw card"
        : filter.tags != null && filter.tags.Count == 1 && !drawCards && (filter.cards == null || filter.cards.Count == 0) ? $"{filter.tags[0]} card"
        : "matching card";

    private static string Ordinal(int n) => n switch
    {
        1 => "First",
        2 => "Second",
        3 => "Third",
        _ => $"{n}th",
    };
}
