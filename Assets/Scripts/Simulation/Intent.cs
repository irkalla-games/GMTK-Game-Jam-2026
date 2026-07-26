using UnityEngine;

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
/// </summary>
public struct Intent
{
    public Card card;

    public Vector2Int target;

    public bool IsWait => card == null;

    public static Intent Wait() => default;

    public static Intent Play(Card card, Vector2Int target) => new() { card = card, target = target };

    public override string ToString() =>
        IsWait ? "Wait" : $"{card.cardName} at {target}";
}
