/// <summary>
/// The carrier goes after whoever taunted it, ignoring its own TargetingPattern - both the victim it
/// attacks and the direction it walks, because TargetSelector.TryPick answers both questions and this
/// overrides it in one place.
///
/// While the taunter is out of reach the carrier takes *no* attack at all rather than falling back to
/// its pattern: TryFindAttack asks TryPick with only the enemies it could legally hit, so a taunter
/// missing from that list comes back as "no attack this action point", and the brain drops through to
/// its move - whose quarry is this same taunter. That is what turns the taunt into a pull instead of a
/// mere redirect.
///
/// The one status carrying a reference to a *character*, which is why it cannot come from
/// StatusEffect.Create: that factory's type/stacks signature has nowhere to put a taunter. TauntEffect
/// and TauntAction are its authoring half - see GridTile.Taunt.
///
/// Binary, like Frozen: the counter is remaining turns and the effect itself has no magnitude, so
/// duration is the only lever there is.
/// </summary>
public class TauntStatus : StatusEffect
{
    /// Not readonly - Merge swaps it when a second character taunts the same target. Still per-copy
    /// state on a runtime object, never on an asset.
    private Character taunter;

    public TauntStatus(Character taunter, int stacks) : base(StatusType.Taunt, stacks)
    {
        this.taunter = taunter;
    }

    /// <summary>
    /// The taunter, but only while it is still alive and standing somewhere.
    ///
    /// The aliveness guard is load-bearing rather than defensive: a taunt pointing at a corpse would
    /// otherwise be a taunt that can never be satisfied, and since an unreachable taunter suppresses
    /// the attack outright, that would leave the enemy refusing to attack anybody until the duration
    /// ran out. Answering null makes a dead taunter read as no taunt at all, so the pattern resumes.
    /// </summary>
    public override Character ForcedQuarry(Character carrier) =>
        taunter != null && !taunter.IsDead && taunter.Tile != null ? taunter : null;

    /// <summary>
    /// A second taunt replaces the first outright - newest taunter wins, with the incoming duration.
    ///
    /// The base implementation would add the counters and keep the *old* taunter, which is the opposite
    /// of what a fresh taunt from a second character means. Taking the incoming duration rather than
    /// the longer of the two is the same reasoning: the old taunt is gone, not extended, so inheriting
    /// its clock would be inheriting a clock that belonged to somebody else.
    /// </summary>
    public override void Merge(StatusEffect incoming)
    {
        if (incoming is TauntStatus fresh) { taunter = fresh.taunter; }

        stacks = incoming.stacks;
    }

    /// Self-ticking: the counter is remaining turns, so a turn passing spends one.
    public override void OnTurnEnd(Character carrier)
    {
        stacks--;
    }

    /// No number worth showing - the same call Frozen and Rooted make. Who it points at is visible on
    /// the board, in where the enemy walks.
    public override string Describe() => "Taunt";
}
