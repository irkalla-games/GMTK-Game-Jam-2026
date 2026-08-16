using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One hero's column on the Tab party sheet: portrait, name, health/shield, energy, and every active
/// status spelled out with a StatusDetailRow rather than capped behind a "+N" badge - see
/// PartySheetPanel for why this screen exists alongside PartyPortraitPanel's compact row.
///
/// A view only. PartySheetPanel decides who gets a column and where it sits; this only knows how to
/// draw the hero it is pointed at.
/// </summary>
public class PartySheetColumn : MonoBehaviour
{
    [SerializeField] private Image portraitImage;

    [SerializeField] private TMP_Text nameLabel;

    [SerializeField] private Image healthFill;

    [SerializeField] private Image shieldFill;

    [SerializeField] private TMP_Text healthText;

    [SerializeField] private TMP_Text energyText;

    [Header("Status rows")]
    [Tooltip("Rows stack top-down inside this, one per active status - unlike HeroPortrait's chip row " +
        "this never caps or overflows, since reading everything in full is the point of this screen.")]
    [SerializeField] private RectTransform statusParent;

    [SerializeField] private StatusDetailRow rowPrefab;

    [SerializeField] private float rowHeight = 56f;

    [SerializeField] private float rowSpacing = 6f;

    /// Cached because Enum.GetValues allocates a fresh array every call - same reasoning as
    /// SelectedCharacterPanel.AllTypes and HeroPortrait.AllTypes.
    private static readonly StatusType[] AllTypes = (StatusType[])Enum.GetValues(typeof(StatusType));

    /// Grown on demand and reused, never destroyed - the same pooling contract StatusChip's other
    /// callers use.
    private readonly List<StatusDetailRow> rows = new();

    public RectTransform Rect { get; private set; }

    private void Awake()
    {
        Rect = (RectTransform)transform;
    }

    public void Refresh(Character hero, StatusIcons icons, Glossary glossary)
    {
        if (hero == null) { return; }

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

        LayOutStatuses(hero, icons, glossary);
    }

    /// <summary>
    /// Unlike HeroPortrait's row, this includes StatusType.Shield - there is no bar competing with it
    /// for space here, and its glossary entry is worth reading like any other. Still skips None, the
    /// "never set" sentinel.
    ///
    /// Falls back to the status's own Describe() and the bare enum name when the glossary has no entry
    /// authored yet, the same degrade StatusIcons.For and Glossary.Title already use elsewhere - a
    /// status with no prose written for it still reads as a sentence rather than vanishing.
    /// </summary>
    private void LayOutStatuses(Character hero, StatusIcons icons, Glossary glossary)
    {
        if (rowPrefab == null || statusParent == null) { return; }

        int visible = 0;

        foreach (StatusType type in AllTypes)
        {
            if (type == StatusType.None) { continue; }

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
            Place(row, visible);

            visible++;
        }

        for (int i = visible; i < rows.Count; i++) { rows[i].gameObject.SetActive(false); }

        statusParent.sizeDelta = new Vector2(
            statusParent.sizeDelta.x,
            visible == 0 ? 0f : visible * (rowHeight + rowSpacing) - rowSpacing);
    }

    private StatusDetailRow RowAt(int index)
    {
        while (rows.Count <= index) { rows.Add(Instantiate(rowPrefab, statusParent)); }

        rows[index].gameObject.SetActive(true);

        return rows[index];
    }

    private void Place(StatusDetailRow row, int index)
    {
        RectTransform rect = row.Rect;

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, rowHeight);
        rect.anchoredPosition = new Vector2(0f, -index * (rowHeight + rowSpacing));
    }
}
