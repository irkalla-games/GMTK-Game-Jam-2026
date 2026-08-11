using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders a card's non-Single area entries into a small pixel-grid glyph - what CardViewer shows in
/// the corner of a card face so an area-of-effect card reads as one at a glance, without having to
/// pick it up and hover a tile first.
///
/// Pure geometry, no board: every entry is rendered as if the caster stood at a fixed virtual origin
/// and a PlayedTile entry were aimed one tile north of it. A Source entry renders centred on that same
/// origin instead - which is exactly how a real play would place the two relative to each other, so a
/// card mixing "buff myself in a radius" with "damage a cone at the target" draws both shapes in their
/// true relative arrangement, not two unrelated icons stitched together.
/// </summary>
public static class CardAreaIconBuilder
{
    private const int PixelsPerCell = 4;

    private static readonly Vector2Int VirtualCaster = Vector2Int.zero;

    /// One tile "north" of the caster - not a real compass direction, it only has to be consistent so
    /// every card's icon uses the same relative placement.
    private static readonly Vector2Int VirtualAim = new(0, 1);

    private static readonly Color CoveredColor = new(1f, 0.35f, 0.3f, 0.9f);
    private static readonly Color AnchorColor = new(1f, 0.9f, 0.3f, 0.95f);

    /// Null if every entry is Single or its effect does not SupportsArea - CardViewer draws no glyph
    /// at all then, which is what a single-target card should look like.
    public static Sprite Build(IReadOnlyList<CardEffectEntry> entries)
    {
        HashSet<Vector2Int> covered = new();
        HashSet<Vector2Int> anchors = new();
        int reach = 0;

        foreach (CardEffectEntry entry in entries)
        {
            if (entry.effect == null || entry.area.IsSingle || !entry.effect.SupportsArea) { continue; }

            Vector2Int aim = entry.aimsAt == EffectTarget.Source ? VirtualCaster : VirtualAim;
            anchors.Add(aim);

            // +1 for the aim tile's own offset from the origin, so a PlayedTile entry's furthest cell
            // never lands outside the box this reach ends up sizing the texture to.
            int entryReach = entry.area.MaxReach + 1;
            reach = Mathf.Max(reach, entryReach);

            for (int y = -entryReach; y <= entryReach; y++)
            {
                for (int x = -entryReach; x <= entryReach; x++)
                {
                    Vector2Int cell = aim + new Vector2Int(x, y);

                    if (entry.area.Covers(VirtualCaster, aim, cell)) { covered.Add(cell); }
                }
            }
        }

        return covered.Count > 0 ? Render(covered, anchors, reach) : null;
    }

    private static Sprite Render(HashSet<Vector2Int> covered, HashSet<Vector2Int> anchors, int reach)
    {
        int span = reach * 2 + 1;
        int size = span * PixelsPerCell;

        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        Color clear = new(0f, 0f, 0f, 0f);
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) { pixels[i] = clear; }

        foreach (Vector2Int cell in covered)
        {
            Paint(pixels, size, span, cell, reach, anchors.Contains(cell) ? AnchorColor : CoveredColor);
        }

        // The anchor always shows, even if the shape's own Covers check happens not to mark its own
        // centre true (a ring-shaped radius, say, whose minDistance excludes the tile it is measured
        // from).
        foreach (Vector2Int anchor in anchors)
        {
            if (!covered.Contains(anchor)) { Paint(pixels, size, span, anchor, reach, AnchorColor); }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), PixelsPerCell);
    }

    private static void Paint(Color[] pixels, int size, int span, Vector2Int cell, int reach, Color color)
    {
        // Grid space has +Y up; texture rows run bottom-to-top the same way, so no flip is needed.
        int cellX = cell.x + reach;
        int cellY = cell.y + reach;

        if (cellX < 0 || cellX >= span || cellY < 0 || cellY >= span) { return; }

        for (int py = 0; py < PixelsPerCell; py++)
        {
            for (int px = 0; px < PixelsPerCell; px++)
            {
                int x = cellX * PixelsPerCell + px;
                int y = cellY * PixelsPerCell + py;
                pixels[y * size + x] = color;
            }
        }
    }
}
