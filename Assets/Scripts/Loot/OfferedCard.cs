using UnityEngine;

/// <summary>
/// Spawns one CardViewer for a choice screen - the sequence RewardPanel and CardRemovalPanel both
/// need and neither should repeat: Instantiate, Setup with a fresh runtime Card, PresentAt so it rests
/// at the right size and layer instead of snapping back to hand behaviour on hover-exit, and a
/// clickOverride so CardPlayManager (which only accepts clicks from cards ActiveHandViewer knows
/// about) never sees the click at all.
/// </summary>
public static class OfferedCard
{
    /// Builds a fresh Card from `data` - the offer-screen path, where every candidate is a CardData the
    /// player has not been dealt a copy of yet.
    public static CardViewer Spawn(
        CardViewer prefab,
        CardData data,
        Vector3 position,
        int order,
        float restScale,
        float hoverScale,
        System.Action<CardViewer> onClick)
    {
        return Spawn(prefab, new Card(data), position, order, restScale, hoverScale, onClick);
    }

    /// Presents an existing runtime Card instead of building a new one - the browse-a-pile path, where
    /// the whole point is to show the character's own copies (cooldown/dormant state included) rather
    /// than a fresh stand-in.
    public static CardViewer Spawn(
        CardViewer prefab,
        Card card,
        Vector3 position,
        int order,
        float restScale,
        float hoverScale,
        System.Action<CardViewer> onClick)
    {
        CardViewer viewer = Object.Instantiate(prefab, position, Quaternion.identity);

        viewer.Setup(card);
        viewer.PresentAt(SortingLayers.Overlay, order, restScale, hoverScale);
        viewer.clickOverride = onClick;

        return viewer;
    }
}
