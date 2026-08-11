using System.Collections;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// Spawns and flies a single travelling sprite from one world point to another, then cleans itself up.
///
/// A static helper rather than a prefab: the only thing that varies card to card is the sprite
/// (CueOverride.projectileSprite), and everything else - the renderer, the sorting, the tween, the
/// destroy - is identical every time. That is what keeps "the card holds the projectile art" literally
/// true, with nothing to wire up in a prefab and nothing that can drift out of sync with one.
/// </summary>
public static class Projectile
{
    /// Sits above every character (SortingGroup orders 0-7) and still under the Cards layer.
    private const int SortingOrder = 100;

    /// <summary>
    /// Flight time used when a CueOverride leaves travelDuration at 0 - the value a freshly added
    /// entry starts at. Zero is treated as "unset" rather than "arrive instantly" on purpose: an
    /// instant shot spawns and destroys itself inside one frame, so authoring a projectile sprite and
    /// nothing else would silently render nothing at all. Same convention as CueBinding.duration.
    /// </summary>
    private const float DefaultTravelDuration = 0.3f;

    /// <summary>
    /// Moves a new sprite from `from` to `to`, then destroys it. Yields until it arrives, so a
    /// CardAnimation can hold its caller's queue for exactly as long as the shot is in the air - see
    /// CardAnimation.Perform.
    ///
    /// By default the sprite turns to face the way it is travelling, which assumes the art points
    /// right (+X) at rest - the usual convention, and invisible either way on a round sprite like a
    /// fireball. `rotation` then nudges that facing, or becomes the whole of it when `lockRotation`
    /// stops the shot turning at all.
    ///
    /// `scale` at 0 means 1, the same "0 is unset" convention travelDuration and CueBinding.duration
    /// use - a fresh CueOverride starts every number at zero, and a shot scaled to nothing would be
    /// as invisible as one that arrived instantly.
    /// </summary>
    public static IEnumerator Travel(Sprite sprite, Vector3 from, Vector3 to, float duration,
                                      float scale = 0f, float rotation = 0f, bool lockRotation = false)
    {
        if (sprite == null) { yield break; }

        if (duration <= 0f) { duration = DefaultTravelDuration; }
        if (scale <= 0f) { scale = 1f; }

        GameObject go = new GameObject("Projectile");
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerName = SortingLayers.Characters;
        renderer.sortingOrder = SortingOrder;
        go.transform.position = from;
        go.transform.localScale = Vector3.one * scale;

        float degrees = rotation;

        if (!lockRotation)
        {
            Vector3 heading = to - from;

            if (heading.sqrMagnitude > 0f)
            {
                degrees += Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg;
            }
        }
        float averageCharacterHeight = 1.5f;
        go.transform.rotation = Quaternion.Euler(0f, 0f, degrees);
        Vector3 heightedTo = new Vector3(to.x, to.y + averageCharacterHeight/2, to.z);
        go.transform.DOMove(heightedTo, duration);
        yield return new WaitForSeconds(duration);

        Object.Destroy(go);
    }
}
