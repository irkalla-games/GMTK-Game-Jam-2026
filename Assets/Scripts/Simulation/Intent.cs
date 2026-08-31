using UnityEngine;

/// <summary>
/// The category an enemy would act on if its turn came right now. Not a card and not a tile - those
/// are re-derived every time this is asked, against the board as it currently stands.
///
/// Wait = 0 because Intent is a struct and Intent.Wait() is `default` - the all-zero value has to mean
/// "nothing promised", the same reasoning as RangeShape.Anywhere = 0. These values are written into
/// IntentIcons.asset, so append new kinds and never reorder them.
///
/// The card is a promise made once at TurnStart, not re-decided from scratch on every board change -
/// see Character.LockedCard and BattleManager.Decide. Only the *aim* stays live: BattleManager keeps
/// re-asking EnemyBrain.Reaim for that same card as the board changes (see BattleManager.LateUpdate),
/// so the victim and tile you see over an enemy's head always match where the locked card would
/// actually land, while the card itself only changes if it stops being legal anywhere at all.
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
/// `kind` and `card` are a promise for the whole turn - see Character.LockedCard. `target` and
/// `victim` are re-derived every time the board changes, by re-aiming that same card - see
/// EnemyBrain.Reaim and BattleManager.Decide - so CommittedIntent always holds where the locked card
/// would land right now, never a stale tile. Character.LockedAim is a second, narrower freeze on top
/// of this one: Dodge locks a copy of a whole Attack Intent, tile included, so it can be replayed
/// against a tile the victim has already left - see BattleManager.LockAimsOn.
/// </summary>
public struct Intent
{
    public IntentKind kind;

    public Card card;

    public Vector2Int target;

    /// Who TryFindAttack picked this Attack for, out of TargetSelector.TryPick - null for Move and
    /// Summon, which have no victim. Not derived from the footprint: an area card aimed at the mage
    /// that happens to splash the rogue is still "aiming at the mage", so only this field can answer
    /// "was this enemy planning to attack the rogue specifically" - see BattleManager.LockAimsOn.
    public Character victim;

    public bool IsWait => kind == IntentKind.Wait || card == null;

    /// True when every field agrees - not just kind. BattleManager.LateUpdate's recompute pass reads
    /// this rather than comparing kind alone, so an Attack that re-aimed onto a different tile or
    /// victim (the same locked card, a different target) still repaints the damage number and icon
    /// position, while an identical re-decide costs nothing.
    public bool Matches(Intent other) =>
        kind == other.kind && card == other.card && target == other.target && victim == other.victim;

    public static Intent Wait() => default;

    public static Intent Play(IntentKind kind, Card card, Vector2Int target, Character victim = null) =>
        new() { kind = kind, card = card, target = target, victim = victim };

    public override string ToString() =>
        IsWait ? "Wait" : $"{kind}: {card.cardName} at {target}";
}
