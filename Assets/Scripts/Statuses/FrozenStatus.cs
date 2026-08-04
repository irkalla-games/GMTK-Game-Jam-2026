/// <summary>
/// The carrier loses its whole turn: no cards, no attack, no movement.
///
/// Enforced through Character.CanAct, which asks every status for an ActRefusal. It is a fact about
/// the actor and not about the tile being clicked, so it stays out of Card.Refusal - CardPlayManager
/// checks it above the commit point so a frozen click costs nothing, and BattleManager skips a frozen
/// enemy outright.
///
/// A gate rather than an OnTurnStart hook on purpose: freezing an enemy during PlayerActing has to
/// deny the action it takes in EnemyResolve that same round, and a turn-start hook would already have
/// run by then.
///
/// No MoveRefusal of its own: a character who cannot act cannot play a Move card either. Rooted is the
/// status that stops only movement, and the two stay separate so they compose.
///
/// Its value is asymmetric and worth watching while tuning - an enemy has one action point, so
/// freezing one denies a single 3-5 damage swing, while freezing a player denies roughly three cards.
/// The lever for making it stronger is duration, not stacks: the effect is binary.
/// </summary>
public class FrozenStatus : Status
{
    public FrozenStatus(int stacks, int turnsRemaining)
        : base(StatusType.Frozen, stacks, turnsRemaining) { }

    public override string ActRefusal(Character carrier) =>
        $"{(carrier != null ? carrier.name : "it")} is frozen solid";

    public override string Describe() => "Frozen";
}
