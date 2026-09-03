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
///
/// The values are tuned to the Sharp GUI art in Assets/SharpUI/Textures - a warm near-black fill under
/// a copper edge, rather than the blue-grey scheme this held before. The sprites themselves live in
/// SharpSkin (Editor-only), because resolving a path needs AssetDatabase and this file is runtime code.
/// </summary>
public static class PanelPalette
{
    // ---- Chrome -----------------------------------------------------------------------------
    // Used to tint Images that have no sprite of their own - rules, backdrops, and the nested-Image
    // border trick in TooltipPanelWiring. An Image that IS given one of SharpSkin's sprites is tinted
    // white instead, so the art shows as authored; see SharpSkin.ApplySliced.
    public static readonly Color PanelFill = new(0.1412f, 0.1098f, 0.0902f);   // #241C17
    public static readonly Color PanelBorder = new(0.4196f, 0.2667f, 0.1373f); // #6B4423
    public static readonly Color HeaderFill = new(0.2275f, 0.1686f, 0.1255f);  // #3A2B20
    public static readonly Color SectionRule = new(0.2902f, 0.2118f, 0.1490f); // #4A3626

    // ---- Type ---------------------------------------------------------------------------------
    public static readonly Color Gold = new(1f, 0.8314f, 0.4784f);           // #FFD47A - header + term names
    public static readonly Color Ink = new(0.9294f, 0.9020f, 0.8667f);       // #EDE6DD - stat values
    public static readonly Color BodyInk = new(0.7373f, 0.6824f, 0.6235f);   // #BCAE9F - term/status bodies
    public static readonly Color LabelGrey = new(0.5490f, 0.4824f, 0.4157f); // #8C7B6A - RANGE / STATUS EFFECTS

    // ---- Button states ------------------------------------------------------------------------
    // Read by SharpButtonTint. The Button* colours MULTIPLY the button's sprite, so they sit at or
    // near white - a dark tint here would darken the art rather than light it, which is the trap of
    // reusing the chrome colours above for this. The Label* colours are used directly: TMP text has
    // no sprite to multiply.
    public static readonly Color ButtonNormal = Color.white;
    public static readonly Color ButtonHover = new(1f, 0.8510f, 0.7059f);      // #FFD9B4
    public static readonly Color ButtonPressed = new(0.6196f, 0.5412f, 0.4706f); // #9E8A78
    public static readonly Color ButtonDisabled = new(0.4196f, 0.3882f, 0.3608f); // #6B635C

    public static readonly Color LabelNormal = new(0.9294f, 0.9020f, 0.8667f);   // #EDE6DD - matches Ink
    public static readonly Color LabelHover = new(1f, 0.8314f, 0.4784f);         // #FFD47A - matches Gold
    public static readonly Color LabelPressed = new(0.7882f, 0.7373f, 0.6667f);  // #C9BCAA
    public static readonly Color LabelDisabled = new(0.4314f, 0.3922f, 0.3529f); // #6E645A

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

    // Party sheet columns. Widened from the original 260 (+30%, then a further +20%) while the height
    // stays where it was: the pressure was horizontal, not vertical - equipment names and status bodies
    // were wrapping in a narrow column, and paging (see PartySheetColumn) already solved the vertical
    // crowding that would otherwise argue for a taller card.
    //
    // Four heroes at this width plus ColumnSpacing is 1744 of the 1920 frame, leaving 88px either side.
    // That is the practical ceiling for this pair of numbers: widening further, or adding a fifth party
    // member, needs ColumnSpacing to come down first or the outer columns start leaving the frame -
    // PartySheetPanel.Refresh centres the row and does not clamp it.
    public const float ColumnWidth = 406f;
    public const float ColumnHeight = 640f;

    /// Gap between columns on the party sheet. Read by both PartySheetPanel (which lays the columns
    /// out) and PartySheetStyling (which writes it back into the scene), so the two cannot drift.
    public const float ColumnSpacing = 40f;

    /// <summary>
    /// Portrait size on a party sheet column. Deliberately NOT scaled with ColumnWidth: the portrait is
    /// square, so widening it also makes it taller, and the column's height did not grow - every pixel
    /// it gained would come straight out of the status list below it.
    /// </summary>
    public const float ColumnPortraitSize = 96f;

    /// The page arrows flanking a column's section label.
    public const float PageArrowSize = 30f;

    // ---- Settings rows ------------------------------------------------------------------------
    public const float SettingsPanelWidth = 720f;
    public const float SettingsRowHeight = 66f;
    public const float SettingsRowSpacing = 6f;
    public const float SettingsLabelColumn = 220f;
    public const float SettingsGroupLabelSize = 14f;
    public const float SettingsRowLabelSize = 22f;

    /// Text INSIDE a widget - a dropdown's caption and its list items. Larger than the row's own
    /// caption on purpose: the caption is a quiet label for a control, and the control's value is
    /// the thing being read. Not derived from SettingsRowLabelSize, because the two moved in
    /// opposite directions once the rows were tightened up.
    public const float WidgetTextSize = 24f;

    /// Inner padding of a settings row: horizontal, then vertical. Vertical is what decides how
    /// much of SettingsRowHeight a widget actually gets.
    public const int SettingsRowPaddingH = 16;
    public const int SettingsRowPaddingV = 7;

    /// Height a widget gets inside a settings row, once padding is taken off.
    public const float WidgetHeight = SettingsRowHeight - (SettingsRowPaddingV * 2);

    /// <summary>
    /// The narrowest a control in a settings row may be squeezed to.
    ///
    /// Without it a row in a narrow container hands the caption its full SettingsLabelColumn and
    /// leaves the control whatever is left - which in a 300px detail pane was 36 pixels of button.
    /// A minWidth here inverts that: the CAPTION gives way, because a truncated label is still
    /// readable and a 36px button is not usable at all.
    /// </summary>
    public const float WidgetMinWidth = 130f;

    /// <summary>
    /// How wide a dropdown wants to be.
    ///
    /// Without a preferred width a dropdown has flexibleWidth 1 and swallows the whole row - which
    /// in the 792px browser column meant a Class filter stretching most of the panel for the sake
    /// of the words "All classes". Capped here so the control is the size of its content rather
    /// than the size of its container.
    /// </summary>
    public const float WidgetPreferredWidth = 240f;

    // Action buttons - the ones that DO something rather than hold a value. Shorter than a
    // settings row and with larger type: a row carrying a slider needs height for the slider,
    // and a button carrying two words does not.
    public const float ActionRowHeight = 64f;
    /// Matches the Back button's 52, which is the size the rest of the panel is tuned against.
    public const float ActionButtonHeight = 52f;
    public const float ActionButtonLabelSize = 22f;

    // ---- Area figure ----------------------------------------------------------------------------
    public const float FigureCellSize = 22f;
    public const float FigureCellGap = 3f;
    public const float FigureCasterInset = 3f;

    // FigureEmpty is warmed to sit on the new chrome, but Hit/Aim/Caster are deliberately left as they
    // were: those three encode what a cell MEANS, and re-tinting them toward copper would cost the
    // contrast that makes an area figure readable at a glance.
    public static readonly Color FigureEmpty = new(0.1725f, 0.1373f, 0.1137f);  // #2C231D
    public static readonly Color FigureHit = new(0.7529f, 0.2706f, 0.2275f);    // #C0453A
    public static readonly Color FigureAim = new(0.7882f, 0.6039f, 0.1412f);    // #C99A24
    public static readonly Color FigureCasterRim = new(0.4353f, 0.6980f, 0.8627f); // #6FB2DC
    public static readonly Color FigureCasterFill = new(0.1843f, 0.4196f, 0.5569f); // #2F6B8E
}
