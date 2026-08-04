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

        // Shield, Block and Parry are statuses like any other now, so they are asked for by type
        // rather than read off the character. Which of them earns a line of its own is this panel's
        // decision to make - the character has no opinion about it.
        healthText.text = $"{shown.Health}/{shown.MaxHealth}  Shield {shown.StatusStacks(StatusType.Shield)}";

        Status block = shown.FindStatus(StatusType.Block);
        blockText.text = block != null ? block.Describe() : "";

        Status parry = shown.FindStatus(StatusType.Parry);
        parryText.text = parry != null ? parry.Describe() : "";

        // Everything else, totem auras included - ActiveStatuses is what the combat rules see, so it
        // is what the player should see too.
        statusesText.text = "";
        foreach (Status status in shown.ActiveStatuses())
        {
            if (status.type is StatusType.Shield or StatusType.Block or StatusType.Parry) { continue; }

            string duration = status.turnsRemaining == Status.Indefinite ? "" : $" ({status.turnsRemaining})";
            statusesText.text += $"{status.Describe()}{duration}\n";
        }
    }

    private void OnDestroy()
    {
        if (BattleManager.Instance != null) { BattleManager.Instance.SelectedCharacterChanged -= ShowInfoFor; }
        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved -= OnActionResolved; }
    }
}
