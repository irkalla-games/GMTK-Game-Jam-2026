/// <summary>
/// One passive buff an AuraSource is currently maintaining on one character, for as long as that
/// character stands in its range.
///
/// Kept separate from Status rather than going through AddStatus: a Status has no owner, so re-applying
/// merges into one shared per-type stack count with no record of who put stacks there - fine for a card
/// effect that only ever adds, but there is then no way to later remove exactly what one aura granted
/// without also touching stacks a card, or a second overlapping aura, put on the same type. An
/// AppliedAura instead remembers its own source, so a character leaving range - or its source dying -
/// withdraws exactly this entry via Character.RemoveAura and leaves every other stack untouched.
/// </summary>
public class AppliedAura
{
    public readonly AuraSource source;
    public readonly StatusType type;
    public readonly int stacks;

    public AppliedAura(AuraSource source, StatusType type, int stacks)
    {
        this.source = source;
        this.type = type;
        this.stacks = stacks;
    }
}
