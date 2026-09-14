using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One step of an enemy's plan, as an intent readout draws it: an icon (rolled or snapped, see
/// IntentRoll), a damage number beside it, a badge per secondary effect (see IntentBadgeStack), and
/// the chevron connecting it to the step before it. IntentStrip owns a pool of these, one per visible
/// step, grown on demand and reused.
///
/// Slot 0 (the primary) and every step after it come from different factory methods on IntentStrip -
/// BuildPrimarySlot adopts the Image already authored on the prefab, BuildFollowUpSlot builds one from
/// scratch - but once built, both are this same type, and every later operation (Show, LayOut, Kill)
/// treats every slot identically. `Window` used to be two different things on two different types
/// today's primary icon's own IntentRoll.Window, and a follow-up's separate "IntentFollowUp" root
/// wrapping its own nested window; now every slot's positioning rect and its rolling window are the
/// same object for both cases - see IntentStrip's factory methods for how that is arranged.
/// </summary>
public class IntentSlot
{
    public readonly IntentRoll roll = new();
    public readonly IntentDamageLabel damageLabel = new();
    public readonly IntentBadgeStack badges = new();

    /// The chevron drawn to this slot's own left, connecting it to the step before it - inactive on
    /// slot 0, which has no predecessor, and whenever IntentIcons.Chevron is unauthored. Owned per-slot
    /// rather than pooled separately, so there is one list to hide/show in step with its slot rather
    /// than two walked in parallel.
    public Image leadIn;

    /// What this slot is currently showing, kept separately from the intent's own kind so the roll
    /// guard can key on the resolved sprite - a melee attack swapping to a ranged one changes the
    /// sprite without changing IntentKind.Attack, which a kind-keyed guard would miss entirely.
    public Sprite shownSprite;

    /// True the moment this slot is (re)activated for a plan that did not carry it across from the
    /// previous refresh - the signal to snap the icon in rather than roll it. Starts true so a brand
    /// new slot's very first icon (including slot 0's, on a freshly spawned enemy) snaps rather than
    /// falling in while a spawn animation is still playing - the same rule a re-activated follow-up
    /// slot needs for the same reason: a slot appearing should never look like the kind it is showing
    /// just changed.
    public bool justActivated = true;

    /// This step's raw outgoing damage, or 0 to hide the number entirely - computed once per Show()
    /// call and held here rather than painted immediately, because painting a damage label needs this
    /// slot's window at its *final* position for the frame, which is not known until IntentStrip.LayOut
    /// has run. See IntentStrip.PaintSlot.
    public int damageAmount;

    /// This step's riders (Root, Vulnerable, and the like), refreshed in place every Show() call - see
    /// IntentStrip.Show, which calls Card.OutgoingRiders into this list directly rather than through a
    /// shared buffer, for the same reason damageAmount above is held rather than painted immediately:
    /// IntentStrip.LayOut needs every step's own rider count before it can lay any of them out, since a
    /// two-rider badge stack reaches left past its own icon and has to be reserved for before that icon
    /// (or the one before it) is positioned.
    public readonly List<IntentRider> riders = new();

    /// The rect every later measurement (IntentStrip.LayOut, badge placement, the damage label) is
    /// built from - IntentRoll's own masked window, which already sits at the exact position and size
    /// the slot occupies, for both the adopted primary icon and a freshly built follow-up.
    public RectTransform Window => roll.Window;

    public static float LeftEdge(RectTransform r) => r.anchoredPosition.x - r.sizeDelta.x * r.pivot.x;

    public static float RightEdge(RectTransform r) => r.anchoredPosition.x + r.sizeDelta.x * (1f - r.pivot.x);

    public static float BottomEdge(RectTransform r) => r.anchoredPosition.y - r.sizeDelta.y * r.pivot.y;

    /// <summary>
    /// The inverse of LeftEdge: writes `r.anchoredPosition` so that its left edge lands at `leftEdge`,
    /// whatever `r`'s own pivot.x happens to be. Every follow-up window has pivot.x 0 (LeftEdge and
    /// anchoredPosition.x already agree, so this is a no-op past the assignment), but slot 0 keeps
    /// whatever pivot the host originally authored its icon with - 0.5 on the overhead, 0 on the
    /// screen-space panel - so IntentStrip.LayOut has to go through this rather than writing a
    /// "local left edge" value straight into anchoredPosition.x, or a centred row would land the
    /// overhead's own primary icon half its own width off from where the row's math intended it.
    /// </summary>
    public static void PlaceByLeftEdge(RectTransform r, float leftEdge, float y)
    {
        r.anchoredPosition = new Vector2(leftEdge + r.sizeDelta.x * r.pivot.x, y);
    }

    /// <summary>
    /// Hides everything about this slot and marks it to snap, not roll, the next time it is shown -
    /// IntentStrip.Show calls this on a slot that used to be part of a longer plan and no longer is.
    /// </summary>
    public void Deactivate()
    {
        Window.gameObject.SetActive(false);
        if (leadIn != null) { leadIn.gameObject.SetActive(false); }

        damageLabel.Show(0);
        badges.Hide();
        shownSprite = null;
        damageAmount = 0;
        riders.Clear();
        justActivated = true;
    }

    public void Kill() => roll.Kill();
}
