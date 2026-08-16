using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The grid layout CardRemovalPanel and CardPilePanel both need: lay a list of Card out in rows and
/// columns around an anchor, spawn a CardViewer per card, and hand the caller its spawned list back to
/// destroy on close. Pulled out of CardRemovalPanel.SpawnGrid so a second, read-only screen (browsing
/// the draw/discard pile) does not have to re-derive the same row/column maths.
///
/// Takes real Card instances, not CardData - a browse screen shows the character's actual runtime
/// copies, cooldown badges and all, not a fresh Card per entry the way an offer screen does.
/// </summary>
public static class CardGridView
{
    /// <summary>
    /// Builds the grid and appends every spawned viewer to `spawned`.
    ///
    /// `onClick` is passed straight through as each viewer's clickOverride index callback - null means
    /// a purely informational grid, since OfferedCard.Spawn already treats a null onClick as "let
    /// CardPlayManager see it", which no card in a grid like this should ever do. Passing an
    /// (index) => {} no-op keeps every card visually clickable-inert without going through
    /// CardPlayManager either, so callers that want a truly inert grid should pass a no-op rather than
    /// null.
    ///
    /// `isEnabled`, if given, is asked per index before a card is wired up: false greys it out
    /// (CardViewer.SetPlayable) and leaves its clickOverride null, so the click falls through to
    /// CardPlayManager exactly like a null onClick already does - inert, not an exception. Null (the
    /// default) leaves every card enabled and unstyled, which is every existing caller's behaviour
    /// unchanged. What CardRemovalPanel reads to grey out cards a choice does not apply to - a card with
    /// no upgradedForm on the upgrade screen, say - while keeping every index aligned with the deck list
    /// the same way a null CardData gap already does.
    ///
    /// `maxHeight` scales cellWidth/cellHeight/cardScale down together, once, if the grid would
    /// otherwise run taller than the camera frame - the same shape stays, just smaller, rather than
    /// running off screen with no scroll or page. 0 disables the clamp.
    /// </summary>
    public static void Build(
        IReadOnlyList<Card> cards,
        CardViewer prefab,
        Transform anchor,
        int columns,
        float cellWidth,
        float cellHeight,
        float cardScale,
        float cardHoverScale,
        float maxHeight,
        Action<int> onClick,
        List<CardViewer> spawned,
        Func<int, bool> isEnabled = null)
    {
        if (prefab == null || anchor == null || cards == null) { return; }

        int count = cards.Count;
        if (count == 0) { return; }

        int cols = Mathf.Max(1, columns);
        int rows = Mathf.CeilToInt(count / (float)cols);

        float fit = 1f;
        if (maxHeight > 0f && rows * cellHeight > maxHeight)
        {
            fit = maxHeight / (rows * cellHeight);
        }

        float fittedCellWidth = cellWidth * fit;
        float fittedCellHeight = cellHeight * fit;
        float fittedCardScale = cardScale * fit;

        float startX = -(cols - 1) * fittedCellWidth / 2f;
        float startY = (rows - 1) * fittedCellHeight / 2f;

        for (int i = 0; i < count; i++)
        {
            Card card = cards[i];

            if (card == null) { continue; }

            int col = i % cols;
            int row = i / cols;

            Vector3 position = anchor.position
                + new Vector3(startX + col * fittedCellWidth, startY - row * fittedCellHeight, 0f);

            // Captured per-iteration on purpose - `i` is reassigned every loop, `chosenIndex` is not.
            int chosenIndex = i;
            bool enabled = isEnabled == null || isEnabled(chosenIndex);

            Action<CardViewer> callback = onClick != null && enabled ? _ => onClick(chosenIndex) : null;

            CardViewer viewer = OfferedCard.Spawn(
                prefab, card, position, i, fittedCardScale, cardHoverScale, callback);

            viewer.RefreshLockCounter();

            if (isEnabled != null) { viewer.SetPlayable(enabled); }

            spawned.Add(viewer);
        }
    }
}
