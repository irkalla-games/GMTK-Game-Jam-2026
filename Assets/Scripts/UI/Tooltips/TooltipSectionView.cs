using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One pooled section inside the tooltip panel's Body: an optional label, an ordered run of stat rows,
/// term blocks and at most one area figure, then a bottom rule. Built once per pool slot and reused
/// across renders - SetActive rather than Instantiate/Destroy on every hover, the same pooling
/// contract PartySheetColumn's own rows already use.
///
/// A plain class, not a MonoBehaviour: nothing here needs a Unity lifecycle callback, only a
/// GameObject to own. TooltipManager is the only caller, and the render sequence is always
/// BeginRender, then some mix of SetLabel/AddStat/AddTerm/SetFigure in the order they should appear,
/// then EndRender.
/// </summary>
internal sealed class TooltipSectionView
{
    public readonly RectTransform root;

    private readonly TMP_Text label;
    private readonly TooltipAreaFigureView figure;
    private readonly Image rule;
    private readonly TMP_FontAsset font;

    private readonly List<(RectTransform root, TMP_Text key, TMP_Text value)> statRows = new();
    private readonly List<(RectTransform root, TMP_Text name, TMP_Text body)> termRows = new();

    private int statsUsed;
    private int termsUsed;
    private int rowsPlaced;
    private bool figureUsed;

    public TooltipSectionView(RectTransform parent, TMP_FontAsset font)
    {
        this.font = font;

        root = TooltipViewBuilder.CreateRect("Section", parent);

        VerticalLayoutGroup vlg = root.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset((int)PanelPalette.SectionPaddingH, (int)PanelPalette.SectionPaddingH,
            (int)PanelPalette.SectionPaddingV, (int)PanelPalette.SectionPaddingV);
        vlg.spacing = PanelPalette.RowSpacing;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;

        // Created once, at fixed sibling index 0/last - only their active state changes on a render,
        // so they never need SetSiblingIndex the way the pooled rows below do.
        label = TooltipViewBuilder.CreateLabel(root, "Label", font, PanelPalette.SectionLabelSize,
            PanelPalette.SectionLabelSpacing, PanelPalette.LabelGrey, FontStyles.Bold | FontStyles.UpperCase);
        label.gameObject.SetActive(false);

        figure = new TooltipAreaFigureView(root, font);
        figure.SetActive(false);

        rule = TooltipViewBuilder.CreateRule(root, "Rule", PanelPalette.SectionRule);
    }

    public void SetActive(bool active) => root.gameObject.SetActive(active);

    public void BeginRender()
    {
        statsUsed = 0;
        termsUsed = 0;
        rowsPlaced = 0;
        figureUsed = false;
        label.gameObject.SetActive(false);
    }

    public void SetLabel(string text)
    {
        label.text = text;
        label.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    public void AddStat(string key, string value)
    {
        (RectTransform rowRect, TMP_Text k, TMP_Text v) = StatAt(statsUsed);
        statsUsed++;

        k.text = key;
        v.text = value;

        rowRect.gameObject.SetActive(true);
        rowRect.SetSiblingIndex(1 + rowsPlaced);
        rowsPlaced++;
    }

    public void AddTerm(string name, string body)
    {
        (RectTransform rowRect, TMP_Text n, TMP_Text b) = TermAt(termsUsed);
        termsUsed++;

        bool hasName = !string.IsNullOrEmpty(name);
        n.text = hasName ? name : string.Empty;
        n.gameObject.SetActive(hasName);
        b.text = body;

        rowRect.gameObject.SetActive(true);
        rowRect.SetSiblingIndex(1 + rowsPlaced);
        rowsPlaced++;
    }

    public void SetFigure(TooltipFigure fig)
    {
        figure.Bind(fig);
        figure.SetActive(true);
        figure.root.SetSiblingIndex(1 + rowsPlaced);
        rowsPlaced++;
        figureUsed = true;
    }

    /// `showRule` is false for whichever section renders last - the panel's own bottom edge is the
    /// closing line, so a rule there would double it.
    public void EndRender(bool showRule)
    {
        for (int i = statsUsed; i < statRows.Count; i++) { statRows[i].root.gameObject.SetActive(false); }
        for (int i = termsUsed; i < termRows.Count; i++) { termRows[i].root.gameObject.SetActive(false); }

        if (!figureUsed) { figure.SetActive(false); }

        rule.gameObject.SetActive(showRule);
        rule.transform.SetAsLastSibling();
    }

    private (RectTransform, TMP_Text, TMP_Text) StatAt(int index)
    {
        while (statRows.Count <= index)
        {
            RectTransform rowRect = TooltipViewBuilder.CreateRect("Stat", root);
            HorizontalLayoutGroup hlg = rowRect.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.UpperLeft;

            TMP_Text key = TooltipViewBuilder.CreateLabel(rowRect, "Key", font, PanelPalette.StatLabelSize,
                PanelPalette.StatLabelSpacing, PanelPalette.LabelGrey, FontStyles.Bold | FontStyles.UpperCase);
            LayoutElement keyElement = key.gameObject.AddComponent<LayoutElement>();
            keyElement.preferredWidth = PanelPalette.StatLabelColumn;
            keyElement.flexibleWidth = 0f;

            TMP_Text value = TooltipViewBuilder.CreateLabel(rowRect, "Value", font, PanelPalette.StatValueSize,
                0f, PanelPalette.Ink, FontStyles.Normal);
            LayoutElement valueElement = value.gameObject.AddComponent<LayoutElement>();
            valueElement.flexibleWidth = 1f;

            statRows.Add((rowRect, key, value));
        }

        return statRows[index];
    }

    private (RectTransform, TMP_Text, TMP_Text) TermAt(int index)
    {
        while (termRows.Count <= index)
        {
            RectTransform rowRect = TooltipViewBuilder.CreateRect("Term", root);
            VerticalLayoutGroup vlg = rowRect.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 3f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperLeft;

            TMP_Text name = TooltipViewBuilder.CreateLabel(rowRect, "Name", font, PanelPalette.TermNameSize,
                PanelPalette.TermNameSpacing, PanelPalette.Gold, FontStyles.Bold | FontStyles.UpperCase);

            TMP_Text body = TooltipViewBuilder.CreateLabel(rowRect, "Body", font, PanelPalette.TermBodySize,
                0f, PanelPalette.BodyInk, FontStyles.Normal);

            termRows.Add((rowRect, name, body));
        }

        return termRows[index];
    }
}
