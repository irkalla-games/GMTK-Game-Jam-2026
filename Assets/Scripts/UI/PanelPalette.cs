using UnityEngine;

/// <summary>
/// Every colour and size the new panel chrome uses - the tooltip and the party sheet column both
/// read from here, so the gold used for a term's name cannot drift between the two the way
/// TooltipManager.titleColor and Glossary.termColor used to.
///
/// A static class rather than a ScriptableObject: there is nothing here for a designer to author per
/// instance, and CLAUDE.md forbids writing to a ScriptableObject at runtime - a static class cannot be
/// written to at all, so it cannot become the next "fixed a value at runtime and silently edited the
/// asset" bug.
/// </summary>
public static class PanelPalette
{
    // ---- Chrome -----------------------------------------------------------------------------
    public static readonly Color PanelFill = new(0.0784f, 0.0902f, 0.1059f);   // #14171B
    public static readonly Color PanelBorder = new(0.2235f, 0.2588f, 0.2980f); // #39424C
    public static readonly Color HeaderFill = new(0.1059f, 0.1255f, 0.1490f);  // #1B2026
    public static readonly Color SectionRule = new(0.1490f, 0.1765f, 0.2039f); // #262D34

    // ---- Type ---------------------------------------------------------------------------------
    public static readonly Color Gold = new(1f, 0.8314f, 0.4784f);       // #FFD47A - header + term names
    public static readonly Color Ink = new(0.9020f, 0.9137f, 0.9255f);   // #E6E9EC - stat values
    public static readonly Color BodyInk = new(0.6627f, 0.7059f, 0.7451f); // #A9B4BE - term/status bodies
    public static readonly Color LabelGrey = new(0.4863f, 0.5333f, 0.5804f); // #7C8894 - RANGE / STATUS EFFECTS

    public const float HeaderTitleSize = 17f;
    public const float HeaderTitleSpacing = 11f;
    public const float StatLabelSize = 14f;
    public const float StatLabelSpacing = 10f;
    public const float StatValueSize = 18f;
    public const float SectionLabelSize = 14f;
    public const float SectionLabelSpacing = 12f;
    public const float TermNameSize = 15f;
    public const float TermNameSpacing = 11f;
    public const float TermBodySize = 17f;
    public const float FigureLegendSize = 14f;

    // ---- Layout -------------------------------------------------------------------------------
    public const float PanelWidth = 400f;
    public const float BorderThickness = 2f;
    public const float HeaderPaddingV = 16f;
    public const float HeaderPaddingH = 21f;
    public const float SectionPaddingV = 18f;
    public const float SectionPaddingH = 21f;
    public const float StatLabelColumn = 96f;
    public const float RowSpacing = 10f;
    public const float RuleHeight = 2f;

    public const float ColumnWidth = 260f;
    public const float ColumnHeight = 640f;

    // ---- Area figure ----------------------------------------------------------------------------
    public const float FigureCellSize = 22f;
    public const float FigureCellGap = 3f;
    public const float FigureCasterInset = 3f;

    public static readonly Color FigureEmpty = new(0.1373f, 0.1647f, 0.1922f);  // #232A31
    public static readonly Color FigureHit = new(0.7529f, 0.2706f, 0.2275f);    // #C0453A
    public static readonly Color FigureAim = new(0.7882f, 0.6039f, 0.1412f);    // #C99A24
    public static readonly Color FigureCasterRim = new(0.4353f, 0.6980f, 0.8627f); // #6FB2DC
    public static readonly Color FigureCasterFill = new(0.1843f, 0.4196f, 0.5569f); // #2F6B8E
}
