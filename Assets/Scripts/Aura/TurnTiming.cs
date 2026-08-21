/// <summary>
/// When a TurnTick aura grants its status, relative to the round. Authored on AuraData, read by
/// TurnTickStatus.
///
/// Written into .asset files, so append-only like every other enum here - see StatusType.
///
/// TurnEnd = 0 so an AuraData whose timing was never touched (every totem authored before this field
/// existed, and any of the six new totems that do not care) defaults to the more common case rather
/// than silently picking the other one.
///
/// The choice is not cosmetic: Character.OnTurnStart/OnTurnEnd both walk one ActiveStatuses snapshot
/// with auras first, then the carrier's own statuses. A TurnTick that shares its timing with the
/// status it grants gets undone in the same pass - a Weaken aura ticking at TurnEnd would top up
/// Weaken 2 just before WeakenStatus.OnTurnEnd zeroes it right back out in that same call. Pick the
/// timing that lands *before* the granted status's own tick: TurnEnd for a status that resets at
/// TurnStart (Shield - ShieldStatus.OnTurnStart wipes it; Block has no turn hook at all, so either
/// timing is safe and TurnEnd keeps it live through EnemyResolve), TurnStart for one that resets at
/// TurnEnd (Weaken - TurnStart keeps it live for the enemy's own action point).
/// </summary>
public enum TurnTiming
{
    TurnEnd = 0,
    TurnStart = 1,
}
