/// <summary>
/// The carrier cannot be targeted by enemy target selection for as long as this lasts. Enemies neither
/// swing at nor walk toward it - see TargetSelector.TryPick, which drops a hidden candidate before
/// either the forced quarry or the priority gets to see it.
///
/// A pure duration: attacking does not spend or break it, the same self-ticking shape as Rooted and
/// Frozen. It says nothing about incoming damage that still finds the carrier some other way - an area
/// attack aimed at somebody else can still splash a stealthed character, the same as Taunt only ever
/// governs who is aimed at and not what a footprint catches.
/// </summary>
public class StealthStatus : StatusEffect
{
    public StealthStatus(int stacks) : base(StatusType.Stealth, stacks) { }

    public override bool Hides(Character carrier) => true;

    /// Self-ticking: the counter is remaining turns, so a turn passing spends one.
    public override void OnTurnEnd(Character carrier)
    {
        stacks--;
    }

    public override string Describe() => "Stealth";
}
