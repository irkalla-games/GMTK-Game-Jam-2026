using UnityEngine;
using UnityEngine.InputSystem;
using DG.Tweening;

/// Click a card to select it, then click a tile to play it there.
public class CardPlayManager : Singleton<CardPlayManager>
{
    private CardViewer selected;

    public bool HasSelection => selected != null;

    /// Which card is armed, or null. Exposed so the tutorial can tell "they have picked up the card I
    /// asked for" apart from "they have picked up some other card" - see TutorialDirector.
    public Card SelectedCard => selected != null ? selected.card : null;

    /// Cancels a pending card selection without playing it - the same effect Escape already has.
    /// Exposed for anything that switches the active character out from under a selected card, e.g.
    /// PartyPortraitPanel, where leaving the selection pointing at a hand that just left the screen
    /// would be a bug rather than a feature.
    public void ClearSelection() => Deselect();

    public void OnCardClicked(CardViewer cardViewer)
    {
        // A reward card routes its own click straight to RewardPanel (see CardViewer.clickOverride)
        // rather than through here, but a hand card is still clickable underneath the panel unless
        // this checks too - InputLocked is the same gate BattleManager.OnTileClicked uses.
        if (BattleManager.Instance != null && BattleManager.Instance.InputLocked)
        {
            Debug.Log($"card clicked: {Name(cardViewer)} - ignored, a reward panel is up");
            return;
        }

        // A card mid-discard keeps its collider live until its fly-away tween finishes and destroys it.
        if (cardViewer == null || !ActiveHandViewer.Instance.Contains(cardViewer))
        {
            Debug.Log($"card clicked: {Name(cardViewer)} - ignored, not in hand (already discarded?)");
            return;
        }

        // The tutorial permits one card per step, from the same declaration its spotlight hole came from
        // - see TutorialDirector. Null whenever no tutorial is driving.
        string tutorialRefusal = TutorialDirector.RefuseCard(cardViewer.card);

        if (tutorialRefusal != null)
        {
            Debug.Log($"card clicked: {Name(cardViewer)} - ignored, {tutorialRefusal}");
            return;
        }

        if (selected == cardViewer)
        {
            Debug.Log($"card clicked: {Name(cardViewer)} - deselecting");
            Deselect();
            return;
        }

        Debug.Log($"card clicked: {Name(cardViewer)} (cost {cardViewer.card.cost}) - selecting");
        Select(cardViewer);
    }

    /// Plays the selected card onto this tile. BattleManager routes tile clicks here once it knows a
    /// card is selected; with nothing selected a tile click means something else entirely.
    public void PlaySelectedOn(GridTile tile)
    {
        if (tile == null || !HasSelection) { return; }

        CardViewer cardViewer = selected;
        Card card = cardViewer.card;

        // Energy belongs to the character, and the card came out of that character's hand, so there is
        // nobody to pay the cost until one has been clicked.
        Character actor = BattleManager.Instance.ActiveCharacter;

        if (actor == null)
        {
            Debug.Log($"tile clicked: {tile.Coordinates} with {card.cardName} - no active character to pay the cost");
            cardViewer.transform.DOShakePosition(0.25f, 0.15f);
            return;
        }

        // Actor-state rules, not targeting ones - Frozen, Cooldown and the cost. The answer does not
        // depend on the tile, so none of it belongs in Card.Refusal. Above the commit point, so a
        // frozen click costs nothing. This is the same call the card highlight is built from, which
        // is what stops a card from ever looking playable and then refusing this click.
        string playRefusal = card.PlayRefusal(actor);

        if (playRefusal != null)
        {
            Debug.Log($"tile clicked: {tile.Coordinates} with {card.cardName} - {playRefusal}");
            cardViewer.transform.DOShakePosition(0.25f, 0.15f);
            return;
        }

        // Targeting is settled here, and only here. Past the commit point below the energy is gone and
        // the card is in the discard pile, so a rule that refuses any later refuses at a price.
        string refusal = card.Refusal(actor, tile);

        if (refusal != null)
        {
            Debug.Log($"tile clicked: {tile.Coordinates} with {card.cardName} - {refusal}");
            cardViewer.transform.DOShakePosition(0.25f, 0.15f);
            return;
        }

        Debug.Log($"tile clicked: {tile.Coordinates} - playing {card.cardName} as {actor.name}, occupant {(tile.Occupant != null ? tile.Occupant.name : "none")}");

        selected = null;
        ClearHighlights();
        actor.SpendEnergy(card.cost);
        card.ResolveEffects(actor, tile);

        // Into the actor's own discard pile - the card came out of that character's hand.
        // ActiveHandViewer is listening for Character.CardDiscarded and removes the viewer itself.
        actor.Discard(card);
    }


    /// <summary>
    /// Refreshes the selected card's two aiming previews against whichever tile the cursor is now over
    /// - the red area footprint, and the yellow projected-loss preview on every health bar it would
    /// actually hit. BattleManager.OnTileHovered is hover's single door, same as OnTileClicked is for
    /// clicks. With nothing selected, or the cursor over nothing, GridManager clears both previews on
    /// its own.
    /// </summary>
    public void RefreshAimPreviews(GridTile hovered)
    {
        if (GridManager.Instance == null) { return; }

        Character actor = BattleManager.Instance != null ? BattleManager.Instance.ActiveCharacter : null;
        Card card = HasSelection ? selected.card : null;

        GridManager.Instance.ShowAreaPreview(card, actor, hovered);
        GridManager.Instance.ShowDamagePreview(card, actor, hovered);
    }

    private static string Name(CardViewer cardViewer) =>
        cardViewer != null && cardViewer.card != null ? cardViewer.card.cardName : "null";

    private void Select(CardViewer cardViewer)
    {
        if (HasSelection) { selected.SetSelected(false); }

        selected = cardViewer;
        cardViewer.SetSelected(true);
        StartCoroutine(ActiveHandViewer.Instance.Relayout());

        if (GridManager.Instance != null)
        {
            GridManager.Instance.ShowPlayableTiles(cardViewer.card, BattleManager.Instance.ActiveCharacter);

            // Switching cards without the mouse moving would otherwise leave the old card's aiming
            // previews lit until the next hover event - nobody will refresh them, since nothing moved.
            GridManager.Instance.ClearAreaPreview();
            GridManager.Instance.ClearDamagePreview();
        }
    }

    private void Deselect()
    {
        if (!HasSelection) { return; }

        selected.SetSelected(false);
        selected = null;
        ClearHighlights();
        StartCoroutine(ActiveHandViewer.Instance.Relayout());
    }

    private static void ClearHighlights()
    {
        if (GridManager.Instance == null) { return; }

        GridManager.Instance.ClearPlayableTiles();
        GridManager.Instance.ClearAreaPreview();
        GridManager.Instance.ClearDamagePreview();
    }

    private void Update()
    {
        if (!HasSelection) { return; }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Deselect();
            return;
        }

        if (Mouse.current == null) { return; }

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            Deselect();
            return;
        }

        // Left click that hit no collider at all. Checking for "hit nothing" keeps this from racing
        // OnMouseDown - a click on a card or a tile always hits something.
        if (Mouse.current.leftButton.wasPressedThisFrame && ClickedEmptySpace())
        {
            Deselect();
        }
    }

    /// <summary>
    /// Whether the click landed on nothing at all - no card, no tile.
    ///
    /// Both cameras have to be asked, not just one. The board and the UI are rendered by two
    /// different cameras at two different zooms (see SceneCameras), so one screen position maps to
    /// two *different* world points: the one a tile would be at, and the one a card would be at.
    /// Testing only one of them would read a click on a card as empty space the moment the board
    /// camera zoomed to anything but the UI camera's fixed size, and silently deselect.
    /// </summary>
    private bool ClickedEmptySpace()
    {
        Vector2 screen = Mouse.current.position.ReadValue();

        return !HitsSomething(SceneCameras.Board, screen)
               && !HitsSomething(SceneCameras.Ui, screen);
    }

    private static bool HitsSomething(Camera cam, Vector2 screen)
    {
        if (cam == null) { return false; }

        return Physics2D.OverlapPoint(cam.ScreenToWorldPoint(screen)) != null;
    }
}
