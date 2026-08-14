using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns the small canvas above a character's head: a health bar with a shield fill layered over it,
/// and - enemies only - an icon for the category of action they have committed to this turn.
///
/// Character raises StatsChanged and IntentChanged and knows nothing about this class - the same
/// contract as CardDrawn/CardDiscarded and ActiveHandViewer. This is the thing worth not repeating
/// from the old design, where Character wrote `healthBar.text` directly from seven places with no
/// null guard.
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

    private Character character;

    /// What the icon is currently showing, kept separately from character.CommittedIntent.kind so
    /// RefreshIntent can tell a real change from a re-assignment of the same kind - the refresh pass in
    /// BattleManager already guards this on its own side, but TurnStart's re-commit does not.
    private IntentKind shownKind;

    private void Awake()
    {
        character = GetComponent<Character>();

        if (intentIcon != null) { intentRoll.Build(intentIcon); }
    }

    private void Start()
    {
        character.StatsChanged += OnStatsChanged;
        character.IntentChanged += OnIntentChanged;

        if (BattleManager.Instance != null) { BattleManager.Instance.TurnAdvanced += RefreshBar; }

        // A pull, not just a tidy default. SpawnParty calls SetHealth on the frame it instantiates a
        // hero - before that hero's own Start runs - so the StatsChanged raise from it fires into an
        // empty invocation list. Same story for a wave enemy's first CommittedIntent. Without this
        // pull, a hero arriving hurt from the previous level would show a full bar until the next
        // stat change, and a freshly spawned enemy would wear no icon until its second turn.
        RefreshBar();

        // Set directly rather than through RefreshIntent/intentRoll.Play - a freshly spawned enemy's
        // first icon should not fall in while its own spawn scale-in is still playing.
        shownKind = character.CommittedIntent.kind;

        if (intentIcon != null)
        {
            intentRoll.Show(icons != null ? icons.For(shownKind) : null);
        }
    }

    private void OnDestroy()
    {
        if (character != null)
        {
            character.StatsChanged -= OnStatsChanged;
            character.IntentChanged -= OnIntentChanged;
        }

        if (BattleManager.Instance != null) { BattleManager.Instance.TurnAdvanced -= RefreshBar; }

        intentRoll.Kill();
    }

    private void OnStatsChanged(Character _) => RefreshBar();

    private void OnIntentChanged(Character _) => RefreshIntent();

    /// The arithmetic lives in HealthBarFill, shared with SelectedCharacterPanel - the two bars show
    /// the same numbers and must not be able to disagree about what they mean.
    private void RefreshBar()
    {
        HealthBarFill.Apply(healthFill, shieldFill, character);
    }

    private void RefreshIntent()
    {
        if (intentIcon == null) { return; }

        IntentKind next = character.CommittedIntent.kind;

        // Guards the case IntentChanged does not: BattleManager's own refresh pass already skips
        // assigning an unchanged kind, but TurnStart re-commits every enemy from scratch every round,
        // including Wait-to-Wait, and CommittedIntent's setter has no equality check of its own.
        if (next == shownKind) { return; }

        Sprite from = icons != null ? icons.For(shownKind) : null;
        Sprite to = icons != null ? icons.For(next) : null;

        shownKind = next;
        intentRoll.Play(from, to);
    }
}
