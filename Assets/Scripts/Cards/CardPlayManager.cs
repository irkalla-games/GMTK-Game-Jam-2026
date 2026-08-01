using UnityEngine;
using UnityEngine.InputSystem;
using DG.Tweening;

/// Click a card to select it, then click a tile to play it there.
public class CardPlayManager : Singleton<CardPlayManager>
{
    private CardViewer selected;

    public bool HasSelection => selected != null;

    public void OnCardClicked(CardViewer cardViewer)
    {
        // A card mid-discard keeps its collider live until its fly-away tween finishes and destroys it.
        if (cardViewer == null || !ActiveHandViewer.Instance.Contains(cardViewer))
        {
            Debug.Log($"card clicked: {Name(cardViewer)} - ignored, not in hand (already discarded?)");
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

        // An actor-state rule, not a targeting one - the answer does not depend on the tile, so it
        // does not belong in Card.Refusal. Above the commit point, so a frozen click costs nothing.
        if (!actor.CanAct)
        {
            Debug.Log($"tile clicked: {tile.Coordinates} with {card.cardName} - {actor.name} cannot act");
            cardViewer.transform.DOShakePosition(0.25f, 0.15f);
            return;
        }

        if (!actor.CanAfford(card.cost))
        {
            Debug.Log($"tile clicked: {tile.Coordinates} with {card.cardName} - {actor.name} cannot afford {card.cost} (energy {actor.Energy})");
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
        if (GridManager.Instance != null) { GridManager.Instance.ClearPlayableTiles(); }
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

    private bool ClickedEmptySpace()
    {
        Camera cam = Camera.main;
        if (cam == null) { return false; }

        Vector3 world = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        return Physics2D.OverlapPoint(world) == null;
    }
}
