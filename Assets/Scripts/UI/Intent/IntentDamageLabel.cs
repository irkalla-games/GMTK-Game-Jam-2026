using TMPro;
using UnityEngine;

/// <summary>
/// The raw damage number beside one step of an intent readout - what IntentStrip shows alongside an
/// Attack step when GameSettings.ShowIntentDamage is on. See Card.OutgoingDamage for what the number
/// itself means.
///
/// A plain class, not a MonoBehaviour and not [System.Serializable] - IntentStrip owns a pool of
/// these, one per slot, and every tunable (font size, gap, colour, box size) is a strip-level setting
/// passed into Build rather than a per-instance field, so every slot's label matches regardless of
/// which slot it happens to be. Built at runtime the same way IntentRoll is - no prefab carries a
/// TextMeshPro child for this today, and building one here means none of them need editing to gain a
/// number.
/// </summary>
public class IntentDamageLabel
{
    private TextMeshProUGUI text;

    /// Whether the label is currently showing a number - what IntentStrip reads to decide whether this
    /// slot's reserved width should include the label's own box when laying out the row.
    public bool Visible => text != null && text.gameObject.activeSelf;

    /// The label's own RectTransform, exposed so IntentStrip can read its right edge - see Build's own
    /// comment on why this shares an anchor point with the slot it was built beside, which is what
    /// makes that edge comparable to the slot's own anchoredPosition.x with no extra conversion.
    public RectTransform Rect => text != null ? (RectTransform)text.transform : null;

    /// <summary>
    /// Builds the label as a sibling of `slot`, never a child of it - `slot` is IntentRoll.Window,
    /// which wraps the icon Image and duplicates it into two rolling copies (see IntentRoll.Build). A
    /// label parented under the icon itself would be duplicated into both copies and roll off-screen
    /// with them; a sibling of the window it builds around is unaffected.
    ///
    /// Sets up everything about this label except its position - see Reposition for that, and for why
    /// it has to be a separate call rather than done here. `slot` is null until IntentRoll.Build has
    /// already run for the same icon - IntentStrip builds each slot's trio in that order for exactly
    /// this reason. A null `slot` here is a silent no-op, the same contract IntentRoll.Play/Show use
    /// for a component nothing was built around.
    /// </summary>
    public void Build(RectTransform slot, float fontSize, Color color, Vector2 size)
    {
        if (slot == null) { return; }

        Transform parent = slot.parent;

        GameObject go = new("IntentDamageLabel", typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.SetSiblingIndex(slot.GetSiblingIndex() + 1);
        go.layer = slot.gameObject.layer;

        // Same top-anchored scheme as the icon's own slot, so the pair moves together if the strip is
        // ever resized - left-pivoted, vertically centred once Reposition sets an actual position.
        rect.anchorMin = slot.anchorMin;
        rect.anchorMax = slot.anchorMin;
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = size;

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
    /// Moves this label to sit at `slot`'s current right edge plus `gap`, vertically centred on it.
    /// A separate call from Build, and one IntentStrip.LayOut makes on *every* refresh rather than
    /// once: a follow-up slot's window does not have its final position for the row until LayOut has
    /// walked the whole plan and centred it, so a label positioned once, at Build time, against that
    /// window's placeholder pre-layout position would end up wherever the window happened to be
    /// sitting the instant this slot was first created - which is exactly the bug this fixes, where a
    /// second step's damage number landed to the left of its icon instead of the right, because it was
    /// built against x = 0 and the window was later moved right without the label following.
    /// </summary>
    public void Reposition(RectTransform slot, float gap)
    {
        if (text == null) { return; }

        RectTransform rect = (RectTransform)text.transform;

        float rightEdge = slot.anchoredPosition.x + slot.sizeDelta.x * (1f - slot.pivot.x);
        float verticalCentre = slot.anchoredPosition.y + slot.sizeDelta.y * (0.5f - slot.pivot.y);
        rect.anchoredPosition = new Vector2(rightEdge + gap, verticalCentre);
    }

    /// <summary>
    /// Shows `damage` beside the icon, or hides the label outright for `damage` &lt;= 0 - a Move or
    /// Summon intent, the toggle turned off, or nothing committed at all. See IntentStrip.Show, the
    /// only caller.
    /// </summary>
    public void Show(int damage)
    {
        if (text == null) { return; }

        bool visible = damage > 0;
        text.gameObject.SetActive(visible);

        if (visible) { text.text = damage.ToString(); }
    }
}
