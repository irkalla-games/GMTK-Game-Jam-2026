using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Read-only readout of one side's selected character - name, health, shield, a wrapping row of status
/// icons, and (enemy side only) what it means to do next. Two of these sit in the battle HUD, one per
/// PanelAudience value, built from the same class because the layout and drawing code are entirely
/// side-agnostic - only *which* character each one will show differs.
///
/// PlayerControlled shows `SelectedCharacter ?? ActiveCharacter`, filtered to IsPlayerControlled. That
/// covers two cases a straight reading of SelectedCharacter gets wrong: battle start, where
/// BattleManager sets an active character but never a selected one, and clicking an enemy, which must
/// not blank or repoint the hero panel. NotPlayerControlled shows SelectedCharacter only when it is
/// *not* player-controlled, and hides otherwise - see Shown(). Resolved here rather than by making the
/// battle track two selections on its behalf - see the doc comments on both events for why the battle
/// must not depend on the view.
///
/// Refreshed from three signals, and it needs all three: SelectedCharacterChanged and
/// ActiveCharacterChanged for *who* is shown, ActionManager.ActionResolved for damage and buffs, and
/// BattleManager.TurnAdvanced for the things that never pass through an action at all - Poison biting
/// in OnTurnEnd and Shield wiping itself in OnTurnStart. This class used to claim ActionResolved was
/// the only point stats could change; it was wrong, and a poisoned enemy's bar visibly did not move.
/// A fourth, per-character signal (Character.IntentChanged) keeps the intent icon live against a shown
/// character whose intent BattleManager recomputes silently in LateUpdate - see UpdateIntentSource.
/// </summary>
public class SelectedCharacterPanel : MonoBehaviour
{
    [SerializeField] private PanelAudience audience = PanelAudience.PlayerControlled;

    [SerializeField] private GameObject panelRoot;

    [SerializeField] private TMP_Text nameText;

    [Tooltip("Filled horizontally from the left. fillAmount is driven from Health/MaxHealth.")]
    [SerializeField] private Image healthFill;

    [Tooltip("The same bar, filled from the left over the health fill and capped at current health, " +
        "so the dark remainder always reads as missing health. Hidden when there is no shield.")]
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

    /// Which characters this instance describes, so a caller wanting the enemy panel specifically can
    /// pick it out of the two in the scene rather than guessing from the object's name.
    public PanelAudience Audience => audience;

    /// The chip row, exposed so the tutorial spotlight can light the statuses it is telling the player
    /// to hover. Read-only: where a chip sits is still this panel's business - see StatusChip's own doc
    /// comment for why it owns no layout of its own.
    public RectTransform StatusRowRect => chipParent;

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

    [Header("Intent (leave empty on the hero panel)")]
    [Tooltip("What the shown character means to do next. Empty on the hero panel - players have no " +
        "committed intent, and an empty field here also skips the IntentChanged subscription entirely.")]
    [SerializeField] private Image intentIcon;

    [SerializeField] private IntentIcons intentIcons;

    /// Grown on demand and reused. Refresh runs on every resolved action, so building and destroying
    /// chips each time would churn garbage to arrive back where it started.
    private readonly List<StatusChip> chips = new();

    /// Walking StatusTypes.Displayable rather than a hand-written list is what makes the row scale: a
    /// new StatusType shows up here the moment it exists, and giving it art is one row in the icon
    /// asset. Nothing in this class and nothing in the scene has to change.

    /// The canvas the panel lives on, needed to project panelRect into screen space for the tooltip.
    /// Found rather than serialized - the panel is already a child of it, so a second reference in the
    /// Inspector would only be a way to get it wrong.
    private Canvas canvas;

    /// Whoever intentIcon's IntentChanged subscription currently points at - see UpdateIntentSource.
    /// Null on the hero panel, since intentIcon is left empty there and the subscription never starts.
    private Character intentSource;

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

        UpdateIntentSource(null);
    }

    /// Both selection events land here. The argument is ignored on purpose - Refresh re-resolves who
    /// to show, so there is one answer to "whose stats are these" rather than a cached second one.
    private void OnCharacterChanged(Character character) => Refresh();

    private void OnActionResolved(GameAction action, ActionContext ctx) => Refresh();

    /// The shown character's intent changed without their stats changing - BattleManager.LateUpdate
    /// recomputes it silently whenever the board does, with no ActionResolved or TurnAdvanced to ride
    /// along on. The argument is the same character UpdateIntentSource already subscribed to, so this
    /// only ever fires for whoever is currently shown.
    private void OnIntentChanged(Character character) => RefreshIntent(character);

    private Character Shown()
    {
        BattleManager battle = BattleManager.Instance;

        if (battle == null) { return null; }

        // Unity's == means a destroyed character already compares equal to null here, which is what
        // makes the enemy panel's own fallback (below) cover a selected enemy dying without any extra
        // bookkeeping.
        Character selected = battle.SelectedCharacter;

        if (audience == PanelAudience.PlayerControlled)
        {
            // Falls back to ActiveCharacter rather than going blank when an enemy is selected - the
            // hero panel must not repoint or hide just because you clicked something else to inspect
            // it. ActiveCharacter is only ever set to a player-controlled character (BattleManager's
            // FirstPlayableCharacter and its OnTileClicked hero branch both gate on it), so this never
            // falls through to something the panel would then have to filter again.
            return selected != null && selected.IsPlayerControlled ? selected : battle.ActiveCharacter;
        }

        // No ActiveCharacter fallback here - there is no "always shown" enemy the way there is always
        // an active hero, so this panel is meant to go blank until something not player-controlled is
        // actually selected.
        return selected != null && !selected.IsPlayerControlled ? selected : null;
    }

    private void Refresh()
    {
        if (panelRoot == null) { return; }

        Character character = Shown();

        if (character == null)
        {
            panelRoot.SetActive(false);
            UpdateIntentSource(null);
            return;
        }

        panelRoot.SetActive(true);

        if (nameText != null) { nameText.text = character.DisplayName; }

        // Shared with the overhead bar so the two can never disagree - see HealthBarFill. Hands back
        // the shield total it read, which the text below needs anyway.
        int shield = HealthBarFill.Apply(healthFill, shieldFill, character);

        // The *full* shield, even when the bar caps its fill at current health. The bar shows where
        // the shield sits, this states how much of it there actually is.
        if (healthText != null)
        {
            healthText.text = shield > 0
                ? $"{character.Health}/{character.MaxHealth}  +{shield}"
                : $"{character.Health}/{character.MaxHealth}";
        }

        UpdateIntentSource(character);
        RefreshIntent(character);
        LayOutStatuses(character);
    }

    /// <summary>
    /// Points the IntentChanged subscription at whoever is now shown, so the icon stays live against a
    /// committed intent BattleManager recomputes silently in LateUpdate whenever the board changes -
    /// none of Refresh's other three signals fire for that. A no-op on the hero panel: intentIcon is
    /// left empty there, so this never subscribes to anything and every shown hero pays nothing for it.
    /// </summary>
    private void UpdateIntentSource(Character character)
    {
        if (intentIcon == null || intentSource == character) { return; }

        if (intentSource != null) { intentSource.IntentChanged -= OnIntentChanged; }

        intentSource = character;

        if (intentSource != null) { intentSource.IntentChanged += OnIntentChanged; }
    }

    /// <summary>
    /// Sets the icon straight to the character's current intent - no roll. IntentRoll's fall-in belongs
    /// to CharacterOverheadViewer, where it always animates the same character's intent changing; here
    /// the shown character itself can change between two refreshes, and rolling from one enemy's icon
    /// to a different enemy's would read as a change in *that enemy's* intent rather than as a change
    /// of who is being looked at.
    /// </summary>
    private void RefreshIntent(Character character)
    {
        if (intentIcon == null) { return; }

        Sprite sprite = character != null && intentIcons != null
            ? intentIcons.For(character.CommittedIntent.kind)
            : null;

        intentIcon.sprite = sprite;
        intentIcon.enabled = sprite != null;
    }

    private void LayOutStatuses(Character character)
    {
        if (chipPrefab == null || chipParent == null) { return; }

        int visible = 0;

        foreach (StatusType type in StatusTypes.Displayable)
        {
            // Shield is drawn on the bar - showing it here too would be the same number in two places.
            if (type == StatusType.Shield) { continue; }

            // Sums the character's own statuses and any aura projecting the same type onto its tile,
            // so a totem's Strength and a carried Strength read as one total rather than two chips.
            int stacks = character.StatusStacks(type);

            if (stacks <= 0) { continue; }

            // The status in force decides whether a number shows at all; the number itself is every
            // carried turn of this type added up. Those differ for Weaken and Vulnerable, where several
            // instances queue behind one another - the badge counts the whole queue, since each waits
            // rather than burning down under the one above it. A totem's aura in force badges nothing:
            // it lasts as long as you stand there, so no number would ever move.
            Status live = character.FindStatus(type);
            string badge = live == null || live.IsProjected
                ? string.Empty
                : character.CarriedStatusStacks(type).ToString();

            StatusChip chip = ChipAt(visible);
            chip.Show(icons != null ? icons.For(type) : null, badge);
            chip.Bind(TooltipFor(character, type, stacks, live), TooltipAnchor.Of(panelRect, canvas));
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
    private TooltipContent TooltipFor(Character character, StatusType type, int stacks, Status live)
    {
        if (glossary == null) { return null; }

        return glossary.StatusContent(type, stacks, live);
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
