/// <summary>
/// Which side a Character is on, and whether the player clicks it directly.
///
/// Enemy = 0 so this matches the old `isPlayerControlled` bool's default (false) - every Character
/// authored before this enum existed keeps behaving like a plain enemy rather than silently joining
/// the party.
/// </summary>
public enum PlayableCharacter
{
    /// Hostile, AI-controlled. The default.
    Enemy = 0,

    /// Hostile, AI-controlled, but a separate roster slot from Enemy - e.g. something an enemy card
    /// summons mid-battle. Same side as Enemy for targeting.
    EnemyAllied = 1,

    /// Friendly and the one the player clicks to control. Only this value makes OnTileClicked
    /// activate the character and counts toward CanAnyoneAct / the loss condition.
    AllyPlayable = 2,

    /// Friendly, but AI-controlled rather than clicked - e.g. a summoned ally. Same side as
    /// AllyPlayable for targeting.
    Ally = 3,

    /// On nobody's side. Not an ally to anyone, and not an enemy to anyone either.
    Neutral = 4,
}
