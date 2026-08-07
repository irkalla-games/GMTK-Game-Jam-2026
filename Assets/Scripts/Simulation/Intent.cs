using UnityEngine;

/// <summary>
/// The category an enemy announces at the top of the turn. Not a card and not a tile - those are
/// re-derived when it actually acts, against a board the player has spent the turn rearranging.
///
/// Wait = 0 because Intent is a struct and Intent.Wait() is `default` - the all-zero value has to mean
/// "nothing promised", the same reasoning as RangeShape.Anywhere = 0. These values are written into
/// IntentIcons.asset, so append new kinds and never reorder them.
///
/// The icon this drives is a LOWER bound on threat. A committed Move may turn into an Attack if one
/// becomes legal; a committed Attack may come to nothing. It never goes the other way - see
/// EnemyBrain.Resolve.
/// </summary>
public enum IntentKind
{
    Wait = 0,
    Move = 1,
    Attack = 2,
    Summon = 3,
}

/// <summary>
/// One thing an enemy means to do: play this card on that tile.
///
/// Not a bespoke Move/Attack pair. An enemy's damage, reach and movement distance are properties of
/// its cards, exactly as they are for the player - a goblin hits for 6 because it holds a card that
/// deals 6, not because a number on the goblin says so. Two systems for "how far can this thing
/// reach" is one too many, and the card one already answers it.
///
/// Targets a tile, never a character. Whoever is standing there is resolved when the card actually
/// resolves, which is what lets a committed intent miss: the player has a whole turn to move the
/// target out from under it.
///
/// The card and tile are not the promise - the kind is. What gets committed at TurnStart is only
/// Intent.kind; EnemyBrain.Resolve re-derives a concrete card and tile inside that kind against the
/// live board when the enemy actually acts. See EnemyBrain.Resolve for the exact rules.
/// </summary>
public struct Intent
{
    public IntentKind kind;

    public Card card;

    public Vector2Int target;

    public bool IsWait => kind == IntentKind.Wait || card == null;

    public static Intent Wait() => default;

    public static Intent Play(IntentKind kind, Card card, Vector2Int target) =>
        new() { kind = kind, card = card, target = target };

    public override string ToString() =>
        IsWait ? "Wait" : $"{kind}: {card.cardName} at {target}";
}
