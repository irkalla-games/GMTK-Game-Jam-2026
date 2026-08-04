/// <summary>
/// The carrier cannot move. Everything else - attacking, playing cards - still works.
///
/// Frozen's other half, split out rather than folded in, because the two are different cards: Frozen
/// is a flat tempo tax that always denies something, while this is situational and swingy. It blanks
/// a Warrior still walking toward you and it pins a Ranger in melee where it can only plink, but it
/// does nothing at all to a Warrior already standing next to you.
///
/// The refusal goes through GridManager.MoveRefusal, so the Move card's highlight goes dark for a
/// rooted character and an enemy brain stops proposing walks - see Status.MoveRefusal.
/// </summary>
public class RootedStatus : StatusEffect
{
    public RootedStatus(int stacks, int turnsRemaining)
        : base(StatusType.Rooted, stacks, turnsRemaining) { }

    public override string MoveRefusal(Character carrier, GridTile destination) =>
        $"{(carrier != null ? carrier.name : "it")} is rooted in place";

    public override string Describe() => "Rooted";
}
