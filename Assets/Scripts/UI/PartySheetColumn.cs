using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One hero's column on the Tab party sheet: portrait, name, health/shield, energy, and - one page at
/// a time - either every active status spelled out with a StatusDetailRow, or everything the hero is
/// wearing. See PartySheetPanel for why this screen exists alongside PartyPortraitPanel's compact row.
///
/// Two pages rather than one long list, because the two answer different questions ("what is happening
/// to me right now" vs "what am I built out of") and stacking them meant the equipment block pushed the
/// statuses - the thing that changes turn to turn - below the fold. Statuses come first for the same
/// reason: that is what you open this screen mid-battle to read.
///
/// Paging is per column, not per panel. Each hero's arrows move only their own column, so two heroes
/// can be read on different pages side by side - which is the whole point of a screen that shows the
/// party at once rather than one hero at a time.
///
/// A view only. PartySheetPanel decides who gets a column and where it sits; this only knows how to
/// draw the hero it is pointed at.
/// </summary>
public class PartySheetColumn : MonoBehaviour
{
    /// Which half of the column's lower section is showing. Statuses is 0 so a column that has never
    /// been paged - a freshly instantiated one, or one whose page field was never touched - opens on
    /// statuses, matching ResetToFirstPage's contract.
    private enum SheetPage
    {
        Statuses = 0,
        Equipment = 1,
    }

    [SerializeField] private Image portraitImage;

    [SerializeField] private TMP_Text nameLabel;

    [SerializeField] private Image healthFill;

    [SerializeField] private Image shieldFill;

    [SerializeField] private TMP_Text healthText;

    [SerializeField] private TMP_Text energyText;

    [Header("Status rows")]
    [Tooltip("Rows stack top-down inside this via its own VerticalLayoutGroup - unlike HeroPortrait's " +
        "chip row this never caps how many statuses are listed, only how many are visible at once " +
        "before scrollRoot scrolls, since reading everything in full is the point of this screen.")]
    [SerializeField] private RectTransform statusParent;

    [SerializeField] private StatusDetailRow rowPrefab;

    [Tooltip("The ScrollRect wrapping statusParent - a hero carrying enough statuses to overflow the " +
        "fixed column height scrolls rather than running off the bottom of the screen.")]
    [SerializeField] private ScrollRect scrollRoot;

    [Tooltip("Shown instead of the row list when a hero is carrying nothing - the empty state used to " +
        "just end the column, which read the same as a broken panel.")]
    [SerializeField] private GameObject noneLabel;

    [Header("Paging")]
    [Tooltip("Names the page currently showing - rewritten on every page change, so the text authored " +
        "on it in the Editor is only ever a placeholder.")]
    [SerializeField] private TMP_Text sectionLabel;

    [Tooltip("Back to the statuses page. Greyed rather than hidden while already there, so the header " +
        "row does not reflow every time the page changes.")]
    [SerializeField] private Button prevPageButton;

    [SerializeField] private Button nextPageButton;

    /// Grown on demand and reused, never destroyed - the same pooling contract StatusChip's other
    /// callers use.
    private readonly List<StatusDetailRow> rows = new();

    /// What the last Refresh was handed. Kept so an arrow click can redraw the lower section on its own
    /// - a page change is not a state change anything outside this column knows or cares about, so it
    /// must not have to wait for PartySheetPanel's next Refresh to show up.
    private Character hero;
    private StatusIcons icons;
    private Glossary glossary;

    private SheetPage page = SheetPage.Statuses;

    public RectTransform Rect { get; private set; }

    private void Awake()
    {
        Rect = (RectTransform)transform;

        if (prevPageButton != null) { prevPageButton.onClick.AddListener(() => SetPage(SheetPage.Statuses)); }

        if (nextPageButton != null) { nextPageButton.onClick.AddListener(() => SetPage(SheetPage.Equipment)); }
    }

    /// <summary>
    /// Puts this column back on the statuses page - what PartySheetPanel calls as it opens, so the sheet
    /// always opens on statuses rather than wherever the last look at this hero left off. Columns are
    /// pooled and reused across opens, so without this the page would persist between them.
    ///
    /// Deliberately does not redraw: Show calls this before Refresh, which draws everything anyway.
    /// </summary>
    public void ResetToFirstPage() => page = SheetPage.Statuses;

    public void Refresh(Character hero, StatusIcons icons, Glossary glossary)
    {
        if (hero == null) { return; }

        this.hero = hero;
        this.icons = icons;
        this.glossary = glossary;

        if (portraitImage != null)
        {
            portraitImage.sprite = hero.Portrait;
            portraitImage.enabled = portraitImage.sprite != null;
        }

        if (nameLabel != null) { nameLabel.text = hero.DisplayName; }

        int shield = HealthBarFill.Apply(healthFill, shieldFill, hero);

        if (healthText != null)
        {
            healthText.text = shield > 0
                ? $"{hero.Health}/{hero.MaxHealth}  +{shield}"
                : $"{hero.Health}/{hero.MaxHealth}";
        }

        if (energyText != null) { energyText.text = $"{hero.Energy}/{hero.MaxEnergy}"; }

        LayOutPage();
    }

    private void SetPage(SheetPage next)
    {
        if (page == next) { return; }

        page = next;

        LayOutPage();
    }

    /// <summary>
    /// Draws whichever page is showing into the shared pooled row list, then hides whatever the other
    /// page left behind - the two pages take turns over one set of rows rather than owning a list each,
    /// since only one is ever visible and a StatusDetailRow is the same shape either way.
    /// </summary>
    private void LayOutPage()
    {
        if (rowPrefab == null || statusParent == null || hero == null) { return; }

        int visible = page == SheetPage.Equipment
            ? LayOutEquipment(hero)
            : LayOutStatuses(hero, icons, glossary);

        for (int i = visible; i < rows.Count; i++) { rows[i].gameObject.SetActive(false); }

        // Positioning is statusParent's own VerticalLayoutGroup's job now, not this method's - a row
        // parented under a controlling layout group is placed and sized by it regardless of what its
        // own RectTransform says, which is what let Place() (and the rowHeight/rowSpacing fields it
        // needed) go away entirely.
        if (noneLabel != null) { noneLabel.SetActive(visible == 0); }

        if (scrollRoot != null) { scrollRoot.gameObject.SetActive(visible > 0); }

        if (sectionLabel != null)
        {
            sectionLabel.text = page == SheetPage.Equipment ? "Equipment" : "Status Effects";
        }

        // Greyed rather than hidden: SharpButtonTint already paints a disabled state, and hiding one
        // would resize the header row every time the page turned.
        if (prevPageButton != null) { prevPageButton.interactable = page != SheetPage.Statuses; }

        if (nextPageButton != null) { nextPageButton.interactable = page != SheetPage.Equipment; }
    }

    /// The four single-occupant slots, in display order - Ring is unlimited and walked separately in
    /// LayOutEquipment since EquippedIn always answers null for it.
    private static readonly EquipmentSlot[] SingleSlots =
    {
        EquipmentSlot.Weapon, EquipmentSlot.Armor, EquipmentSlot.Hat, EquipmentSlot.Boots,
    };

    /// <summary>
    /// The statuses page. Unlike HeroPortrait's row, this includes StatusType.Shield - there is no bar
    /// competing with it for space here, and its glossary entry is worth reading like any other.
    ///
    /// Falls back to the status's own Describe() and the bare enum name when the glossary has no entry
    /// authored yet, the same degrade StatusIcons.For and Glossary.Title already use elsewhere - a
    /// status with no prose written for it still reads as a sentence rather than vanishing.
    ///
    /// Returns how many rows it used, which is what LayOutPage hides the tail beyond.
    /// </summary>
    private int LayOutStatuses(Character hero, StatusIcons icons, Glossary glossary)
    {
        int visible = 0;

        foreach (StatusType type in StatusTypes.Displayable)
        {
            int stacks = hero.StatusStacks(type);

            if (stacks <= 0) { continue; }

            Status live = hero.FindStatus(type);

            TooltipContent content = glossary != null
                ? glossary.StatusContent(type, stacks, live)
                : null;

            string title = content != null && content.Entries.Count > 0
                ? content.Entries[0].title
                : type.ToString().ToUpperInvariant();

            string body = content != null && content.Entries.Count > 0
                ? content.Entries[0].body
                : live?.Describe() ?? string.Empty;

            StatusDetailRow row = RowAt(visible);
            row.Bind(icons != null ? icons.For(type) : null, title, body);

            visible++;
        }

        return visible;
    }

    /// <summary>
    /// The equipment page - the four single-occupant slots first (each shown even when empty, so a free
    /// slot reads as free rather than just being absent), then every ring held. Shares rowPrefab and the
    /// same pooled `rows` list the statuses page uses: only one page is visible at a time, equipment has
    /// no board presence of its own worth a second prefab over, and StatusDetailRow.Bind already takes
    /// exactly icon/title/body.
    ///
    /// Never returns 0, since the four slot rows are drawn whether or not anything fills them - so the
    /// "None" empty state LayOutPage shows is reachable only from the statuses page, which is correct:
    /// "no statuses" is worth saying, "no equipment at all" is already spelled out four times over.
    /// </summary>
    private int LayOutEquipment(Character hero)
    {
        int visible = 0;

        foreach (EquipmentSlot slot in SingleSlots)
        {
            EquipmentData equipped = hero.EquippedIn(slot);

            StatusDetailRow row = RowAt(visible);

            row.Bind(
                equipped != null ? equipped.icon : null,
                equipped != null ? $"{slot}: {equipped.equipmentName}" : $"{slot}: Empty",
                equipped != null ? equipped.description : $"No {slot} equipped.");

            visible++;
        }

        foreach (EquipmentData ring in hero.Equipment)
        {
            if (ring == null || ring.slot != EquipmentSlot.Ring) { continue; }

            StatusDetailRow row = RowAt(visible);
            row.Bind(ring.icon, ring.equipmentName, ring.description);

            visible++;
        }

        return visible;
    }

    private StatusDetailRow RowAt(int index)
    {
        while (rows.Count <= index) { rows.Add(Instantiate(rowPrefab, statusParent)); }

        rows[index].gameObject.SetActive(true);

        return rows[index];
    }
}
