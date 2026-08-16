/// <summary>
/// A buff that rides along on the carrier's attacks: every hit that connects does something beyond
/// the damage. Poison Blade envenoms the victim; a Knight's counterpart will shield the attacker.
///
/// Turns-shaped by decision, not by accident: stacks counts turns and the rider fires as often as the
/// carrier lands a hit, so one swing catching three enemies riders all three of them. A charge-shaped
/// version would have to answer "one charge for the swing, or one per victim" - a question this shape
/// never has to raise, because OnDamageDealt already fires once per victim and the duration does not
/// care how many times it fires in one turn.
///
/// Subclassing this is the whole cost of a new on-hit rider: override OnDamageDealt, add a StatusType
/// value, a line in StatusEffect.Create, a Glossary row and a card row.
/// </summary>
public abstract class OnHitStatus : StatusEffect
{
    protected OnHitStatus(StatusType type, int stacks) : base(type, stacks) { }

    /// Self-ticking, and sealed: every rider of this shape spends its counter the same way, on the
    /// carrier's own turn ending, regardless of how many hits landed during it.
    public sealed override void OnTurnEnd(Character carrier)
    {
        stacks--;
    }
}
