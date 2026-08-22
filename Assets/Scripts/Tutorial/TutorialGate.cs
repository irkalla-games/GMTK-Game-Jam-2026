using System;

/// <summary>
/// What the player is allowed to do right now, while the tutorial is driving.
///
/// Answers in the null-or-reason shape every other gate in this codebase uses - Card.Refusal,
/// CardEffect.Refusal, GridManager.MoveRefusal, Character.ActRefusal. Null means "no objection"; a
/// string is the reason, logged exactly the way BattleManager.OnTileClicked already logs its own
/// refusals.
///
/// **Not a blanket lock.** BattleManager.InputLocked exists for the case where a modal owns the whole
/// screen and nothing may be touched; this is the opposite shape, where exactly one thing may be touched
/// and everything else may not. Both are consulted, and both are *pulled* by the doors rather than
/// pushed by whoever raised them - see InputLocked's own doc comment for why one pushed bool cannot
/// remember that two things wanted it held.
///
/// **The permit is built from the same declaration the spotlight's holes are.** That is deliberate, and
/// it is the rule GridManager.ShowPlayableTiles already relies on: it builds the tile highlight from the
/// very same Card.Refusal call the click asks, which is what stops the highlight from ever promising a
/// tile that a click would then refuse. A tutorial whose lit thing and clickable thing came from two
/// declarations would drift apart the first time a step was edited, and the failure would be silent.
///
/// A card step permits the card *and* its legal targets at once, not one then the other. Two separate
/// permits would soft-lock the moment a player deselected mid-aim: the card click would be refused
/// because the step had moved on to expecting a tile, and the tile click would do nothing because no
/// card was selected any more. The copy still moves on in two beats - see TutorialDirector.
/// </summary>
public sealed class TutorialGate
{
    private Card card;

    private Predicate<GridTile> tiles;

    private Character activate;

    private bool endTurn;

    /// Every door open at once - the free-play tail of turn 3, where nothing further is scripted and
    /// the player survives the round however they like. Checked first by all four Refusal methods below.
    private bool allowAll;

    /// <summary>
    /// Replaces the whole permit. Every step calls this exactly once, so what is allowed can never be
    /// the union of this step's intent and some leftover of the last one's.
    /// </summary>
    public void Permit(
        Card allowedCard = null,
        Predicate<GridTile> allowedTiles = null,
        Character allowedActivate = null,
        bool allowEndTurn = false,
        bool allowAll = false)
    {
        card = allowedCard;
        tiles = allowedTiles;
        activate = allowedActivate;
        endTurn = allowEndTurn;
        this.allowAll = allowAll;
    }

    /// Nothing on the board may be touched - a read beat, where Continue is the only way on.
    public void PermitNothing() => Permit();

    public string CardRefusal(Card clicked)
    {
        if (allowAll) { return null; }
        if (card == null) { return "the tutorial is not asking for a card right now"; }

        return clicked == card ? null : $"the tutorial is asking for {card.cardName}";
    }

    public string TileRefusal(GridTile clicked)
    {
        if (allowAll) { return null; }
        if (tiles == null) { return "the tutorial is not asking for a tile right now"; }

        return clicked != null && tiles(clicked) ? null : "that is not the tile the tutorial is asking for";
    }

    public string ActivateRefusal(Character hero)
    {
        if (allowAll) { return null; }
        if (activate == null) { return "the tutorial is not asking you to switch hero right now"; }

        return hero == activate ? null : $"the tutorial is asking for {activate.name}";
    }

    public string EndTurnRefusal() =>
        allowAll || endTurn ? null : "the tutorial is not ready for you to end the turn";
}
