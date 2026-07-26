/// <summary>
/// One status on one character.
///
/// StatusType is the type and this is the copy, the same split as CardData/Card - the enum says what
/// a Poison is, this says how much of it is on this particular goblin and for how long.
///
/// Two independent ways to expire, and a status may use either, both, or neither:
///
///   duration  turnsRemaining ticks down at TurnStart and the status drops at 0.  Poison, Frozen.
///   charge    an event spends a stack.                                           DoubleNextAttack.
///   neither   Indefinite, lasts the whole combat.                                Strength.
///
/// Keeping those separate is what lets one class cover all four. Folding duration into stacks - the
/// Slay the Spire trick where poison's stack count doubles as its remaining turns - would force
/// Strength and Poison into different storage.
/// </summary>
public class Status
{
    /// A turnsRemaining that never ticks down.
    public const int Indefinite = -1;

    public readonly StatusType type;

    /// Magnitude for most statuses, remaining charges for the ones spent by an event.
    public int stacks;

    public int turnsRemaining;

    public Status(StatusType type, int stacks, int turnsRemaining)
    {
        this.type = type;
        this.stacks = stacks;
        this.turnsRemaining = turnsRemaining;
    }

    public bool IsExpired => stacks <= 0 || turnsRemaining == 0;
}
