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

    public void Setup(EquipmentData item, Action<EquipmentData> onChosen)
    {
        data = item;
        onClick = onChosen;

        if (icon != null) { icon.sprite = item != null ? item.icon : null; }
        if (nameLabel != null) { nameLabel.text = item != null ? item.equipmentName : string.Empty; }
        if (descriptionLabel != null) { descriptionLabel.text = item != null ? item.description : string.Empty; }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(Choose);
        }
    }

    private void Choose() => onClick?.Invoke(data);
}
