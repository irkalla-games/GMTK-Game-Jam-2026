/// <summary>
/// Which characters a SelectedCharacterPanel instance describes. Lets one component serve both the
/// hero corner and the enemy corner of the battle HUD without a second class - the two differ only in
/// who they will show, not in how they lay out a name, a bar or a status row.
///
/// PlayerControlled = 0 so the panel already authored in Game.unity - which has always effectively
/// shown the active hero - deserializes into that same behaviour with no scene edit required, the same
/// reasoning RangeShape.Anywhere and IntentKind.Wait give for being the zero value.
/// </summary>
public enum PanelAudience
{
    /// Exactly Character.IsPlayerControlled - the party member you click to control.
    PlayerControlled = 0,

    /// Everything else: Enemy, EnemyAllied, Ally (totems) and Neutral.
    NotPlayerControlled = 1,
}
