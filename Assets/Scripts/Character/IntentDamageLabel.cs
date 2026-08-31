using TMPro;
using UnityEngine;

/// <summary>
/// The raw damage number beside an enemy's overhead intent icon - what CharacterOverheadViewer shows
/// alongside an Attack intent when GameSettings.ShowIntentDamage is on. See Card.OutgoingDamage for
/// what the number itself means.
///
/// A plain serializable class, not a MonoBehaviour, built at runtime the same way IntentRoll is - none
/// of the 28 enemy/boss/ally prefabs carry a TextMeshPro child today, and building one here means none
/// of them need editing to gain a number.
/// </summary>
[System.Serializable]
public class IntentDamageLabel
{
    [Tooltip("Font size, in the same canvas units as the icon it sits beside - the icon's own slot is "
             + "44x44, for a sense of scale.")]
    [SerializeField] private float fontSize = 28f;

    [Tooltip("Horizontal gap between the icon's own slot and the label's left edge.")]
    [SerializeField] private float gap = 6f;

    [SerializeField] private Color color = Color.white;

    [Tooltip("The label's own box - just needs to comfortably fit a couple of digits at fontSize.")]
    [SerializeField] private Vector2 size = new(60f, 44f);

    private TextMeshProUGUI text;

    /// <summary>
    /// Builds the label as a sibling of `slot`, never a child of it - `slot` is IntentRoll.Window,
    /// which wraps the authored icon Image and duplicates it into two rolling copies (see
    /// IntentRoll.Build). A label parented under the icon itself would be duplicated into both copies
    /// and roll off-screen with them; a sibling of the window it builds around is unaffected.
    ///
    /// `slot` is null until IntentRoll.Build has already run - CharacterOverheadViewer.Awake calls
    /// that first for exactly this reason. A null `slot` here is a silent no-op, the same contract
    /// IntentRoll.Play/Show use for a component nothing was built around.
    /// </summary>
    public void Build(RectTransform slot)
    {
        if (slot == null) { return; }

        Transform parent = slot.parent;

        GameObject go = new("IntentDamageLabel", typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.SetSiblingIndex(slot.GetSiblingIndex() + 1);
        go.layer = slot.gameObject.layer;

        // Same top-anchored scheme as the icon's own slot, so the pair moves together if Overhead is
        // ever resized - left-pivoted at the slot's own right edge plus a gap, vertically centred on
        // it regardless of how the icon's own pivot is set.
        rect.anchorMin = slot.anchorMin;
        rect.anchorMax = slot.anchorMin;
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = size;

        float rightEdge = slot.anchoredPosition.x + slot.sizeDelta.x * (1f - slot.pivot.x);
        float verticalCentre = slot.anchoredPosition.y + slot.sizeDelta.y * (0.5f - slot.pivot.y);
        rect.anchoredPosition = new Vector2(rightEdge + gap, verticalCentre);

        text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        // The overhead canvas must not eat tile clicks - same reason the icon Image itself is
        // authored with raycastTarget off.
        text.raycastTarget = false;

        go.SetActive(false);
    }

    /// <summary>
    /// Shows `damage` beside the icon, or hides the label outright for `damage` &lt;= 0 - a Move or
    /// Summon intent, the toggle turned off, or nothing committed at all. See
    /// CharacterOverheadViewer.RefreshIntentDamage, the only caller.
    /// </summary>
    public void Show(int damage)
    {
        if (text == null) { return; }

        bool visible = damage > 0;
        text.gameObject.SetActive(visible);

        if (visible) { text.text = damage.ToString(); }
    }
}
