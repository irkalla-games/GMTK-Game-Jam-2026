using UnityEngine;

/// <summary>
/// Discards one card at random from the carrier's hand at the start of each of its next few turns -
/// the Reaper's Grave Tithe.
///
/// Deferred on purpose. An enemy that reached into your hand the moment it played the card would take
/// nothing worth having: BattleManager.TurnStart discards every hand and redraws it, so mid-enemy-turn
/// theft only claims a card you were about to lose regardless. Firing on the way into your turn, after
/// the fresh hand has been dealt, is the only point where losing a card costs a real play.
///
/// Ordering is what makes that work, and it is not an accident: TurnStart draws (DrawCards) before it
/// runs the status hooks (Character.OnTurnStart), so by the time this fires the hand is already full.
///
/// Random rather than chosen: the enemy has no way to value a hand, and picking the best card would
/// read as a targeted punish rather than a theft. Random is also the version a player can play around,
/// by spending down rather than hoarding.
/// </summary>
public class PilferedStatus : StatusEffect
{
    public PilferedStatus(int stacks) : base(StatusType.Pilfered, stacks) { }

    public override void OnTurnStart(Character carrier)
    {
        if (carrier == null || carrier.Hand.Count == 0) { return; }

        Card taken = carrier.Hand[Random.Range(0, carrier.Hand.Count)];

        carrier.Discard(taken);

        Debug.Log($"{carrier.name} lost {taken.Data.cardName} to Grave Tithe");
    }

    /// Spends a turn like every other timed curse. OnTurnEnd rather than inside OnTurnStart, so the
    /// count a player reads during their turn is the number of turns still to come.
    public override void OnTurnEnd(Character carrier)
    {
        stacks--;
    }

    public override string Describe() => "Discards a card at the start of your turn";
}
