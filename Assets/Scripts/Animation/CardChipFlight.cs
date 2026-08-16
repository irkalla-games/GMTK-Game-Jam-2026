using System.Collections;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// Flies a handful of small, faceless card-back sprites from one world point to another - what a
/// reshuffle looks like. Modelled directly on Projectile.Travel's "spawn a bare SpriteRenderer, tween
/// it, destroy it" shape, but for several at once with a stagger between them, and no rotation - a
/// card chip has an obvious up, a projectile does not.
///
/// Deliberately not CardViewer instances. A CardViewer is eleven renderers deep (see its own Awake);
/// animating a dozen of them for a half-second flourish that shows no card identity anyway would cost
/// far more than the moment is worth. One sprite per chip is the whole point.
/// </summary>
public static class CardChipFlight
{
    /// Sits above the pile buttons and the HUD but stays out of CardHover/Overlay, so a reshuffle never
    /// draws over a card actually being hovered or a modal actually open.
    private const int SortingOrder = 50;

    /// <summary>
    /// Spawns `count` chips (capped by the caller - see CardPileHud.maxChips) and sends them from
    /// `from` to `to` with `stagger` seconds between each one's start. Yields until the last one lands,
    /// so a caller sequencing a deal queue around this waits for the whole flourish, not just the
    /// first chip.
    /// </summary>
    public static IEnumerator Play(Sprite sprite, Vector3 from, Vector3 to, int count, float scale,
                                    float duration, float stagger)
    {
        if (sprite == null || count <= 0) { yield break; }

        if (duration <= 0f) { duration = 0.3f; }
        if (scale <= 0f) { scale = 1f; }

        for (int i = 0; i < count; i++)
        {
            SpawnChip(sprite, from, to, scale, duration);

            if (i < count - 1) { yield return new WaitForSeconds(stagger); }
        }

        // The last chip spawned still has `duration` left to fly when the loop above exits.
        yield return new WaitForSeconds(duration);
    }

    private static void SpawnChip(Sprite sprite, Vector3 from, Vector3 to, float scale, float duration)
    {
        GameObject go = new GameObject("CardChip");
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerName = SortingLayers.Cards;
        renderer.sortingOrder = SortingOrder;
        go.transform.position = from;
        go.transform.localScale = Vector3.one * scale;

        go.transform.DOMove(to, duration).SetEase(Ease.InOutQuad);
        go.transform.DOScale(scale * 0.6f, duration).SetEase(Ease.InQuad);

        Object.Destroy(go, duration);
    }
}
