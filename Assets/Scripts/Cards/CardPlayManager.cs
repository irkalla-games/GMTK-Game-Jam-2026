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

    /// Cancels a pending card selection without playing it - the same effect Backspace already has.
    /// (Backspace, not Escape: Escape opens the pause menu, which is one key with one meaning.)
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
            ShowRefusalHint(card, actor, tile, refusal);
            return;
        }

        Debug.Log($"tile clicked: {tile.Coordinates} - playing {card.cardName} as {actor.name}, occupant {(tile.Occupant != null ? tile.Occupant.name : "none")}");

        selected = null;
        ClearHighlights();
        actor.SpendEnergy(card.cost);
        card.ResolveEffects(actor, tile);

        // After ResolveEffects, not before - the rotation the player dialled in is exactly what has
        // to land, so it must still be readable through card.AimOctant for that call. Reset now,
        // rather than left for the next Select, so a card played again while still on cooldown (or
        // simply picked up again later) starts from its default orientation.
        card.ResetAim();

        // Into the actor's own discard pile - the card came out of that character's hand.
        // ActiveHandViewer is listening for Character.CardDiscarded and removes the viewer itself.
        //
        // DiscardPlayed rather than Discard: this is the play path, and a Rebound card is only supposed
        // to come back when it was played rather than every time it leaves hand - see that method.
        actor.DiscardPlayed(card);
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
        // Remembered so the E-key rotate handler can re-run this against the same tile the cursor is
        // already parked over - without it, rotating with the mouse sitting still would leave the old
        // orientation lit until something else moved it, since nothing else would ever call back in.
        lastHovered = hovered;

        if (GridManager.Instance == null) { return; }

        Character actor = BattleManager.Instance != null ? BattleManager.Instance.ActiveCharacter : null;
        Card card = HasSelection ? selected.card : null;

        GridManager.Instance.ShowAreaPreview(card, actor, hovered);
        GridManager.Instance.ShowDamagePreview(card, actor, hovered);
        GridManager.Instance.ShowPushPreview(card, actor, hovered);
    }

    private GridTile lastHovered;

    /// World-unit cap height of the refusal label. A sentence rather than a damage number, so it is
    /// shorter than FloatingTextManager's own default to keep the line from overhanging its tile.
    private const float RefusalHintHeight = 0.28f;

    /// <summary>
    /// Pops a plain-language label over the clicked tile when a refusal is worth explaining, rather
    /// than only shaking the card and writing to the console where a player will never see it.
    ///
    /// Deliberately narrow: only the push refusal gets one. Every other reason is either already
    /// obvious on screen (out of range shows no highlight, an unaffordable card shows its cost) or
    /// phrased for a log rather than a player. Compared against the refusal the click actually
    /// produced, so a tile that is *also* out of range reports that instead of blaming the push.
    /// </summary>
    private static void ShowRefusalHint(Card card, Character actor, GridTile tile, string refusal)
    {
        if (FloatingTextManager.Instance == null) { return; }

        if (card.PushRefusal(actor, tile) != refusal) { return; }

        FloatingTextManager.Instance.Show(tile, "Too many enemies nearby!", PanelPalette.Gold,
                                          RefusalHintHeight);
    }

    private static string Name(CardViewer cardViewer) =>
        cardViewer != null && cardViewer.card != null ? cardViewer.card.cardName : "null";

    private void Select(CardViewer cardViewer)
    {
        if (HasSelection) { selected.SetSelected(false); }

        selected = cardViewer;
        cardViewer.SetSelected(true);
        cardViewer.card.ResetAim();
        StartCoroutine(ActiveHandViewer.Instance.Relayout());

        if (GridManager.Instance != null)
        {
            GridManager.Instance.ShowPlayableTiles(cardViewer.card, BattleManager.Instance.ActiveCharacter);

            // Switching cards without the mouse moving would otherwise leave the old card's aiming
            // previews lit until the next hover event - nobody will refresh them, since nothing moved.
            GridManager.Instance.ClearAreaPreview();
            GridManager.Instance.ClearDamagePreview();
            GridManager.Instance.ClearPushPreview();
        }

        if (AimHintLabel.Instance != null)
        {
            if (cardViewer.card.CanRotateAim) { AimHintLabel.Instance.Show("Press E to rotate"); }
            else { AimHintLabel.Instance.Hide(); }
        }
    }

    private void Deselect()
    {
        if (!HasSelection) { return; }

        selected.SetSelected(false);
        selected.card.ResetAim();
        selected = null;
        lastHovered = null;
        ClearHighlights();
        StartCoroutine(ActiveHandViewer.Instance.Relayout());

        if (AimHintLabel.Instance != null) { AimHintLabel.Instance.Hide(); }
    }

    private static void ClearHighlights()
    {
        if (GridManager.Instance == null) { return; }

        GridManager.Instance.ClearPlayableTiles();
        GridManager.Instance.ClearAreaPreview();
        GridManager.Instance.ClearDamagePreview();
        GridManager.Instance.ClearPushPreview();
    }

    private void Update()
    {
        if (!HasSelection) { return; }

        if (Keyboard.current != null && Keyboard.current.backspaceKey.wasPressedThisFrame)
        {
            Deselect();
            return;
        }

        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame && selected.card.CanRotateAim)
        {
            // The first turn of a session starts from the facing currently on screen, which depends on
            // where the caster stands and which tile is under the cursor - hence both are handed over.
            Character actor = BattleManager.Instance != null ? BattleManager.Instance.ActiveCharacter : null;

            selected.card.RotateAim(actor, lastHovered);

            // The locked facing moves the footprint, which changes which tiles the push can actually
            // clear - so the green highlight has to be rebuilt, not just the previews. Leaving it
            // would let it promise a tile the very next click refuses, which is the one thing
            // Card.Refusal and ShowPlayableTiles exist together to prevent.
            if (GridManager.Instance != null) { GridManager.Instance.ShowPlayableTiles(selected.card, actor); }

            RefreshAimPreviews(lastHovered);
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
