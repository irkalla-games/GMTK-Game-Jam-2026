using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One entry in the debug browser's grid - a card, a piece of equipment or an enemy - as art, a name
/// and a small badge in the corner.
///
/// Deliberately knows nothing about what it is showing. DebugPanel decides what the badge means per
/// mode (a card's cost, an equipment's rarity initial, an enemy's health), which is what lets one grid
/// and one pool serve all three lists instead of three of everything.
///
/// A uGUI tile rather than a real CardViewer: that is world-space SpriteRenderers whose every tween
/// runs on scaled time, so it can neither render into this canvas nor animate while anything is frozen.
/// What a tile has no room for, the detail pane beside the grid says instead.
///
/// Pooled and reused, never destroyed, so Bind is called many times on one instance and must leave no
/// state behind.
/// </summary>
public class DebugTile : MonoBehaviour
{
    [SerializeField] private Button button;

    [SerializeField] private Image background;

    [SerializeField] private Image art;

    [SerializeField] private TMP_Text nameLabel;

    [SerializeField] private TMP_Text badgeLabel;

    /// <summary>
    /// Points this tile at one entry. `index` is the caller's own list position - the tile does not care
    /// which list that is.
    /// </summary>
    public void Bind(int index, Sprite icon, string title, string badge, Action<int> onClick)
    {
        if (nameLabel != null) { nameLabel.text = title; }

        if (badgeLabel != null)
        {
            badgeLabel.text = badge;

            // An empty badge hides its backing text rather than drawing an empty corner box.
            badgeLabel.gameObject.SetActive(!string.IsNullOrEmpty(badge));
        }

        if (art != null)
        {
            art.sprite = icon;

            // Art missing is normal - most equipment and every enemy prefab has no icon here. Disabling
            // the Image leaves the tile's own background showing, which reads as "no art" rather than as
            // a white box bug.
            art.enabled = icon != null;
        }

        if (button == null) { return; }

        // Cleared first: a pooled tile has been bound before and would otherwise accumulate one callback
        // per rebuild, firing every one of them on a click.
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onClick?.Invoke(index));
    }

    /// <summary>
    /// Tints the tile to show it is the one the detail pane is describing. A tint rather than a swapped
    /// sprite, so the tile needs no asset reference of its own.
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (background == null) { return; }

        background.color = selected ? PanelPalette.Gold : Color.white;
    }

    /// Assigns the child graphics, so the wiring command does not need a SerializedObject write per field.
    public void SetGraphics(Button tileButton, Image tileBackground, Image tileArt, TMP_Text tileName,
        TMP_Text tileBadge)
    {
        button = tileButton;
        background = tileBackground;
        art = tileArt;
        nameLabel = tileName;
        badgeLabel = tileBadge;
    }
}
