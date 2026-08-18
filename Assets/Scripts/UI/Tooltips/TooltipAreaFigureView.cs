using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The area-of-effect grid inside a tooltip section - a GridLayoutGroup of pooled cells plus a legend
/// line naming whichever roles the current TooltipFigure actually uses.
///
/// A grid of Images rather than a baked sprite: CardAreaIconBuilder bakes its card-face glyph because
/// that glyph is small, cached per card and never touches PanelPalette's colours. This diagram is
/// rebuilt on every hover and has to track those colours live, so a GridLayoutGroup of pooled cells -
/// the same shape TooltipSectionView already pools rows with - costs nothing extra to keep in sync.
/// </summary>
internal sealed class TooltipAreaFigureView
{
    public readonly RectTransform root;

    private readonly RectTransform grid;
    private readonly GridLayoutGroup gridLayout;
    private readonly TMP_Text legend;

    private readonly List<Image> cellBg = new();
    private readonly List<Image> cellInset = new();

    public TooltipAreaFigureView(Transform parent, TMP_FontAsset font)
    {
        root = TooltipViewBuilder.CreateRect("Figure", parent);

        VerticalLayoutGroup vlg = root.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = PanelPalette.RowSpacing;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;

        grid = TooltipViewBuilder.CreateRect("Grid", root);
        gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize = new Vector2(PanelPalette.FigureCellSize, PanelPalette.FigureCellSize);
        gridLayout.spacing = new Vector2(PanelPalette.FigureCellGap, PanelPalette.FigureCellGap);
        gridLayout.childAlignment = TextAnchor.UpperLeft;
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;

        legend = TooltipViewBuilder.CreateLabel(root, "Legend", font, PanelPalette.FigureLegendSize, 0f,
            PanelPalette.LabelGrey, FontStyles.Normal);
    }

    public void SetActive(bool active) => root.gameObject.SetActive(active);

    /// <summary>
    /// Cells are read row 0 first per TooltipFigure's own doc comment - row 0 north, column 0 west -
    /// which is exactly GridLayoutGroup's own child order for a fixed-column-count grid, so no
    /// coordinate flip is needed between the figure's geometry and the grid's sibling order.
    /// </summary>
    public void Bind(TooltipFigure figure)
    {
        gridLayout.constraintCount = figure.Span;

        bool casterUsed = false, aimUsed = false, hitUsed = false;

        for (int i = 0; i < figure.cells.Length; i++)
        {
            (Image bg, Image inset) = CellAt(i);
            TooltipCellRole role = figure.cells[i];

            bg.color = role switch
            {
                TooltipCellRole.Caster => PanelPalette.FigureCasterRim,
                TooltipCellRole.Aim => PanelPalette.FigureAim,
                TooltipCellRole.Hit => PanelPalette.FigureHit,
                _ => PanelPalette.FigureEmpty,
            };

            inset.gameObject.SetActive(role == TooltipCellRole.Caster);

            casterUsed |= role == TooltipCellRole.Caster;
            aimUsed |= role == TooltipCellRole.Aim;
            hitUsed |= role == TooltipCellRole.Hit;
        }

        for (int i = figure.cells.Length; i < cellBg.Count; i++) { cellBg[i].gameObject.SetActive(false); }

        legend.text = BuildLegend(casterUsed, aimUsed, hitUsed);
    }

    private (Image bg, Image inset) CellAt(int index)
    {
        while (cellBg.Count <= index)
        {
            Image bg = TooltipViewBuilder.CreateFill(grid, "Cell", PanelPalette.FigureEmpty);

            RectTransform insetRect = TooltipViewBuilder.CreateRect("Inset", bg.transform);
            insetRect.anchorMin = Vector2.zero;
            insetRect.anchorMax = Vector2.one;
            insetRect.offsetMin = new Vector2(PanelPalette.FigureCasterInset, PanelPalette.FigureCasterInset);
            insetRect.offsetMax = new Vector2(-PanelPalette.FigureCasterInset, -PanelPalette.FigureCasterInset);

            Image inset = insetRect.gameObject.AddComponent<Image>();
            inset.color = PanelPalette.FigureCasterFill;
            inset.raycastTarget = false;

            cellBg.Add(bg);
            cellInset.Add(inset);
        }

        cellBg[index].gameObject.SetActive(true);
        cellBg[index].transform.SetSiblingIndex(index);

        return (cellBg[index], cellInset[index]);
    }

    /// "you / aim / hit" - lower-case, matching the spec's own legend rather than the upper-case
    /// treatment every label elsewhere in the panel gets. A legend is naming a colour swatch, not
    /// heading a section.
    private static string BuildLegend(bool caster, bool aim, bool hit)
    {
        List<string> words = new();

        if (caster) { words.Add("you"); }
        if (aim) { words.Add("aim"); }
        if (hit) { words.Add("hit"); }

        return string.Join("   ", words);
    }
}
