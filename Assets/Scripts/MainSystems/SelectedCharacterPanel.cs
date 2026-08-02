using TMPro;
using UnityEngine;

/// <summary>
/// Read-only readout of whatever character is selected - health, shield, block, parry, statuses.
/// Works for allies and enemies alike, since Character exposes the same properties for both.
///
/// Follows BattleManager.SelectedCharacterChanged the same way ActiveHandViewer follows
/// ActiveCharacterChanged. Re-reads the shown character whenever ActionManager.ActionResolved fires
/// rather than subscribing to per-stat events on Character - that is the one point in the pipeline
/// where anything about a character's stats could have changed, so it is the only refresh trigger
/// this needs. See ActionManager.ActionResolved's own doc comment.
/// </summary>
public class SelectedCharacterPanel : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;

    [SerializeField] private TextMeshProUGUI nameText;

    [SerializeField] private TextMeshProUGUI healthText;

    [SerializeField] private TextMeshProUGUI blockText;

    [SerializeField] private TextMeshProUGUI parryText;

    [SerializeField] private TextMeshProUGUI statusesText;

    private Character shown;

    private void Start()
    {
        BattleManager battle = BattleManager.Instance;

        if (battle == null)
        {
            Debug.LogError($"{name}: no BattleManager in the scene - nothing to show info for");
            return;
        }

        battle.SelectedCharacterChanged += ShowInfoFor;

        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved += OnActionResolved; }

        ShowInfoFor(battle.SelectedCharacter);
    }

    private void ShowInfoFor(Character character)
    {
        shown = character;
        Refresh();
    }

    private void OnActionResolved(GameAction action, ActionContext ctx) => Refresh();

    private void Refresh()
    {
        if (panelRoot == null) { return; }

        if (shown == null)
        {
            panelRoot.SetActive(false);
            return;
        }

        panelRoot.SetActive(true);
        nameText.text = shown.name;
        healthText.text = $"{shown.Health}/{shown.MaxHealth}  Shield {shown.Shield}";
        blockText.text = shown.BlockCharges > 0 ? $"Block {shown.BlockAmount} x{shown.BlockCharges}" : "";
        parryText.text = shown.ParryCharges > 0 ? $"Parry x{shown.ParryCharges}" : "";

        statusesText.text = "";
        foreach (Status status in shown.Statuses)
        {
            string duration = status.turnsRemaining == Status.Indefinite ? "" : $" ({status.turnsRemaining})";
            statusesText.text += $"{status.type} x{status.stacks}{duration}\n";
        }
    }

    private void OnDestroy()
    {
        if (BattleManager.Instance != null) { BattleManager.Instance.SelectedCharacterChanged -= ShowInfoFor; }
        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved -= OnActionResolved; }
    }
}
