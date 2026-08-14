using UnityEngine;

/// <summary>
/// Something a tile itself carries, independent of whoever is standing on it - a wall, a patch of fire.
/// The tile-level mirror of Status: same self-ticking shape, same gate-vs-notification split, just
/// carried by a GridTile instead of a Character.
///
/// Two kinds of member:
///
///   EnterRefusal   a gate. Asked *before* a move, by GridManager.MoveRefusal - the same choke point
///                  Rooted's MoveRefusal already answers through, so a wall blocks a Move card's
///                  highlight, a Dodge sidestep and enemy pathing all at once, with no separate check
///                  anywhere.
///   OnTurnEnd      a notification. Something has happened - the round has ended - and the effect
///                  reacts and ages itself in the same method, exactly like Status.OnTurnEnd.
///
/// One counter, `turnsRemaining`, and every effect ages itself by decrementing its own inside
/// OnTurnEnd - nothing outside a TileEffect owns ageing, same reason Status collapsed stacks and
/// turnsRemaining into one field.
/// </summary>
public abstract class TileEffect
{
    public abstract TileEffectType type { get; }

    public abstract int turnsRemaining { get; set; }

    public bool IsExpired => turnsRemaining <= 0;

    /// Why this effect refuses to let `mover` step onto `tile`, or null if it does not object. Wall of
    /// Force. Asked by GridManager.MoveRefusal, the single choke point every kind of movement already
    /// goes through - MoveEffect.Refusal, GridManager.MoveCharacter, Dodge's StepAwayFrom, and enemy
    /// pathing via Board.IsWalkable all honour it for free.
    public virtual string EnterRefusal(Character mover, GridTile tile) => null;

    /// The end of the player turn - see GridManager.TickTileEffects and BattleManager.RunBattle. Wall
    /// of Flames bites here, then decrements, same order Poison bites-then-decays.
    public virtual void OnTurnEnd(GridTile tile) { }

    /// Folds a fresh application of this same type into the one already on the tile. Adding, the same
    /// reading StatusEffect.Merge uses - a second Wall of Flames laid on a still-burning tile extends
    /// it rather than replacing it.
    public virtual void Merge(TileEffect incoming)
    {
        turnsRemaining += incoming.turnsRemaining;
    }

    /// What a UI or log line shows for this effect.
    public virtual string Describe() => $"{type} ({turnsRemaining} turns left)";

    /// Colour of the wash this effect leaves on its tile - see TileEffectOverlay. Alpha is the effect's
    /// own call, unlike Totem.AuraColor where AuraPulse owns it, because a tile effect's tint is static
    /// rather than pulsing.
    public virtual Color OverlayColor => Color.clear;

    /// <summary>
    /// The one place a TileEffectType turns into the object that implements it. Returns null for None
    /// and anything not yet implemented, so an unset dropdown does nothing rather than throwing.
    /// `magnitude` is ignored by effects that have no number of their own - Wall of Force cares only
    /// about `turns`.
    /// </summary>
    public static TileEffect Create(TileEffectType type, int turns, int magnitude) => type switch
    {
        TileEffectType.WallOfForce => new WallOfForceTileEffect(turns),
        TileEffectType.WallOfFlames => new WallOfFlamesTileEffect(turns, magnitude),
        _ => null,
    };
}
