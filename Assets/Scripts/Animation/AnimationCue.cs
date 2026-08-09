/// <summary>
/// What a character is being asked to do, in presentation terms - the vocabulary GameAction, CardData
/// and CharacterAnimator all speak so a card can name an intent without knowing how any particular body
/// performs it.
///
/// None = 0 so a CardData or GameAction authored before this field existed deserializes to "animates
/// nothing", same reasoning as RangeShape.Anywhere - see CLAUDE.md. Values are written into .asset
/// files: append new cues at the end, never reorder or remove one.
/// </summary>
public enum AnimationCue
{
    /// No cue. On a GameAction this means the action never animates on its own; in a CueOverride's
    /// `play` it means "do not redirect" - the action's own cue stands. Never "perform nothing",
    /// which is Silent.
    None = 0,

    /// Swung at something in reach. DamageAction defaults to this when the card's range is melee.
    MeleeAttack = 1,

    /// Thrown, shot or hurled at something further off - the *delivery* is at range, whatever the
    /// character's art actually shows. A mage's staff-cast attack animation belongs here, not on Cast:
    /// this is what DamageAction asks for whenever the card's range reaches past one tile.
    RangedAttack = 2,

    /// A spell with no attack reading to it - a heal, a buff, a ritual.
    Cast = 3,

    Summon = 4,
    Move = 5,
    Hurt = 6,
    Die = 7,

    /// <summary>
    /// Perform nothing, deliberately. Only meaningful as a CueOverride's `play`, where it is the one
    /// way to get a card's projectile and impact without the caster animating at all - an
    /// environmental bolt, a trap, something the character did not throw.
    ///
    /// Distinct from None because None came to mean "inherit the action's cue", which is the far more
    /// common thing to want and so earns the value a fresh entry starts at. This is the explicit
    /// opposite, and has to be asked for by name.
    /// </summary>
    Silent = 8,
}
