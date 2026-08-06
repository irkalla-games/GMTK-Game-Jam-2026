using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Read-only readout of whatever character is selected - name, health, shield, and a wrapping row of
/// status icons. Works for allies and enemies alike, since Character exposes the same properties for
/// both.
///
/// Shows `SelectedCharacter ?? ActiveCharacter`. That one rule covers two cases a straight reading of
/// SelectedCharacter gets wrong: battle start, where BattleManager sets an active character but never
/// a selected one, and a selected enemy dying, where SetSelectedCharacter(null) would otherwise leave
/// the panel blank. Resolved here rather than by making the battle select somebody on its behalf -
/// see the doc comments on both events for why the battle must not depend on the view.
///
/// Refreshed from three signals, and it needs all three: SelectedCharacterChanged and
/// ActiveCharacterChanged for *who* is shown, ActionManager.ActionResolved for damage and buffs, and
/// BattleManager.TurnAdvanced for the things that never pass through an action at all - Poison biting
/// in OnTurnEnd and Shield wiping itself in OnTurnStart. This class used to claim ActionResolved was
/// the only point stats could change; it was wrong, and a poisoned enemy's bar visibly did not move.
/// </summary>
public class SelectedCharacterPanel : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;

    [SerializeField] private TMP_Text nameText;

    [Tooltip("Filled horizontally from the left. fillAmount is driven from Health/MaxHealth.")]
    [SerializeField] private Image healthFill;

    [Tooltip("The same bar, filled from the *right* so shield reads as a distinct segment meeting the " +
        "health fill rather than covering it. Hidden when there is no shield.")]
    [SerializeField] private Image shieldFill;

    [SerializeField] private TMP_Text healthText;

    [Header("Status row")]
    [Tooltip("StatusType -> sprite. A type missing from it still gets a chip, just without art.")]
    [SerializeField] private StatusIcons icons;

    [Tooltip("StatusType -> the sentence a chip's tooltip shows. A type missing from it still gets a " +
        "chip, just without an explanation.")]
    [SerializeField] private Glossary glossary;

    [SerializeField] private StatusChip chipPrefab;

    [Tooltip("Chips are positioned inside this. Its height is driven from the number of rows.")]
    [SerializeField] private RectTransform chipParent;

    [Tooltip("How wide a row of chips may get before it wraps. Defaults to roughly the health bar's " +
        "width - set it independently if the row should run longer or shorter than the bar.")]
    [SerializeField] private float rowWidth = 220f;

    [SerializeField] private float chipSize = 44f;

    [SerializeField] private float chipSpacing = 4f;

    [Tooltip("Optional. If set, the panel's height is driven from however many chip rows there are, " +
        "so a second row grows the backing plate instead of spilling out of it. Anchored from its " +
        "bottom edge, so it grows upward.")]
    [SerializeField] private RectTransform panelRect;

    [Tooltip("Gap left under the last row of chips when panelRect is being resized.")]
    [SerializeField] private float panelBottomPadding = 12f;

    /// Grown on demand and reused. Refresh runs on every resolved action, so building and destroying
    /// chips each time would churn garbage to arrive back where it started.
    private readonly List<StatusChip> chips = new();

    /// <summary>
    /// Cached because Enum.GetValues allocates a fresh array every call and this runs on every action.
    ///
    /// Walking the enum rather than a hand-written list is what makes the row scale: a new StatusType
    /// shows up here the moment it exists, and giving it art is one row in the icon asset. Nothing in
    /// this class and nothing in the scene has to change.
    /// </summary>
    private static readonly StatusType[] AllTypes = (StatusType[])Enum.GetValues(typeof(StatusType));

    /// The canvas the panel lives on, needed to project panelRect into screen space for the tooltip.
    /// Found rather than serialized - the panel is already a child of it, so a second reference in the
    /// Inspector would only be a way to get it wrong.
    private Canvas canvas;

    /// How many chips fit before wrapping. At least one, however narrow the row is set.
    private int PerRow => Mathf.Max(1, Mathf.FloorToInt((rowWidth + chipSpacing) / (chipSize + chipSpacing)));

    private void Start()
    {
        canvas = GetComponentInParent<Canvas>();

        BattleManager battle = BattleManager.Instance;

        if (battle == null)
        {
            Debug.LogError($"{name}: no BattleManager in the scene - nothing to show info for");
            return;
        }

        battle.SelectedCharacterChanged += OnCharacterChanged;
        battle.ActiveCharacterChanged += OnCharacterChanged;
        battle.TurnAdvanced += Refresh;

        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved += OnActionResolved; }

        Refresh();
    }

    private void OnDestroy()
    {
        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.SelectedCharacterChanged -= OnCharacterChanged;
            BattleManager.Instance.ActiveCharacterChanged -= OnCharacterChanged;
            BattleManager.Instance.TurnAdvanced -= Refresh;
        }

        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved -= OnActionResolved; }
    }

    /// Both selection events land here. The argument is ignored on purpose - Refresh re-resolves who
    /// to show, so there is one answer to "whose stats are these" rather than a cached second one.
    private void OnCharacterChanged(Character character) => Refresh();

    private void OnActionResolved(GameAction action, ActionContext ctx) => Refresh();

    private Character Shown()
    {
        BattleManager battle = BattleManager.Instance;

        if (battle == null) { return null; }

        // Unity's == means a destroyed character already compares equal to null here, which is what
        // makes the fallback cover a selected enemy dying without any extra bookkeeping.
        return battle.SelectedCharacter != null ? battle.SelectedCharacter : battle.ActiveCharacter;
    }

    private void Refresh()
    {
        if (panelRoot == null) { return; }

        Character character = Shown();

        if (character == null)
        {
            panelRoot.SetActive(false);
            return;
        }

        panelRoot.SetActive(true);

        if (nameText != null) { nameText.text = character.name; }

        // Shield is not a field on Character - it is whatever a ShieldStatus in its list says it is.
        int shield = character.StatusStacks(StatusType.Shield);

        // A character authored with 0 max health would otherwise divide by zero and blank the bar.
        float max = Mathf.Max(1, character.MaxHealth);

        if (healthFill != null) { healthFill.fillAmount = Mathf.Clamp01(character.Health / max); }

        if (shieldFill != null)
        {
            shieldFill.fillAmount = Mathf.Clamp01(shield / max);
            shieldFill.enabled = shield > 0;
        }

        if (healthText != null)
        {
            healthText.text = shield > 0
                ? $"{character.Health}/{character.MaxHealth}  +{shield}"
                : $"{character.Health}/{character.MaxHealth}";
        }

        LayOutStatuses(character);
    }

    private void LayOutStatuses(Character character)
    {
        if (chipPrefab == null || chipParent == null) { return; }

        int visible = 0;

        foreach (StatusType type in AllTypes)
        {
            // None is the "never set" sentinel, and Shield is drawn on the bar - showing it here too
            // would be the same number in two places.
            if (type is StatusType.None or StatusType.Shield) { continue; }

            // Sums the character's own statuses and any aura projecting the same type onto its tile,
            // so a totem's Strength and a carried Strength read as one total rather than two chips.
            int stacks = character.StatusStacks(type);

            if (stacks <= 0) { continue; }

            StatusChip chip = ChipAt(visible);
            chip.Show(icons != null ? icons.For(type) : null, stacks);
            chip.Bind(TooltipFor(character, type, stacks), TooltipAnchor.Of(panelRect, canvas));
            Place(chip, visible);

            visible++;
        }

        for (int i = visible; i < chips.Count; i++) { chips[i].Hide(); }

        int rows = visible == 0 ? 0 : Mathf.CeilToInt(visible / (float)PerRow);
        float rowsHeight = rows == 0 ? 0f : rows * (chipSize + chipSpacing) - chipSpacing;

        chipParent.sizeDelta = new Vector2(rowWidth, rowsHeight);

        if (panelRect == null) { return; }

        // chipParent is anchored to the panel's top with a top pivot, so -y is how far down the row
        // starts. Everything above it is header and health bar, whose heights this deliberately does
        // not need to know - it just measures where the content actually ends.
        float contentBottom = -chipParent.anchoredPosition.y + rowsHeight;

        panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, contentBottom + panelBottomPadding);
    }

    /// <summary>
    /// What a chip says when hovered.
    ///
    /// `stacks` is passed in rather than read off the status because the chip shows a *sum* - a carried
    /// Strength and a totem projecting one are one chip - and the sentence has to quote the same number
    /// the badge does. FindStatus supplies the rest: Block's per-hit amount has no other source.
    ///
    /// The anchor is panelRect, not the chip, so the box sits above the whole panel and stays put as
    /// the cursor slides along the row.
    /// </summary>
    private TooltipContent TooltipFor(Character character, StatusType type, int stacks)
    {
        if (glossary == null) { return null; }

        return glossary.StatusContent(type, stacks, character.FindStatus(type));
    }

    /// The pool. Grows to whatever the busiest character needs and never shrinks.
    private StatusChip ChipAt(int index)
    {
        while (chips.Count <= index)
        {
            chips.Add(Instantiate(chipPrefab, chipParent));
        }

        return chips[index];
    }

    /// <summary>
    /// Left-to-right, top-down, wrapping at PerRow.
    ///
    /// Done here rather than with a GridLayoutGroup deliberately. The layout is four lines of
    /// arithmetic, and doing it in code keeps rowWidth a live number - change it and the next refresh
    /// re-flows - without depending on the ContentSizeFitter/LayoutGroup interplay to resize the row.
    /// </summary>
    private void Place(StatusChip chip, int index)
    {
        int perRow = PerRow;

        RectTransform rect = chip.Rect;

        // Anchored and pivoted to the parent's top-left so the arithmetic below is a straight offset
        // and rows grow downward however the panel above them is anchored.
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(chipSize, chipSize);
        rect.anchoredPosition = new Vector2(
            index % perRow * (chipSize + chipSpacing),
            -(index / perRow) * (chipSize + chipSpacing));
    }
}
