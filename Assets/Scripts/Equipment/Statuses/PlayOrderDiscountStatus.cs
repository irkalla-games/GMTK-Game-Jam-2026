/// <summary>
/// Takes energy off the Nth matching card its carrier plays each turn - Opaline Charm, Manaflow Ring.
/// Every rule (which cards, which plays, how much) is the PlayOrderDiscountModifier's; this only
/// carries it into the Status pipeline.
///
/// Holds no state of its own - it is rebuilt on every ActiveStatuses call, so "how many have I played
/// this turn" lives on the carrier (Character.PlayedThisTurn, appended below the commit point and
/// cleared at the top of the round). Read through Card.cost, so the card face, CanAfford and the bot
/// all see the discount at the same moment, and every other matching card in hand goes back to full
/// price the instant the window is used up.
///
/// Reports StatusType.None for the same reason ResourceStatus does: equipment has its own UI.
/// </summary>
public class PlayOrderDiscountStatus : StatusEffect
{
    private readonly PlayOrderDiscountModifier rule;

    public PlayOrderDiscountStatus(PlayOrderDiscountModifier rule) : base(StatusType.None, 1)
    {
        this.rule = rule;
    }

    public override int CostDiscount(Character carrier, Card card) =>
        rule != null ? rule.DiscountFor(carrier, card) : 0;

    public override string Describe() => rule != null ? rule.Describe() : "";
}
