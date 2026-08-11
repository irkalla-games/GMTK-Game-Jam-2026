using UnityEngine;

/// <summary>
/// The category an enemy would act on if its turn came right now. Not a card and not a tile - those
/// are re-derived every time this is asked, against the board as it currently stands.
///
/// Wait = 0 because Intent is a struct and Intent.Wait() is `default` - the all-zero value has to mean
/// "nothing promised", the same reasoning as RangeShape.Anywhere = 0. These values are written into
/// IntentIcons.asset, so append new kinds and never reorder them.
///
/// The icon this drives is live, not a promise made once at TurnStart - BattleManager recomputes every
/// enemy's Intent whenever the board changes (see BattleManager.LateUpdate) and re-asks it outright
/// when the enemy actually acts, so what you see over an enemy's head is always what EnemyBrain.Decide
/// would return right now.
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
/// resolves, so moving a target out from under a committed Attack turns it back into a Move on the
/// very next recompute rather than firing anyway.
///
/// Every field here is re-derived together, every time - see EnemyBrain.Decide. There is no separate
/// "kind was promised, card and tile are still stale" step; CommittedIntent always holds the result of
/// the most recent Decide call.
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
