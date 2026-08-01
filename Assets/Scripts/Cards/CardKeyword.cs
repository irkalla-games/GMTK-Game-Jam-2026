/// <summary>
/// One keyword on one card copy. type and magnitude are the authored rule (magnitude is Cooldown's
/// length, restored every time it resets); remaining is the live countdown Cooldown ticks down and
/// Refusal checks. The same type/copy split as CardData/Card and StatusType/Status.
/// </summary>
public class CardKeyword
{
    public readonly CardKeywordType type;
    public readonly int magnitude;
    public int remaining;

    public CardKeyword(CardKeywordType type, int magnitude, int remaining)
    {
        this.type = type;
        this.magnitude = magnitude;
        this.remaining = remaining;
    }
}
