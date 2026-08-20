using UnityEngine;

/// <summary>
/// The project's Unity layers, spelled once - the same contract SortingLayers holds for sorting
/// layers, and a different thing entirely. A Unity layer decides which *camera* draws an object; a
/// sorting layer decides the order it is drawn *within* one camera.
///
/// Only one layer here matters, and it matters a great deal: **Board**. The battle scene renders
/// through two stacked cameras - a board camera that moves and zooms to frame the level, and a fixed
/// UI camera for the cards and HUD - and they are told apart purely by culling mask. So an object's
/// layer is what decides which of the two draws it, and an object on the wrong layer does not
/// disappear, which would at least be obvious. It is drawn by the other camera, at that camera's
/// position and zoom, and lands somewhere else entirely on screen.
///
/// That is the failure this class exists to make hard to cause. `new GameObject(...)` starts on
/// Default no matter what it is parented to - **SetParent does not change layer** - so every
/// runtime-created child of a board object has to say so explicitly. See TileAuraOverlay.AttachTo,
/// which drew every totem's aura through the UI camera until it did.
/// </summary>
public static class GameLayers
{
    /// Added by Tools/Board/2 - Wire Cameras, which reads this same constant.
    public const string Board = "Board";

    /// Cached: LayerMask.NameToLayer is a string lookup, and this is asked once per projectile and
    /// once per floating damage number. -1 when the layer does not exist yet, which is a scene that
    /// has not been through Tools/Board/2 - Wire Cameras.
    private static int boardLayer = -2;

    public static int BoardLayer
    {
        get
        {
            // -2 is "not looked up yet", distinct from NameToLayer's own -1 for "no such layer" - so a
            // scene genuinely missing the layer is not re-queried on every projectile.
            if (boardLayer == -2) { boardLayer = LayerMask.NameToLayer(Board); }

            return boardLayer;
        }
    }

    /// <summary>
    /// Puts a runtime-created object on the board layer, for the ones with no board parent to inherit
    /// from - a projectile in flight, a floating damage number.
    ///
    /// Does nothing when the layer is missing rather than writing -1, which Unity rejects. A scene
    /// that has not been wired yet then behaves exactly as it did before the split.
    /// </summary>
    public static void PutOnBoard(GameObject go)
    {
        if (go == null || BoardLayer < 0) { return; }

        go.layer = BoardLayer;
    }
}
