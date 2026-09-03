using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the rows a settings screen is made of - a group heading, and label-plus-widget rows for a
/// toggle, a slider and a dropdown.
///
/// Shared by the menu's Settings panel and the pause menu's, which are separate panels on purpose: the
/// in-game one is narrower and greys out what a run has already snapshotted. Separate LAYOUTS, one
/// definition of what a row looks like - the same split SettingsBinding draws for behaviour.
///
/// Deliberately not three prefab assets. A prefab would have to be found, version-controlled and kept
/// in step with the code that fills it, and both callers are Editor commands that can simply call these
/// methods. The rule the plan was reaching for - do not define a row twice - is satisfied either way.
///
/// The widgets themselves come from Unity's own DefaultControls / TMP_DefaultControls factories rather
/// than being assembled by hand. A TMP dropdown in particular is a ScrollRect, a viewport, a content
/// root, a mask and a template item; hand-building that is a large amount of code whose only virtue
/// would be being ours.
/// </summary>
public static class SettingsRowBuilder
{
    /// How tall one row of an open dropdown list is, and how much of the list shows at once.
    /// Both scale with WidgetTextSize - see DropdownRow.
    private const float DropdownItemHeight = 42f;
    private const float DropdownListHeight = 300f;

    /// Gap between a row s caption and its widget. Named because the non-stretch row width is
    /// computed from it - see DropdownRow.
    private const float RowSpacing = 16f;

    private static DefaultControls.Resources cachedResources;

    private static bool resourcesLoaded;

    /// <summary>
    /// The built-in sprites Unity's control factories expect. Loaded once - GetBuiltinExtraResource
    /// hits the asset database on every call, and a settings screen builds a dozen widgets.
    ///
    /// These are only what the widgets are BORN with; every one is re-skinned with SharpSkin art
    /// immediately afterwards. They exist so the factories produce a control that is structurally
    /// complete rather than one with null sprites.
    /// </summary>
    private static DefaultControls.Resources Resources()
    {
        if (resourcesLoaded) { return cachedResources; }

        cachedResources = new DefaultControls.Resources
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
            mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
        };

        resourcesLoaded = true;

        return cachedResources;
    }


    private static TMP_DefaultControls.Resources cachedTmpResources;

    private static bool tmpResourcesLoaded;

    /// <summary>
    /// The same built-in sprites again, in the struct TMP's own factory wants.
    ///
    /// TMP_DefaultControls.Resources is a distinct type from DefaultControls.Resources despite having
    /// identical fields, so the two cannot be shared and there is nothing to be gained by trying.
    /// </summary>
    private static TMP_DefaultControls.Resources TmpResources()
    {
        if (tmpResourcesLoaded) { return cachedTmpResources; }

        DefaultControls.Resources shared = Resources();

        cachedTmpResources = new TMP_DefaultControls.Resources
        {
            standard = shared.standard,
            background = shared.background,
            inputField = shared.inputField,
            knob = shared.knob,
            checkmark = shared.checkmark,
            dropdown = shared.dropdown,
            mask = shared.mask,
        };

        tmpResourcesLoaded = true;

        return cachedTmpResources;
    }
    /// <summary>
    /// A section heading - DISPLAY, AUDIO, GAME. Small, letter-spaced and grey, so it reads as a label
    /// for what follows rather than as another row.
    /// </summary>
    public static void GroupLabel(RectTransform body, string objectName, string text)
    {
        RectTransform row = SharpSkin.EnsureChild(body, objectName);

        TextMeshProUGUI label = SharpSkin.Ensure<TextMeshProUGUI>(row.gameObject);

        TMP_FontAsset font = SharpSkin.LoadFont();

        if (font != null) { label.font = font; }

        label.text = text.ToUpperInvariant();
        label.fontSize = PanelPalette.SettingsGroupLabelSize;
        label.characterSpacing = PanelPalette.SectionLabelSpacing;
        label.color = PanelPalette.LabelGrey;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;

        LayoutElement layout = SharpSkin.Ensure<LayoutElement>(row.gameObject);
        layout.minHeight = PanelPalette.SettingsRowHeight * 0.75f;
        layout.preferredHeight = PanelPalette.SettingsRowHeight * 0.75f;
        // Explicit 0 for the same reason every other row sets it - see Row. This one has no layout
        // group to inherit a stray 1 from today, but it sits in the same column as rows that do.
        layout.flexibleHeight = 0f;
        layout.flexibleWidth = 1f;

        EditorUtility.SetDirty(label);
    }

    /// <summary>
    /// The shell every row shares: a skinned background, a horizontal layout, and the caption on the
    /// left. Returns the rect the widget should be parented under.
    /// </summary>
    /// <summary>
    /// The shell every row shares.
    ///
    /// `labelWidth` 0 means the standard caption column. `stretch` false makes the row only as wide
    /// as its contents rather than filling its container - for a filter row sitting above a card
    /// grid, where a full-width background running far past a short dropdown reads as a mistake.
    /// </summary>
    private static RectTransform Row(RectTransform body, string objectName, string caption,
        out TMP_Text captionLabel, float labelWidth = 0f, bool stretch = true)
    {
        RectTransform row = SharpSkin.EnsureChild(body, objectName);

        Image background = SharpSkin.Ensure<Image>(row.gameObject);
        SharpSkin.ApplySliced(background, SharpSkin.Row);
        background.raycastTarget = false;

        HorizontalLayoutGroup layout = SharpSkin.Ensure<HorizontalLayoutGroup>(row.gameObject);
        layout.padding = new RectOffset(PanelPalette.SettingsRowPaddingH, PanelPalette.SettingsRowPaddingH,
            PanelPalette.SettingsRowPaddingV, PanelPalette.SettingsRowPaddingV);
        layout.spacing = RowSpacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        LayoutElement rowLayout = SharpSkin.Ensure<LayoutElement>(row.gameObject);
        rowLayout.minHeight = PanelPalette.SettingsRowHeight;
        rowLayout.preferredHeight = PanelPalette.SettingsRowHeight;
        // MUST be set explicitly, and 0 rather than left alone. LayoutUtility.GetLayoutProperty
        // SKIPS any component whose value is negative, so a LayoutElement left at the default -1
        // does not merely lose - it is never considered, and the next ILayoutElement on the object
        // answers instead. That is this row s own HorizontalLayoutGroup, which reports
        // flexibleHeight = 1 exactly because childForceExpandHeight is true above.
        //
        // The row then advertises "I will take spare vertical space" to its column, and four such
        // rows split a 314px surplus 78.5 each - which is how a row whose preferredHeight says 40
        // rendered at 118.5.
        rowLayout.flexibleHeight = 0f;
        // 1 fills the container, 0 leaves the row at its content width - which, with the
        // LayoutElement preferredWidth left unset, is what the HorizontalLayoutGroup computes
        // from the caption plus the widget.
        rowLayout.flexibleWidth = stretch ? 1f : 0f;

        RectTransform labelRect = SharpSkin.EnsureChild(row, "Label");
        captionLabel = SharpSkin.Ensure<TextMeshProUGUI>(labelRect.gameObject);

        TMP_FontAsset font = SharpSkin.LoadFont();

        if (font != null) { captionLabel.font = font; }

        captionLabel.text = caption;
        captionLabel.fontSize = PanelPalette.SettingsRowLabelSize;
        captionLabel.color = PanelPalette.Ink;
        captionLabel.alignment = TextAlignmentOptions.MidlineLeft;
        captionLabel.raycastTarget = false;

        // NoWrap: at 220px "Browse" was breaking to "Brows / e". A caption is one or two words
        // and should shorten with an ellipsis rather than grow a second line the row has no
        // height for.
        captionLabel.textWrappingMode = TextWrappingModes.NoWrap;
        captionLabel.overflowMode = TextOverflowModes.Ellipsis;

        LayoutElement labelLayout = SharpSkin.Ensure<LayoutElement>(labelRect.gameObject);
        labelLayout.preferredWidth = labelWidth > 0f ? labelWidth : PanelPalette.SettingsLabelColumn;
        labelLayout.flexibleWidth = 0f;

        // Anything an older version of this builder left in the row goes now - see
        // SharpSkin.PruneChildren. Callers add their widget after this returns, so the widget
        // name they are about to use has to survive; each passes its own.
        EditorUtility.SetDirty(captionLabel);

        return row;
    }

    /// <summary>
    /// A checkbox row. Unity's toggle factory ships a legacy UI.Text caption, which is deleted - the row
    /// already has its own TMP label, and leaving both would put the same words on screen twice.
    /// </summary>
    public static Toggle ToggleRow(RectTransform body, string objectName, string caption)
    {
        RectTransform row = Row(body, objectName, caption, out TMP_Text _);

        Toggle toggle = row.GetComponentInChildren<Toggle>(includeInactive: true);

        if (toggle == null)
        {
            GameObject created = DefaultControls.CreateToggle(Resources());
            created.name = "Toggle";
            created.transform.SetParent(row, worldPositionStays: false);
            toggle = created.GetComponent<Toggle>();

            Transform legacyLabel = created.transform.Find("Label");

            if (legacyLabel != null) { Object.DestroyImmediate(legacyLabel.gameObject); }
        }

        RectTransform toggleRect = (RectTransform)toggle.transform;
        toggleRect.sizeDelta = new Vector2(PanelPalette.WidgetHeight, PanelPalette.WidgetHeight);

        LayoutElement layout = SharpSkin.Ensure<LayoutElement>(toggle.gameObject);
        layout.preferredWidth = PanelPalette.WidgetHeight;
        layout.preferredHeight = PanelPalette.WidgetHeight;
        // No minWidth: a checkbox is square by definition, and 130 wide would stretch it into a bar.
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;

        Transform backgroundTransform = toggle.transform.Find("Background");

        if (backgroundTransform != null)
        {
            RectTransform backgroundRect = (RectTransform)backgroundTransform;
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;

            Image backgroundImage = backgroundTransform.GetComponent<Image>();
            SharpSkin.ApplySliced(backgroundImage, SharpSkin.Checkbox);
            toggle.targetGraphic = backgroundImage;

            Transform checkmark = backgroundTransform.Find("Checkmark");

            if (checkmark != null)
            {
                RectTransform checkRect = (RectTransform)checkmark;
                checkRect.anchorMin = Vector2.zero;
                checkRect.anchorMax = Vector2.one;
                checkRect.offsetMin = new Vector2(6f, 6f);
                checkRect.offsetMax = new Vector2(-6f, -6f);

                Image checkImage = checkmark.GetComponent<Image>();
                SharpSkin.ApplySliced(checkImage, SharpSkin.CheckMark, PanelPalette.Gold);
                toggle.graphic = checkImage;
            }
        }

        SharpSkin.PruneChildren(row, "Label", "Toggle");

        EditorUtility.SetDirty(toggle);

        return toggle;
    }

    /// <summary>
    /// A volume row. The fill and handle are skinned from the package's scroll art, which is the only
    /// track-and-knob pair it ships.
    /// </summary>
    public static Slider SliderRow(RectTransform body, string objectName, string caption)
    {
        RectTransform row = Row(body, objectName, caption, out TMP_Text _);

        Slider slider = row.GetComponentInChildren<Slider>(includeInactive: true);

        if (slider == null)
        {
            GameObject created = DefaultControls.CreateSlider(Resources());
            created.name = "Slider";
            created.transform.SetParent(row, worldPositionStays: false);
            slider = created.GetComponent<Slider>();
        }

        LayoutElement layout = SharpSkin.Ensure<LayoutElement>(slider.gameObject);
        layout.preferredHeight = PanelPalette.WidgetHeight - 8f;
        layout.minWidth = PanelPalette.WidgetMinWidth;
        layout.flexibleWidth = 1f;

        Transform background = slider.transform.Find("Background");

        if (background != null)
        {
            SharpSkin.ApplySliced(background.GetComponent<Image>(), SharpSkin.SliderTrack);
        }

        Transform fill = slider.transform.Find("Fill Area/Fill");

        if (fill != null)
        {
            SharpSkin.ApplySliced(fill.GetComponent<Image>(), SharpSkin.SliderFill, PanelPalette.Gold);
        }

        Transform handle = slider.transform.Find("Handle Slide Area/Handle");

        if (handle != null)
        {
            Image handleImage = handle.GetComponent<Image>();
            SharpSkin.ApplySliced(handleImage, SharpSkin.SliderKnob);
            slider.targetGraphic = handleImage;
        }

        SharpSkin.PruneChildren(row, "Label", "Slider");

        EditorUtility.SetDirty(slider);

        return slider;
    }

    /// <summary>
    /// A dropdown row, used for the resolution list.
    ///
    /// The template is the part worth knowing about: TMP_DefaultControls builds a hidden "Template"
    /// child that is cloned per option when the list opens, so the item row has to be skinned there
    /// rather than on anything currently visible.
    /// </summary>
    public static TMP_Dropdown DropdownRow(RectTransform body, string objectName, string caption,
        float labelWidth = 0f, bool stretch = true)
    {
        RectTransform row = Row(body, objectName, caption, out TMP_Text _);

        TMP_Dropdown dropdown = row.GetComponentInChildren<TMP_Dropdown>(includeInactive: true);

        if (dropdown == null)
        {
            GameObject created = TMP_DefaultControls.CreateDropdown(TmpResources());
            created.name = "Dropdown";
            created.transform.SetParent(row, worldPositionStays: false);
            dropdown = created.GetComponent<TMP_Dropdown>();
        }

        LayoutElement layout = SharpSkin.Ensure<LayoutElement>(dropdown.gameObject);
        layout.preferredHeight = PanelPalette.WidgetHeight;
        layout.minWidth = PanelPalette.WidgetMinWidth;
        layout.preferredWidth = PanelPalette.WidgetPreferredWidth;

        // 0, not 1: a flexible dropdown swallows the whole row - see WidgetPreferredWidth.
        layout.flexibleWidth = 0f;

        Image dropdownImage = dropdown.GetComponent<Image>();
        SharpSkin.ApplySliced(dropdownImage, SharpSkin.Input);
        dropdown.targetGraphic = dropdownImage;

        if (dropdown.captionText != null)
        {
            dropdown.captionText.color = PanelPalette.Ink;
            dropdown.captionText.fontSize = PanelPalette.WidgetTextSize;
        }

        if (dropdown.itemText != null)
        {
            dropdown.itemText.color = PanelPalette.Ink;
            dropdown.itemText.fontSize = PanelPalette.WidgetTextSize;
        }

        Transform template = dropdown.transform.Find("Template");

        if (template != null)
        {
            Image templateImage = template.GetComponent<Image>();
            SharpSkin.ApplySliced(templateImage, SharpSkin.Panel);

            // TMP builds its template 150 tall with 20px items, sized for the 14pt text its
            // factory defaults to. At WidgetTextSize the stock item clips the descenders, so
            // both are resized to match rather than left to the factory.
            RectTransform templateRect = (RectTransform)template;
            templateRect.sizeDelta = new Vector2(templateRect.sizeDelta.x, DropdownListHeight);

            Transform item = template.Find("Viewport/Content/Item");

            if (item != null)
            {
                RectTransform itemRect = (RectTransform)item;
                itemRect.sizeDelta = new Vector2(itemRect.sizeDelta.x, DropdownItemHeight);

                Transform itemBackground = item.Find("Item Background");

                if (itemBackground != null)
                {
                    SharpSkin.ApplySliced(itemBackground.GetComponent<Image>(), SharpSkin.RowSelected);
                }

                Transform itemCheckmark = item.Find("Item Checkmark");

                if (itemCheckmark != null)
                {
                    SharpSkin.ApplySliced(itemCheckmark.GetComponent<Image>(), SharpSkin.CheckMark,
                        PanelPalette.Gold);
                }
            }
        }

        // Explicit, and the reason the row was full-width even with flexibleWidth 0: the row s
        // background is a Simple Image carrying an 875px sprite, and a Simple Image reports its
        // sprite s native size as its preferredWidth. With the LayoutElement left at -1 it is
        // SKIPPED, so the Image and the layout group - both priority 0 - are compared and the
        // LARGER wins. 875, not the ~400 the caption and dropdown actually need.
        if (!stretch)
        {
            LayoutElement rowLayout = SharpSkin.Ensure<LayoutElement>(row.gameObject);

            rowLayout.preferredWidth = (PanelPalette.SettingsRowPaddingH * 2f)
                + (labelWidth > 0f ? labelWidth : PanelPalette.SettingsLabelColumn)
                + RowSpacing + PanelPalette.WidgetPreferredWidth;
        }

        SharpSkin.PruneChildren(row, "Label", "Dropdown");

        EditorUtility.SetDirty(dropdown);

        return dropdown;
    }

    /// <summary>
    /// A caption on the left and one action button on the right.
    ///
    /// SettingsRowBuilder had no label-plus-button shape - every settings row ends in a widget that
    /// holds a value, and a debug row ends in something that just happens. Built on the same Row shell
    /// so a debug row lines up with a settings row rather than being its own visual language.
    /// </summary>
    public static Button ButtonRow(RectTransform body, string objectName, string caption, string buttonText)
    {
        RectTransform row = Row(body, objectName, caption, out TMP_Text _);

        // The shared Row shell sizes itself for a settings widget; an action row needs less.
        LayoutElement rowLayout = SharpSkin.Ensure<LayoutElement>(row.gameObject);
        rowLayout.minHeight = PanelPalette.ActionRowHeight;
        rowLayout.preferredHeight = PanelPalette.ActionRowHeight;

        SharpSkin.PruneChildren(row, "Label", "Button");

        return MakeButton(row, "Button", buttonText, flexible: true);
    }

    /// <summary>
    /// A row of equal-width buttons with no caption - for a cluster of one-shot actions where each
    /// button's own label already says what it does and a left-hand caption would only repeat it.
    /// </summary>
    public static Button[] ButtonBar(RectTransform body, string objectName, params string[] labels)
    {
        RectTransform row = SharpSkin.EnsureChild(body, objectName);

        Image background = SharpSkin.Ensure<Image>(row.gameObject);
        SharpSkin.ApplySliced(background, SharpSkin.Row);
        background.raycastTarget = false;

        HorizontalLayoutGroup layout = SharpSkin.Ensure<HorizontalLayoutGroup>(row.gameObject);
        layout.padding = new RectOffset(10, 10, 4, 4);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        LayoutElement rowLayout = SharpSkin.Ensure<LayoutElement>(row.gameObject);
        rowLayout.minHeight = PanelPalette.ActionRowHeight;
        rowLayout.preferredHeight = PanelPalette.ActionRowHeight;
        // MUST be set explicitly, and 0 rather than left alone. LayoutUtility.GetLayoutProperty
        // SKIPS any component whose value is negative, so a LayoutElement left at the default -1
        // does not merely lose - it is never considered, and the next ILayoutElement on the object
        // answers instead. That is this row s own HorizontalLayoutGroup, which reports
        // flexibleHeight = 1 exactly because childForceExpandHeight is true above.
        //
        // The row then advertises "I will take spare vertical space" to its column, and four such
        // rows split a 314px surplus 78.5 each - which is how a row whose preferredHeight says 40
        // rendered at 118.5.
        rowLayout.flexibleHeight = 0f;
        rowLayout.flexibleWidth = 1f;

        Button[] buttons = new Button[labels.Length];

        for (int i = 0; i < labels.Length; i++)
        {
            buttons[i] = MakeButton(row, $"Button{i}", labels[i], flexible: true);
        }

        string[] keep = new string[labels.Length];

        for (int i = 0; i < labels.Length; i++) { keep[i] = $"Button{i}"; }

        SharpSkin.PruneChildren(row, keep);

        return buttons;
    }

    /// <summary>
    /// The button half of both shapes above. Idempotent like everything else here - a second run finds
    /// the same child and re-styles it rather than adding another.
    /// </summary>
    private static Button MakeButton(RectTransform parent, string objectName, string text, bool flexible)
    {
        RectTransform rect = SharpSkin.EnsureChild(parent, objectName);

        LayoutElement layout = SharpSkin.Ensure<LayoutElement>(rect.gameObject);
        layout.preferredHeight = PanelPalette.ActionButtonHeight;
        layout.minWidth = PanelPalette.WidgetMinWidth;
        layout.flexibleWidth = flexible ? 1f : 0f;

        Button button = SharpSkin.Ensure<Button>(rect.gameObject);

        RectTransform labelRect = SharpSkin.EnsureChild(rect, "Label");
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        labelRect.localScale = Vector3.one;

        TextMeshProUGUI label = SharpSkin.Ensure<TextMeshProUGUI>(labelRect.gameObject);

        TMP_FontAsset font = SharpSkin.LoadFont();

        if (font != null) { label.font = font; }

        label.text = text;
        label.fontSize = PanelPalette.ActionButtonLabelSize;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        SharpSkin.ApplyButton(button);

        return button;
    }
}
