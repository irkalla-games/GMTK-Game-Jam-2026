using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the debug tools panel in Game.unity and hands its widgets to DebugPanel.
///
/// A third root on PauseCanvas beside PauseRoot and SettingsRoot, and - unlike either of those - a
/// FIXED-size window rather than one that grows to its content. That is not a style choice: the card
/// browser is a ScrollRect, and a ScrollRect needs a bounded viewport to clip against, the same
/// constraint PartySheetStyling records for the party sheet column. A grow-to-content window would
/// simply expand to fit all 147 cards and never scroll.
///
/// Run Tools/UI/Wire Pause Menu first - this wires PauseMenu.debugPanel, but the Debug button that
/// opens it is built over there, next to the rest of the menu.
/// </summary>
public static class DebugPanelWiring
{
    private const string ScenePath = "Assets/Scenes/Game.unity";

    private const string CanvasName = "PauseCanvas";
    private const string RootName = "DebugRoot";

    private const string CardLibraryPath = "Assets/Data/CardLibrary/AllCards.asset";
    private const string EquipmentLibraryPath = "Assets/Data/EquipmentLibrary/AllEquipment.asset";
    private const string EnemyRegistryPath = "Assets/Data/EnemyRegistry.asset";
    private const string GlossaryPath = "Assets/Scripts/UI/Tooltips/Glossary.asset";

    /// Fixed, now that the card browser exists: a ScrollRect has to have something bounded to clip
    /// against, so the window cannot be left to grow with its content the way the pause and
    /// settings windows do. This is the same constraint PartySheetStyling records for the party
    /// sheet column.
    ///
    /// 900, not the original 820: the left column's VerticalLayoutGroup sets minHeight equal to
    /// preferredHeight on every row (see BuildPanel), so it cannot compress - a row that does not
    /// fit is not squeezed, it overflows the window. Hero + Browse + Search (3x66) + two group
    /// labels (2x49.5) + five buttons (5x64) + nine 6px gaps (54) = 671px of content against the
    /// 616px Split gets out of an 820 window (820 - 48 padding - 64 header - 68 footer - 24
    /// spacing). 900 leaves 696, a real margin rather than a rounding accident.
    private static readonly Vector2 WindowSize = new(1420f, 900f);

    private const float LeftColumnWidth = 460f;

    /// One card tile. Tall enough for art plus a wrapped two-line name underneath.
    private static readonly Vector2 TileSize = new(132f, 158f);

    private const float DetailPaneWidth = 300f;

    /// Caption column for the two filter rows. Much narrower than the standard 220 - "Class" and
    /// "Rarity" are one short word, and the wide column is what pushed those rows to full width.
    private const float FilterLabelWidth = 110f;

    [MenuItem("Tools/UI/Wire Debug Panel")]
    public static void Wire()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Debug panel wiring: exit Play Mode first - scene edits made in play do not persist.");
            return;
        }

        if (!OpenGameScene()) { return; }

        Canvas canvas = FindCanvas();

        if (canvas == null)
        {
            Debug.LogError($"Debug panel wiring: no {CanvasName} in {ScenePath} - run "
                           + "Tools/UI/Wire Pause Menu first.");
            return;
        }

        RectTransform canvasRect = (RectTransform)canvas.transform;

        DebugPanel panel = BuildPanel(canvasRect);

        WirePauseMenu(panel);

        // Last sibling, so it draws over the settings panel and the pause menu. PauseMenuWiring's
        // OrderLayers does the same thing; repeated here so this command is correct on its own.
        Transform root = canvasRect.Find(RootName);

        if (root != null) { root.SetAsLastSibling(); }

        // Before saving: RectTransform sizes are serialized, so a layout left to the deferred
        // end-of-frame pass bakes half-built numbers into the scene. See SharpSkin.RebuildLayout.
        SharpSkin.RebuildLayout((RectTransform)root);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log("Debug panel wiring: done - scene saved.");
    }

    private static DebugPanel BuildPanel(RectTransform canvasRect)
    {
        // On the canvas object, not on the root it hides. PauseMenuWiring documents why: a component
        // that deactivates its own GameObject can never run again to undo that.
        DebugPanel panel = SharpSkin.Ensure<DebugPanel>(canvasRect.gameObject);

        // Opened here rather than at the end: BuildBrowser writes its own widget references
        // through it, so one SerializedObject spans the whole build and is applied once.
        SerializedObject so = new(panel);

        RectTransform root = SharpSkin.EnsureChild(canvasRect, RootName);
        Stretch(root);

        BuildBackdrop(root);

        RectTransform window = BuildWindow(root);

        RectTransform split = SharpSkin.EnsureChild(window, "Split");

        HorizontalLayoutGroup splitLayout = SharpSkin.Ensure<HorizontalLayoutGroup>(split.gameObject);
        splitLayout.spacing = 16f;
        splitLayout.childAlignment = TextAnchor.UpperLeft;
        splitLayout.childForceExpandWidth = false;
        splitLayout.childForceExpandHeight = true;
        splitLayout.childControlWidth = true;
        splitLayout.childControlHeight = true;

        // Takes the window's spare height, which is what gives the card grid something to scroll
        // inside. Was 0 while the right column stood empty and this was just a tall empty box.
        LayoutElement splitLayoutElement = SharpSkin.Ensure<LayoutElement>(split.gameObject);
        splitLayoutElement.flexibleHeight = 1f;

        RectTransform left = SharpSkin.EnsureChild(split, "LeftColumn");

        LayoutElement leftElement = SharpSkin.Ensure<LayoutElement>(left.gameObject);
        leftElement.preferredWidth = LeftColumnWidth;
        leftElement.flexibleWidth = 0f;

        // No ScrollRect any more: moving Status into the browse dropdown took four rows off this
        // column, so it fits again. The prune is what removes the scroll and its Viewport -
        // without it the rows would exist BOTH here and inside the old scroll content, which is
        // exactly the duplication that appeared when the scroll was added.
        SharpSkin.PruneChildren(left, "HeroRow", "BrowseRow", "SearchRow", "CharacterGroup",
            "RefillRow", "HealRow", "DrawRow", "DiscardRow", "BoardGroup", "KillRow");

        VerticalLayoutGroup leftLayout = SharpSkin.Ensure<VerticalLayoutGroup>(left.gameObject);
        leftLayout.spacing = PanelPalette.SettingsRowSpacing;
        leftLayout.childAlignment = TextAnchor.UpperLeft;
        leftLayout.childForceExpandWidth = true;
        leftLayout.childForceExpandHeight = false;
        leftLayout.childControlWidth = true;
        leftLayout.childControlHeight = true;

        // Holds the card browser.
        RectTransform right = SharpSkin.EnsureChild(split, "RightColumn");
        LayoutElement rightElement = SharpSkin.Ensure<LayoutElement>(right.gameObject);
        rightElement.flexibleWidth = 1f;

        // Same reasoning: the browser owns everything under RightColumn, so a row left over from
        // an earlier arrangement goes rather than stacking beneath it.
        BuildBrowser(right, so);

        TMP_Dropdown hero = SettingsRowBuilder.DropdownRow(left, "HeroRow", "Hero");

        // A dropdown rather than a button per mode: at five modes the buttons were most of the
        // column, and this is navigation rather than an action.
        TMP_Dropdown browse = SettingsRowBuilder.DropdownRow(left, "BrowseRow", "Browse");

        // Filters the grid in every mode, unlike Class/Rarity which only apply to Cards - so it
        // sits with Hero/Browse rather than inside the card browser itself.
        TMP_InputField search = SettingsRowBuilder.SearchRow(left, "SearchRow", "search...");

        // Stacked one per row rather than in a bar: at four across, "Discard Hand" wrapped onto
        // two lines inside a 52px button.
        SettingsRowBuilder.GroupLabel(left, "CharacterGroup", "Character");

        Button refill = SettingsRowBuilder.ButtonBar(left, "RefillRow", "Refill Energy")[0];
        Button heal = SettingsRowBuilder.ButtonBar(left, "HealRow", "Heal")[0];
        Button draw = SettingsRowBuilder.ButtonBar(left, "DrawRow", "Draw")[0];
        Button discard = SettingsRowBuilder.ButtonBar(left, "DiscardRow", "Discard Hand")[0];

        // A separate group from Character: the four rows above act on the selected hero, this one
        // acts on the board regardless of who is selected.
        SettingsRowBuilder.GroupLabel(left, "BoardGroup", "Board");

        Button kill = SettingsRowBuilder.ButtonBar(left, "KillRow", "Kill All Enemies")[0];

        // Stated, not implied by creation order. EnsureChild finds-or-creates but never repositions
        // a child that already existed from an earlier run of this command - only a brand-new one is
        // appended, at the end, in whatever order it was created. On a scene that already had a
        // debug panel, SearchRow/BoardGroup/KillRow are all new and so all landed after DiscardRow
        // rather than where the calls above suggest - exactly the bug BuildBrowser's GridColumn/
        // DetailPane fix already exists for.
        SharpSkin.EnsureChild(left, "HeroRow").SetSiblingIndex(0);
        SharpSkin.EnsureChild(left, "BrowseRow").SetSiblingIndex(1);
        SharpSkin.EnsureChild(left, "SearchRow").SetSiblingIndex(2);
        SharpSkin.EnsureChild(left, "CharacterGroup").SetSiblingIndex(3);
        SharpSkin.EnsureChild(left, "RefillRow").SetSiblingIndex(4);
        SharpSkin.EnsureChild(left, "HealRow").SetSiblingIndex(5);
        SharpSkin.EnsureChild(left, "DrawRow").SetSiblingIndex(6);
        SharpSkin.EnsureChild(left, "DiscardRow").SetSiblingIndex(7);
        SharpSkin.EnsureChild(left, "BoardGroup").SetSiblingIndex(8);
        SharpSkin.EnsureChild(left, "KillRow").SetSiblingIndex(9);

        Button back = BuildFooterButton(window);

        so.FindProperty("root").objectReferenceValue = root.gameObject;
        so.FindProperty("backButton").objectReferenceValue = back;
        so.FindProperty("heroDropdown").objectReferenceValue = hero;
        so.FindProperty("browseDropdown").objectReferenceValue = browse;
        so.FindProperty("searchField").objectReferenceValue = search;
        so.FindProperty("refillEnergyButton").objectReferenceValue = refill;
        so.FindProperty("healButton").objectReferenceValue = heal;
        so.FindProperty("drawCardButton").objectReferenceValue = draw;
        so.FindProperty("discardHandButton").objectReferenceValue = discard;
        so.FindProperty("killEnemiesButton").objectReferenceValue = kill;
        so.FindProperty("glossary").objectReferenceValue = Load<Glossary>(GlossaryPath);
        so.FindProperty("cardLibrary").objectReferenceValue = Load<CardLibrary>(CardLibraryPath);
        so.FindProperty("equipmentLibrary").objectReferenceValue = Load<EquipmentLibrary>(EquipmentLibraryPath);
        so.FindProperty("enemyRegistry").objectReferenceValue = Load<EnemyRegistry>(EnemyRegistryPath);
        so.ApplyModifiedProperties();

        root.gameObject.SetActive(true);

        return panel;
    }

    /// <summary>

    /// <summary>
    /// Builds the card browser into the right column: filters, a scrolling grid, and the detail pane.
    ///
    /// The ScrollRect recipe is PartySheetStyling's, verbatim, because it is the only one in the project
    /// that works: a bounded viewport carrying a fully transparent Image whose raycastTarget stays TRUE
    /// - that is what catches the drag, and a viewport without it simply will not scroll - plus a
    /// RectMask2D to clip, over a top-pivot Content that grows downward.
    /// </summary>
    private static void BuildBrowser(RectTransform right, SerializedObject so)
    {
        // RightColumn now holds only the Browser. The filter rows moved INSIDE it, beside the
        // grid rather than above the whole column - up here their background stretched the full
        // 792px, past the detail pane, while the tiles below stopped short of it.
        SharpSkin.PruneChildren(right, "Browser");

        VerticalLayoutGroup rightLayout = SharpSkin.Ensure<VerticalLayoutGroup>(right.gameObject);
        rightLayout.spacing = PanelPalette.SettingsRowSpacing;
        rightLayout.childAlignment = TextAnchor.UpperLeft;
        rightLayout.childForceExpandWidth = true;
        rightLayout.childForceExpandHeight = false;
        rightLayout.childControlWidth = true;
        rightLayout.childControlHeight = true;


        RectTransform browser = SharpSkin.EnsureChild(right, "Browser");

        // ScrollArea used to be a direct child here; it now lives inside GridColumn, so the old
        // one has to go or the grid renders twice.
        SharpSkin.PruneChildren(browser, "GridColumn", "DetailPane");

        HorizontalLayoutGroup browserLayout = SharpSkin.Ensure<HorizontalLayoutGroup>(browser.gameObject);
        browserLayout.spacing = 12f;
        // LowerLeft: in a horizontal group the alignment positions each child on the CROSS axis,
        // so a child shorter than the row sits at the bottom of it. GridColumn has flexibleHeight
        // 1 and still fills, so this only moves the detail pane - down to line up with the last
        // row of cards.
        browserLayout.childAlignment = TextAnchor.LowerLeft;
        browserLayout.childForceExpandWidth = false;
        // false, or the group hands the pane the leftover height and it stretches again - which
        // would undo both the content-hugging height and the alignment above.
        browserLayout.childForceExpandHeight = false;
        browserLayout.childControlWidth = true;
        browserLayout.childControlHeight = true;

        LayoutElement browserElement = SharpSkin.Ensure<LayoutElement>(browser.gameObject);
        browserElement.flexibleHeight = 1f;
        browserElement.flexibleWidth = 1f;

        // A column of its own so the filter rows are exactly as wide as the grid beneath them.
        RectTransform gridColumn = SharpSkin.EnsureChild(browser, "GridColumn");

        SharpSkin.PruneChildren(gridColumn, "ClassFilterRow", "RarityFilterRow", "ScrollArea");

        VerticalLayoutGroup gridLayout = SharpSkin.Ensure<VerticalLayoutGroup>(gridColumn.gameObject);
        gridLayout.spacing = PanelPalette.SettingsRowSpacing;
        gridLayout.childAlignment = TextAnchor.UpperLeft;
        gridLayout.childForceExpandWidth = true;
        gridLayout.childForceExpandHeight = false;
        gridLayout.childControlWidth = true;
        gridLayout.childControlHeight = true;

        LayoutElement gridElement = SharpSkin.Ensure<LayoutElement>(gridColumn.gameObject);
        gridElement.flexibleWidth = 1f;
        gridElement.flexibleHeight = 1f;

        // Narrow caption and no stretch: these sit above the card grid, and a full-width background
        // running far past a short dropdown reads as a mistake rather than as a row.
        TMP_Dropdown classFilter = SettingsRowBuilder.DropdownRow(gridColumn, "ClassFilterRow", "Class",
            FilterLabelWidth, stretch: false);
        TMP_Dropdown rarityFilter = SettingsRowBuilder.DropdownRow(gridColumn, "RarityFilterRow", "Rarity",
            FilterLabelWidth, stretch: false);

        // The captions are handed over too, so DebugPanel can grey them out. The rows are DISABLED
        // rather than hidden outside card mode: hiding collapsed two rows and moved the grid up,
        // so the browser was visibly a different size per mode.
        so.FindProperty("classFilterLabel").objectReferenceValue = FilterLabel(classFilter);
        so.FindProperty("rarityFilterLabel").objectReferenceValue = FilterLabel(rarityFilter);

        RectTransform content = BuildScrollArea(gridColumn, "ScrollArea");

        GridLayoutGroup grid = SharpSkin.Ensure<GridLayoutGroup>(content.gameObject);
        grid.cellSize = TileSize;
        grid.spacing = new Vector2(8f, 8f);
        grid.padding = new RectOffset(6, 6, 6, 6);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperLeft;

        // Flexible rather than a fixed column count: the column count then follows the real
        // viewport width, so changing the detail pane or the window does not silently start
        // clipping. It only works because Content is exactly viewport-width - see BuildScrollArea.
        grid.constraint = GridLayoutGroup.Constraint.Flexible;

        DebugTile template = BuildTileTemplate(content);

        BuildDetailPane(browser, so);

        // Stated, not implied by creation order. DetailPane already existed and survived the
        // prune while GridColumn was created fresh after it, so the horizontal group laid the
        // pane out FIRST and it appeared to the left of the cards. Exactly the bug that put the
        // settings panel behind the pause menu - see PauseMenuWiring.OrderLayers.
        Transform gridFirst = browser.Find("GridColumn");
        Transform paneLast = browser.Find("DetailPane");

        if (gridFirst != null) { gridFirst.SetSiblingIndex(0); }

        if (paneLast != null) { paneLast.SetAsLastSibling(); }

        so.FindProperty("classFilter").objectReferenceValue = classFilter;
        so.FindProperty("rarityFilter").objectReferenceValue = rarityFilter;
        so.FindProperty("tileGridContent").objectReferenceValue = content;
        so.FindProperty("tileTemplate").objectReferenceValue = template;
    }

    /// <summary>
    /// A vertical ScrollRect and the Content rect to fill, added under `parent`.
    ///
    /// Returns Content WITHOUT a layout group - the caller adds the one it wants, because the card
    /// grid needs a GridLayoutGroup and the left column needs a VerticalLayoutGroup, and that is
    /// the only thing the two differ in.
    /// </summary>
    private static RectTransform BuildScrollArea(RectTransform parent, string objectName)
    {
        RectTransform scrollArea = SharpSkin.EnsureChild(parent, objectName);

        LayoutElement scrollElement = SharpSkin.Ensure<LayoutElement>(scrollArea.gameObject);
        scrollElement.flexibleWidth = 1f;
        scrollElement.flexibleHeight = 1f;

        ScrollRect scrollRect = SharpSkin.Ensure<ScrollRect>(scrollArea.gameObject);
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 32f;

        RectTransform viewport = SharpSkin.EnsureChild(scrollArea, "Viewport");
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        viewport.pivot = new Vector2(0f, 1f);

        Image viewportImage = SharpSkin.Ensure<Image>(viewport.gameObject);
        viewportImage.sprite = null;
        viewportImage.color = new Color(0f, 0f, 0f, 0f);

        // Load-bearing: a transparent Image still catches the drag, and without it the grid does not
        // scroll at all. PartySheetStyling records the same thing.
        viewportImage.raycastTarget = true;

        SharpSkin.Ensure<RectMask2D>(viewport.gameObject);

        RectTransform content = SharpSkin.EnsureChild(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;

        // Explicitly zero, and load-bearing. With anchorMin.x 0 and anchorMax.x 1, sizeDelta.x is
        // an OFFSET from the viewport width rather than a width - so any value left over from a
        // previous run makes Content wider than what clips it, and RectMask2D cuts the first
        // column off mid-card. Zero means Content is exactly viewport-width, which also makes
        // horizontal scrolling impossible rather than merely disabled.
        content.sizeDelta = new Vector2(0f, content.sizeDelta.y);

        ContentSizeFitter contentFitter = SharpSkin.Ensure<ContentSizeFitter>(content.gameObject);
        contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewport;
        scrollRect.content = content;

        EditorUtility.SetDirty(scrollRect);

        return content;
    }

    /// <summary>
    /// The tile DebugPanel clones once per visible card.
    ///
    /// Built as a scene object rather than a prefab asset so its styling lives here with every other
    /// piece of chrome, and so it picks up a PanelPalette change on the same re-run as everything else.
    /// DebugPanel.Awake deactivates it - it must never be visible itself.
    /// </summary>
    private static DebugTile BuildTileTemplate(RectTransform content)
    {
        // Renamed from CardTileTemplate now that it serves equipment and enemies too. Content is
        // pruned to this one name, so the old object goes rather than lingering with a missing
        // script where DebugCardTile used to be.
        SharpSkin.PruneChildren(content, "TileTemplate");

        RectTransform rect = SharpSkin.EnsureChild(content, "TileTemplate");

        DebugTile tile = SharpSkin.Ensure<DebugTile>(rect.gameObject);

        Image background = SharpSkin.Ensure<Image>(rect.gameObject);
        SharpSkin.ApplySliced(background, SharpSkin.Frame, 4f);

        Button button = SharpSkin.Ensure<Button>(rect.gameObject);
        button.targetGraphic = background;
        button.transition = Selectable.Transition.None;

        RectTransform artRect = SharpSkin.EnsureChild(rect, "Art");
        artRect.anchorMin = new Vector2(0f, 0.32f);
        artRect.anchorMax = new Vector2(1f, 1f);
        artRect.offsetMin = new Vector2(8f, 4f);
        artRect.offsetMax = new Vector2(-8f, -8f);

        Image art = SharpSkin.Ensure<Image>(artRect.gameObject);
        art.preserveAspect = true;
        art.raycastTarget = false;

        TMP_FontAsset font = SharpSkin.LoadFont();

        TMP_Text nameLabel = TileLabel(rect, "Name", font, new Vector2(0f, 0f), new Vector2(1f, 0.32f),
            PanelPalette.Ink, 15f, TextAlignmentOptions.Center);

        // "Badge", not "Cost": it carries a card s energy cost, an equipment s carried count or an
        // enemy s max health depending on the mode - see DebugPanel.RebuildTiles.
        TMP_Text badgeLabel = TileLabel(rect, "Badge", font, new Vector2(0f, 0.76f), new Vector2(0.34f, 1f),
            PanelPalette.Gold, 20f, TextAlignmentOptions.Center);

        tile.SetGraphics(button, background, art, nameLabel, badgeLabel);

        EditorUtility.SetDirty(tile);

        return tile;
    }

    private static TMP_Text TileLabel(RectTransform parent, string objectName, TMP_FontAsset font,
        Vector2 anchorMin, Vector2 anchorMax, Color colour, float size, TextAlignmentOptions alignment)
    {
        RectTransform rect = SharpSkin.EnsureChild(parent, objectName);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(4f, 2f);
        rect.offsetMax = new Vector2(-4f, -2f);

        TextMeshProUGUI label = SharpSkin.Ensure<TextMeshProUGUI>(rect.gameObject);

        if (font != null) { label.font = font; }

        label.fontSize = size;
        label.color = colour;
        label.alignment = alignment;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Truncate;

        EditorUtility.SetDirty(label);

        return label;
    }

    /// <summary>
    /// The pane beside the grid: what a tile has no room to say, plus the button that commits.
    /// </summary>
    private static void BuildDetailPane(RectTransform browser, SerializedObject so)
    {
        RectTransform pane = SharpSkin.EnsureChild(browser, "DetailPane");

        SharpSkin.ApplySliced(SharpSkin.Ensure<Image>(pane.gameObject), SharpSkin.Row);

        // The action button was renamed from GrantRow to ActionRow when one button replaced the
        // per-mode ones; without this the old GrantRow stays beside it and the pane shows two.
        SharpSkin.PruneChildren(pane, "DetailArt", "DetailName", "DetailMeta", "DetailBody",
            "ActionRow");

        LayoutElement paneElement = SharpSkin.Ensure<LayoutElement>(pane.gameObject);
        paneElement.preferredWidth = DetailPaneWidth;
        paneElement.flexibleWidth = 0f;
        // 0, not 1: stretching to the browser s full height made a tall column with the text at the
        // top and the button pinned at the bottom, far apart with nothing between them. The pane
        // now hugs its content, which is roughly half the height and puts the two together.
        paneElement.flexibleHeight = 0f;

        VerticalLayoutGroup layout = SharpSkin.Ensure<VerticalLayoutGroup>(pane.gameObject);
        // 22, not 14: the body text and the action button were sitting right against the pane s
        // own border. A panel that draws a frame needs the frame to read as a margin.
        // 22/20 rather than more: 30 was tried and left too little room for a card description
        // plus its keywords plus the totem it summons, which is the longest thing this pane shows.
        layout.padding = new RectOffset(22, 22, 20, 20);
        layout.spacing = 8f;

        // No ContentSizeFitter here. Its parent already controls this axis (childControlHeight),
        // and a fitter driving the same axis as the layout group above it is the classic pair
        // that fight each other. The pane s own VerticalLayoutGroup reports the content height,
        // which is what the parent reads - so the hugging happens without one.
        ContentSizeFitter staleFitter = pane.GetComponent<ContentSizeFitter>();

        if (staleFitter != null) { Object.DestroyImmediate(staleFitter); }
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        TMP_FontAsset font = SharpSkin.LoadFont();

        RectTransform artRect = SharpSkin.EnsureChild(pane, "DetailArt");

        LayoutElement artElement = SharpSkin.Ensure<LayoutElement>(artRect.gameObject);
        artElement.preferredHeight = 110f;

        Image art = SharpSkin.Ensure<Image>(artRect.gameObject);
        art.preserveAspect = true;
        art.raycastTarget = false;

        TMP_Text name = PaneLabel(pane, "DetailName", font, PanelPalette.Gold, 22f, 32f);
        TMP_Text meta = PaneLabel(pane, "DetailMeta", font, PanelPalette.LabelGrey, 15f, 24f);
        // A fixed slot rather than "take what is left": a flexible body is what pushed the button
        // away from the text. 160 is enough for a card description plus its keywords plus the
        // totem it summons, and the label auto-shrinks inside it rather than overflowing.
        TMP_Text body = PaneLabel(pane, "DetailBody", font, PanelPalette.BodyInk, 17f, 160f, autoSize: true);

        // ButtonBar, not ButtonRow: a captioned row in a 300px pane spends 220 of it on the word
        // "Give" and leaves the button unusable. The button's own label already says what it does,
        // which is exactly the case ButtonBar exists for.
        // Label rewritten per mode by DebugPanel.ShowDetail - Grant Card, Give Equipment, Take
        // Equipment, Spawn Enemy - so what it says here is only the resting state.
        Button action = SettingsRowBuilder.ButtonBar(pane, "ActionRow", "Grant Card")[0];

        so.FindProperty("detailArt").objectReferenceValue = art;
        so.FindProperty("detailName").objectReferenceValue = name;
        so.FindProperty("detailMeta").objectReferenceValue = meta;
        so.FindProperty("detailBody").objectReferenceValue = body;
        so.FindProperty("actionButton").objectReferenceValue = action;
        so.FindProperty("actionLabel").objectReferenceValue =
            action.GetComponentInChildren<TMP_Text>(includeInactive: true);
    }

    /// A detail line. preferredHeight 0 means "take what is left", for the body text.
    /// The caption a filter row was built with, so DebugPanel can grey it out with the control.
    private static TMP_Text FilterLabel(TMP_Dropdown dropdown)
    {
        if (dropdown == null) { return null; }

        Transform label = dropdown.transform.parent.Find("Label");

        return label != null ? label.GetComponent<TMP_Text>() : null;
    }

    /// <summary>
    /// One line of the detail pane.
    ///
    /// `autoSize` is separate from `height` on purpose: the body now has a FIXED slot like the
    /// others, and still needs to shrink inside it - the two used to be the same decision.
    /// </summary>
    private static TMP_Text PaneLabel(RectTransform pane, string objectName, TMP_FontAsset font, Color colour,
        float size, float height, bool autoSize = false)
    {
        RectTransform rect = SharpSkin.EnsureChild(pane, objectName);

        LayoutElement layout = SharpSkin.Ensure<LayoutElement>(rect.gameObject);

        // Every row is a fixed slot: nothing in this pane takes the leftover, which is what keeps
        // the button directly under the text instead of pinned to the bottom of a tall column.
        layout.preferredHeight = height;

        // Explicit 0 - a LayoutElement left at -1 is skipped by LayoutUtility entirely, so
        // whatever else is on the object answers instead. See CLAUDE.md.
        layout.flexibleHeight = 0f;

        TextMeshProUGUI label = SharpSkin.Ensure<TextMeshProUGUI>(rect.gameObject);

        if (font != null) { label.font = font; }

        label.fontSize = size;
        label.color = colour;
        label.alignment = TextAlignmentOptions.Top;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal;

        if (!autoSize) { EditorUtility.SetDirty(label); return label; }

        // The body is the only one that can be long - a card description plus its keywords plus
        // the totem it summons. Auto-sizing shrinks that to fit rather than letting it render
        // outside the panel, and Truncate is the backstop for text too long even at the minimum.
        label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = size;
        label.overflowMode = TextOverflowModes.Truncate;

        EditorUtility.SetDirty(label);

        return label;
    }

    /// Fills PauseMenu.debugPanel. The Debug button itself is built by PauseMenuWiring, next to the
    /// rest of the menu's buttons - this only supplies the thing that button opens.
    /// </summary>
    private static void WirePauseMenu(DebugPanel panel)
    {
        PauseMenu menu = Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include);

        if (menu == null)
        {
            Debug.LogWarning("Debug panel wiring: no PauseMenu in the scene - nothing will open the panel. "
                             + "Run Tools/UI/Wire Pause Menu.");
            return;
        }

        SerializedObject so = new(menu);
        so.FindProperty("debugPanel").objectReferenceValue = panel;
        so.ApplyModifiedProperties();
    }

    private static void BuildBackdrop(RectTransform root)
    {
        RectTransform backdrop = SharpSkin.EnsureChild(root, "Backdrop");
        Stretch(backdrop);

        Image image = SharpSkin.Ensure<Image>(backdrop.gameObject);
        image.sprite = null;
        // Barely tinted, unlike the pause and settings backdrops at 0.78 and 0.85. Those panels
        // stop the world and have nothing behind them worth seeing; this one leaves the battle
        // running, so hiding it behind an opaque sheet would defeat the point of not freezing.
        image.color = new Color(0f, 0f, 0f, 0.25f);
        // Still takes raycasts though: the board stays clickable underneath either way, because
        // those are OnMouseDown physics raycasts no Canvas intercepts, but this stops a stray
        // click landing on End Turn.
        image.raycastTarget = true;
    }

    /// <summary>
    /// The window. Fits its content vertically for now - see WindowWidth for why, and for what
    /// changes when the card browser lands.
    private static RectTransform BuildWindow(RectTransform root)
    {
        RectTransform window = SharpSkin.EnsureChild(root, "Window");
        window.anchorMin = new Vector2(0.5f, 0.5f);
        window.anchorMax = new Vector2(0.5f, 0.5f);
        window.pivot = new Vector2(0.5f, 0.5f);
        window.anchoredPosition = Vector2.zero;
        window.sizeDelta = WindowSize;
        window.localScale = Vector3.one;

        SharpSkin.ApplySliced(SharpSkin.Ensure<Image>(window.gameObject), SharpSkin.Panel);

        // No ContentSizeFitter: see WindowSize. If one was added while the browser did not exist,
        // it has to come off again or it will fight the fixed height.
        ContentSizeFitter staleFitter = window.GetComponent<ContentSizeFitter>();

        if (staleFitter != null) { Object.DestroyImmediate(staleFitter); }

        VerticalLayoutGroup layout = SharpSkin.Ensure<VerticalLayoutGroup>(window.gameObject);
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        RectTransform header = SharpSkin.EnsureChild(window, "Header");
        SharpSkin.ApplySliced(SharpSkin.Ensure<Image>(header.gameObject), SharpSkin.Header);

        LayoutElement headerLayout = SharpSkin.Ensure<LayoutElement>(header.gameObject);
        headerLayout.minHeight = 64f;
        headerLayout.preferredHeight = 64f;

        RectTransform titleRect = SharpSkin.EnsureChild(header, "Title");
        Stretch(titleRect);

        TextMeshProUGUI title = SharpSkin.Ensure<TextMeshProUGUI>(titleRect.gameObject);

        TMP_FontAsset font = SharpSkin.LoadFont();

        if (font != null) { title.font = font; }

        title.text = "DEBUG TOOLS";
        title.fontSize = 28f;
        title.characterSpacing = PanelPalette.HeaderTitleSpacing;
        title.color = PanelPalette.Gold;
        title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false;

        EditorUtility.SetDirty(title);

        return window;
    }

    private static Button BuildFooterButton(RectTransform window)
    {
        RectTransform footer = SharpSkin.EnsureChild(window, "Footer");

        HorizontalLayoutGroup layout = SharpSkin.Ensure<HorizontalLayoutGroup>(footer.gameObject);
        layout.padding = new RectOffset(0, 0, 8, 0);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        LayoutElement footerLayout = SharpSkin.Ensure<LayoutElement>(footer.gameObject);
        footerLayout.minHeight = 68f;
        footerLayout.preferredHeight = 68f;

        RectTransform rect = SharpSkin.EnsureChild(footer, "BackButton");

        LayoutElement buttonLayout = SharpSkin.Ensure<LayoutElement>(rect.gameObject);
        buttonLayout.preferredWidth = 260f;
        buttonLayout.preferredHeight = 52f;

        Button button = SharpSkin.Ensure<Button>(rect.gameObject);

        RectTransform labelRect = SharpSkin.EnsureChild(rect, "Label");
        Stretch(labelRect);

        TextMeshProUGUI label = SharpSkin.Ensure<TextMeshProUGUI>(labelRect.gameObject);

        TMP_FontAsset font = SharpSkin.LoadFont();

        if (font != null) { label.font = font; }

        label.text = "Back";
        label.fontSize = 26f;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        SharpSkin.ApplyButton(button);

        return button;
    }

    private static T Load<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);

        if (asset == null) { Debug.LogWarning($"Debug panel wiring: no {typeof(T).Name} at {path}."); }

        return asset;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;

        EditorUtility.SetDirty(rect);
    }

    private static Canvas FindCanvas()
    {
        foreach (Canvas candidate in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (candidate.name == CanvasName) { return candidate; }
        }

        return null;
    }

    private static bool OpenGameScene()
    {
        if (SceneManager.GetActiveScene().path == ScenePath) { return true; }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { return false; }

        EditorSceneManager.OpenScene(ScenePath);

        return true;
    }
}
