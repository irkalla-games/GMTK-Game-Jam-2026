/// <summary>
/// The names of the project's sorting layers, back to front.
///
/// These have to match the rows under Project Settings -> Tags and Layers -> Sorting Layers exactly.
/// Unity looks a layer up by string at runtime and silently does nothing on a name it does not know,
/// so a typo here is a renderer that quietly stays wherever it was - hence one spelling, in one place.
///
/// The stack, and what belongs in each:
///
///     Background   the flat backdrop at order 0, the tilemap the board sits on at order 10
///     Grid         the board's tiles and their highlights
///     Characters   character sprites, their overhead canvases
///     Cards        cards resting in hand, ordered by their index in the hand
///     UI           the HUD - turn counter, mana counter, End Turn
///     CardHover    the one card being hovered, lifted above everything but a modal
///     Overlay      the notification modal
///
/// Order within a layer is a small number - 0, 1, 2 - meaning "on top of the thing before it in this
/// layer", never a global position. That is the whole point of splitting the layers out: a card no
/// longer has to know what number the grid picked.
///
/// Two things in the same layer on the same order have no defined order between them - Unity falls
/// back to distance, which for a flat 2D scene is a coin toss. The map and the backdrop both sitting
/// at Background/0 is exactly how the tilemap ended up behind the blue screen once. If two things can
/// overlap, give them different orders.
///
/// Do not reorder or delete these. The layer's position in that settings list is the render order,
/// and its id is written into every prefab and scene that uses it - deleting one drops every asset
/// referencing it back to Default without a warning. Append new layers at the end.
/// </summary>
public static class SortingLayers
{
    public const string Background = "Background";

    public const string Grid = "Grid";

    public const string Characters = "Characters";

    public const string Cards = "Cards";

    public const string UI = "UI";

    public const string CardHover = "CardHover";

    public const string Overlay = "Overlay";
}
