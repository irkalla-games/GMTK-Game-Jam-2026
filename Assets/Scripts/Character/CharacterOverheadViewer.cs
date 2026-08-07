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

    private Character character;

    private void Awake() => character = GetComponent<Character>();

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
        RefreshIntent();
    }

    private void OnDestroy()
    {
        if (character != null)
        {
            character.StatsChanged -= OnStatsChanged;
            character.IntentChanged -= OnIntentChanged;
        }

        if (BattleManager.Instance != null) { BattleManager.Instance.TurnAdvanced -= RefreshBar; }
    }

    private void OnStatsChanged(Character _) => RefreshBar();

    private void OnIntentChanged(Character _) => RefreshIntent();

    private void RefreshBar()
    {
        float max = Mathf.Max(1, character.MaxHealth);
        int shield = character.StatusStacks(StatusType.Shield);

        if (healthFill != null) { healthFill.fillAmount = Mathf.Clamp01(character.Health / max); }

        if (shieldFill != null)
        {
            shieldFill.fillAmount = Mathf.Clamp01(shield / max);
            shieldFill.enabled = shield > 0;
        }
    }

    private void RefreshIntent()
    {
        if (intentIcon == null) { return; }

        Sprite sprite = icons != null ? icons.For(character.CommittedIntent.kind) : null;

        intentIcon.sprite = sprite;
        intentIcon.enabled = sprite != null && !character.CommittedIntent.IsWait;
    }
}
