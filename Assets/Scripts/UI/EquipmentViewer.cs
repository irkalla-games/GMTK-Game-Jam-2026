using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One equipment tile on a reward panel - icon, name, description, click to choose. The equipment
/// counterpart to CardViewer, but a plain UGUI element rather than a world-space sprite: equipment has
/// no board presence and no hand to sit in, so there is no reason to share CardViewer's sorting-layer
/// and hover-scale machinery built for a card that also has to look right mid-play.
/// </summary>
public class EquipmentViewer : MonoBehaviour
{
    [SerializeField] private Image icon;

    [SerializeField] private TMP_Text nameLabel;

    [SerializeField] private TMP_Text descriptionLabel;

    [SerializeField] private Button button;

    private EquipmentData data;

    /// Set by RewardPanel before the click can fire - mirrors CardViewer.clickOverride's contract,
    /// minus the "let it fall through to CardPlayManager" case, since nothing here is ever a hand card.
    private Action<EquipmentData> onClick;

    /// <summary>
    /// `replaced` is what this pick would displace - the item already sitting in the same single-
    /// occupant slot (Weapon/Armor/Hat/Boots), or null for an unlimited slot (Ring) or an empty one.
    /// Appended onto the description rather than a dedicated label: this tile has no board presence
    /// and no layout of its own worth risking a new child element over, and the skip button already
    /// standing next to it is the decline.
    /// </summary>
    public void Setup(EquipmentData item, EquipmentData replaced, Action<EquipmentData> onChosen)
    {
        data = item;
        onClick = onChosen;

        if (icon != null) { icon.sprite = item != null ? item.icon : null; }
        if (nameLabel != null) { nameLabel.text = item != null ? item.equipmentName : string.Empty; }

        if (descriptionLabel != null)
        {
            string description = item != null ? item.description : string.Empty;

            descriptionLabel.text = replaced != null
                ? $"{description}\n\nReplaces: {replaced.equipmentName}"
                : description;
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(Choose);
        }
    }

    private void Choose() => onClick?.Invoke(data);
}
