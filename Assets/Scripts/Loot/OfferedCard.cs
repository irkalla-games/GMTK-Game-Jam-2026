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
    public static CardViewer Spawn(
        CardViewer prefab,
        CardData data,
        Vector3 position,
        int order,
        float restScale,
        float hoverScale,
        System.Action<CardViewer> onClick)
    {
        CardViewer viewer = Object.Instantiate(prefab, position, Quaternion.identity);

        viewer.Setup(new Card(data));
        viewer.PresentAt(SortingLayers.Overlay, order, restScale, hoverScale);
        viewer.clickOverride = onClick;

        return viewer;
    }
}
