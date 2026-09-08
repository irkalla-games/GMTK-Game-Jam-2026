using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns the small canvas above a character's head: a health bar with a shield fill layered over it,
/// a damage-preview fill layered under it, and - enemies only - an icon for the category of action
/// they have committed to this turn, with the raw damage of a committed Attack shown beside it.
///
/// Character raises StatsChanged and IntentChanged and knows nothing about this class - the same
/// contract as CardDrawn/CardDiscarded and ActiveHandViewer. This is the thing worth not repeating
/// from the old design, where Character wrote `healthBar.text` directly from seven places with no
/// null guard. GridManager.ShowDamagePreview is the same kind of push, aimed at SetDamagePreview
/// instead: this class knows nothing about cards or hovering either.
///
/// Named for the half it owns, not "character UI" generally - SelectedCharacterPanel is the other
/// half, and it stays a screen-space HUD keyed to whichever character is selected/active.
/// </summary>
[RequireComponent(typeof(Character))]
public class CharacterOverheadViewer : MonoBehaviour
{
    [SerializeField] private Image healthFill;

    [SerializeField] private Image shieldFill;

    [Tooltip("Enemies only. Leave empty on heroes, totems and anything else with no brain.")]
    [SerializeField] private Image intentIcon;

    [Tooltip("Unused if intentIcon is empty.")]
    [SerializeField] private IntentIcons icons;

    [Tooltip("Placeholder motion for the intent icon changing - the new icon falls in and pushes the "
             + "old one out, the same mechanic TurnTransitionViewer plays for the turn counter. Unused "
             + "if intentIcon is empty.")]
    [SerializeField] private IntentRoll intentRoll = new();

    [Tooltip("The raw damage number beside the intent icon for an Attack, gated on "
             + "GameSettings.ShowIntentDamage. Built beside intentRoll's own window - unused if "
             + "intentIcon is empty.")]
    [SerializeField] private IntentDamageLabel damageLabel = new();

    [Tooltip("One more icon per action point past the first, for a character whose Character.ActionPoints "
             + "is above 1 - mainly bosses. Built beside intentRoll's own window, same as damageLabel; "
             + "unused if intentIcon is empty.")]
    [SerializeField] private IntentRow intentRow = new();

    [Header("Status icons")]
    [Tooltip("Small glyph row pooled under this - a point anchor to the left of the bar and clear of "
             + "it vertically, growing further left as more stack up. Leave empty to skip this "
             + "character entirely.")]
    [SerializeField] private RectTransform statusIconParent;

    [SerializeField] private OverheadStatusIcon statusIconPrefab;

    [SerializeField] private StatusIcons statusIcons;

    [SerializeField] private float statusIconSize = 22f;

    [SerializeField] private float statusIconSpacing = 4f;

    /// Grown on demand and reused, never destroyed - same pooling contract HeroPortrait's own chips
    /// and pips use.
    private readonly List<OverheadStatusIcon> statusIconPool = new();

    private Character character;

    /// Exposed for the tutorial spotlight, which lights the health bar it is telling the player to
    /// read - the same read-only-rect contract SelectedCharacterPanel.StatusRowRect and
    /// PartyPortraitPanel.PortraitRectFor already use.
    public RectTransform HealthBarRect => healthFill != null ? (RectTransform)healthFill.transform : null;

    /// Null on a hero, same as intentIcon itself - see the field's own tooltip.
    public RectTransform IntentIconRect => intentIcon != null ? (RectTransform)intentIcon.transform : null;

    /// What the icon is currently showing, kept separately from character.CommittedIntent.kind so
    /// RefreshIntent can tell a real change from a re-assignment of the same kind - the refresh pass in
    /// BattleManager already guards this on its own side, but TurnStart's re-commit does not.
    private IntentKind shownKind;

    /// Built from healthFill in Awake - see DamagePreviewFill.Build. Null when this character has no
    /// healthFill authored at all (there is none today, but nothing enforces it).
    private DamagePreviewFill previewFill;

    /// Health a hovered attack would take off this character, or 0 for none. Set by
    /// GridManager.ShowDamagePreview and cleared by ClearDamagePreview - this class does not decide
    /// when a preview applies, only how to draw the one it is handed.
    private int previewLoss;

    private void Awake()
    {
        character = GetComponent<Character>();

        if (intentIcon != null)
        {
            intentRoll.Build(intentIcon);

            // Must run after Build - the label anchors to intentRoll.Window, which Build is what
            // creates.
            damageLabel.Build(intentRoll.Window);

            // Same ordering requirement - the row lays its own slots out from the primary window's
            // and damage label's current geometry.
            intentRow.Build(intentRoll.Window, damageLabel);
        }

        if (healthFill != null) { previewFill = DamagePreviewFill.Build(healthFill); }

        // The overhead canvas is authored World Space with no camera assigned. TooltipAnchor's rect path
        // (what the tutorial spotlight uses to light HealthBarRect/IntentIconRect) projects through
        // canvas.worldCamera for anything but an Overlay canvas, and a null camera there answers world
        // coordinates rather than screen ones - the highlight lands nowhere visible. Same fix as
        // TotemTooltip.Awake for its own overhead canvas.
        if (healthFill != null)
        {
            Canvas overheadCanvas = healthFill.GetComponentInParent<Canvas>();

            if (overheadCanvas != null && overheadCanvas.worldCamera == null)
            {
                overheadCanvas.worldCamera = SceneCameras.Board;
            }
        }
    }

    private void Start()
    {
        character.StatsChanged += OnStatsChanged;
        character.IntentChanged += OnIntentChanged;

        if (BattleManager.Instance != null) { BattleManager.Instance.TurnAdvanced += OnTurnAdvanced; }

        // Auras are pulled, not pushed (see Totem) - summoning a totem or a character stepping into or
        // out of its range never touches this character's own StatsChanged, since nothing writes to it.
        // ActionResolved is what SelectedCharacterPanel already listens to for exactly this reason: it
        // fires for every action on the board, not just this character's own, so the totem's own summon
        // is enough to refresh this row without anything happening to the character it now covers.
        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved += OnActionResolved; }

        // A pull, not just a tidy default. SpawnParty calls SetHealth on the frame it instantiates a
        // hero - before that hero's own Start runs - so the StatsChanged raise from it fires into an
        // empty invocation list. Same story for a wave enemy's first CommittedIntent. Without this
        // pull, a hero arriving hurt from the previous level would show a full bar until the next
        // stat change, and a freshly spawned enemy would wear no icon until its second turn.
        RefreshBar();
        RefreshStatusIcons();

        // Set directly rather than through RefreshIntent/intentRoll.Play - a freshly spawned enemy's
        // first icon should not fall in while its own spawn scale-in is still playing.
        IReadOnlyList<Intent> plan = character.CommittedPlan;
        shownKind = plan.Count > 0 ? plan[0].kind : IntentKind.Wait;

        if (intentIcon != null)
        {
            intentRoll.Show(icons != null ? icons.For(shownKind) : null);
            RefreshIntentDamage(plan);
        }
    }

    private void OnDestroy()
    {
        if (character != null)
        {
            character.StatsChanged -= OnStatsChanged;
            character.IntentChanged -= OnIntentChanged;
        }

        if (BattleManager.Instance != null) { BattleManager.Instance.TurnAdvanced -= OnTurnAdvanced; }

        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved -= OnActionResolved; }

        intentRoll.Kill();
        intentRow.Kill();
    }

    private void OnStatsChanged(Character _)
    {
        RefreshBar();
        RefreshStatusIcons();

        // The damage number is this character's own outgoing side (Strength, Weaken, Double Attack),
        // so it has to repaint on the same signal as the health bar - see Card.OutgoingDamage's own
        // doc comment for why the defender's side is deliberately not watched here.
        RefreshIntentDamage(character.CommittedPlan);
    }

    private void OnTurnAdvanced()
    {
        RefreshBar();
        RefreshStatusIcons();
    }

    private void OnIntentChanged(Character _) => RefreshIntent();

    private void OnActionResolved(GameAction action, ActionContext ctx)
    {
        RefreshStatusIcons();

        // A totem's aura is pulled, not pushed (see Totem/Aura) - summoning or destroying one near
        // this character never raises its own StatsChanged, only an action resolving somewhere on the
        // board. Same reasoning RefreshStatusIcons above already relies on this event for.
        RefreshIntentDamage(character.CommittedPlan);
    }

    /// <summary>
    /// Shows a projected loss of `loss` health on next refresh - GridManager.ShowDamagePreview calls
    /// this once per previewed character while a card is selected and a tile is hovered.
    /// Equality-guarded like TileSelector's setters, so re-hovering the same tile every frame costs
    /// nothing past the first call.
    /// </summary>
    public void SetDamagePreview(int loss)
    {
        loss = Mathf.Max(0, loss);
        if (loss == previewLoss) { return; }

        previewLoss = loss;
        RefreshBar();
    }

    public void ClearDamagePreview() => SetDamagePreview(0);

    /// <summary>
    /// Routed through RefreshBar rather than writing fillAmount directly, so a StatsChanged arriving
    /// mid-hover (poison ticking, say) does not silently wipe the preview - HealthBarFill.Apply
    /// overwrites healthFill's fillAmount unconditionally on every call.
    ///
    /// The arithmetic lives in HealthBarFill, shared with SelectedCharacterPanel - the two bars show
    /// the same numbers and must not be able to disagree about what they mean.
    /// </summary>
    private void RefreshBar()
    {
        HealthBarFill.Apply(healthFill, shieldFill, previewFill, character, previewLoss);
    }

    private void RefreshIntent()
    {
        if (intentIcon == null) { return; }

        IReadOnlyList<Intent> plan = character.CommittedPlan;
        Intent intent = plan.Count > 0 ? plan[0] : Intent.Wait();

        // Repaints on every IntentChanged, unlike the roll below - a re-aim onto a different tile or
        // victim keeps the same kind (so the icon does not roll) but can still change what the locked
        // card would hit for, e.g. a splash card now catching one more character. Also repaints every
        // follow-up icon and number the same way, and re-lays the row out to match.
        RefreshIntentDamage(plan);

        IntentKind next = intent.kind;

        // Guards the case IntentChanged does not: BattleManager's own refresh pass already skips
        // assigning an unchanged kind, but TurnStart re-commits every enemy from scratch every round,
        // including Wait-to-Wait, and CommittedIntent's setter has no equality check of its own.
        if (next == shownKind) { return; }

        Sprite from = icons != null ? icons.For(shownKind) : null;
        Sprite to = icons != null ? icons.For(next) : null;

        shownKind = next;
        intentRoll.Play(from, to);
    }

    /// <summary>
    /// The raw damage this character's currently-committed Attack card would swing for, or hidden
    /// entirely - a Move/Summon/Wait intent has no victim to show one about, and GameSettings can turn
    /// the whole feature off. Deliberately does not depend on anything the defender is carrying - see
    /// Card.OutgoingDamage's own doc comment for why that is the point, not a gap.
    ///
    /// Also drives every follow-up icon in the row from the rest of `plan` - see IntentRow.Show, which
    /// applies the same guard to each of its own slots.
    /// </summary>
    private void RefreshIntentDamage(IReadOnlyList<Intent> plan)
    {
        Intent intent = plan.Count > 0 ? plan[0] : Intent.Wait();

        bool show = GameSettings.ShowIntentDamage
                    && intent.kind == IntentKind.Attack
                    && intent.card != null
                    && GridManager.Instance != null;

        damageLabel.Show(show ? intent.card.OutgoingDamage(character, GridManager.Instance.GetTile(intent.target)) : 0);

        intentRow.Show(plan, icons, character);
    }

    /// <summary>
    /// The same walk HeroPortrait/SelectedCharacterPanel's own status rows use, with no count badge and
    /// no tooltip - this is a glance-only glyph row for a canvas too small to carry either, not a third
    /// answer to what a status means. Shield is skipped for the same reason both of those skip it: it is
    /// drawn on the bar this row sits right next to.
    /// </summary>
    private void RefreshStatusIcons()
    {
        if (statusIconPrefab == null || statusIconParent == null) { return; }

        int visible = 0;

        foreach (StatusType type in StatusTypes.Displayable)
        {
            if (type == StatusType.Shield) { continue; }

            int stacks = character.StatusStacks(type);

            if (stacks <= 0) { continue; }

            OverheadStatusIcon icon = IconAt(visible);
            icon.Show(statusIcons != null ? statusIcons.For(type) : null);
            PlaceIcon(icon.Rect, visible);

            visible++;
        }

        for (int i = visible; i < statusIconPool.Count; i++) { statusIconPool[i].gameObject.SetActive(false); }
    }

    private OverheadStatusIcon IconAt(int index)
    {
        while (statusIconPool.Count <= index) { statusIconPool.Add(Instantiate(statusIconPrefab, statusIconParent)); }

        statusIconPool[index].gameObject.SetActive(true);

        return statusIconPool[index];
    }

    /// Pivoted on its own left edge and grown rightward (positive x per index), so the first icon's
    /// left edge lands exactly on statusIconParent's anchor - which OverheadStatusIconWiring places at
    /// the health bar's own left edge, so the row reads as flush with the bar rather than floating to
    /// one side of it.
    private void PlaceIcon(RectTransform rect, int index)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(statusIconSize, statusIconSize);
        rect.anchoredPosition = new Vector2(index * (statusIconSize + statusIconSpacing), 0f);
    }
}
