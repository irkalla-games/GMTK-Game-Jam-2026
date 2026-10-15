/// <summary>
/// Waives the cost of any card with a Draw effect until the carrier has played one this round -
/// Opaline Charm. A "Draw card" is any card holding a DrawEffect entry: there is no Draw CardTag, and
/// the effect is what the card actually does.
///
/// Holds no state of its own - it is rebuilt on every ActiveStatuses call, so "already used this turn"
/// lives on the carrier (Character.PlayedDrawCardThisTurn, set below the commit point and cleared at
/// the top of the round). Read through Card.cost, so the card face, CanAfford and the bot all see the
/// 0 at the same moment, and every other Draw card in hand goes back to full price the instant the
/// first one is played.
///
/// Reports StatusType.None for the same reason ResourceStatus does: equipment has its own UI.
/// </summary>
public class FirstDrawFreeStatus : StatusEffect
{
    public FirstDrawFreeStatus() : base(StatusType.None, 1) { }

    public override bool WaivesCost(Character carrier, Card card) =>
        carrier != null && !carrier.PlayedDrawCardThisTurn && card != null && card.HasEffect<DrawEffect>();

    public override string Describe() => "First Draw card each turn costs 0";
}
