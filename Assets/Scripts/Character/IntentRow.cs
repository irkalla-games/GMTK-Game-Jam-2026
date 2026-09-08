using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The follow-up icons beside an enemy's primary overhead intent icon - one more per action point past
/// the first, so a two-action-point boss telegraphs both of its actions instead of only the one
/// CharacterOverheadViewer's own intentIcon/intentRoll/damageLabel already show.
///
/// Owns slots 1..N-1 of the row only. Slot 0 - the primary icon, its roll and its damage label - stays
/// exactly as authored on every prefab and is never touched here; that is what keeps every one-action-
/// point enemy pixel-identical to before this existed. Each follow-up slot is a small self-contained
/// clone of that same trio (an IntentRoll wrapping a fresh Image, plus its own IntentDamageLabel), built
/// from scratch rather than by cloning the authored icon - none of the 29 enemy/boss/ally prefabs need
/// editing to gain a row, the same reason IntentRoll/IntentDamageLabel build themselves at runtime.
///
/// A plain serializable class, not a MonoBehaviour - CharacterOverheadViewer owns exactly one, alongside
/// its own intentRoll and damageLabel.
/// </summary>
[System.Serializable]
public class IntentRow
{
    [Tooltip("Uniform scale of every follow-up slot (icon, backing plate and damage label together) "
             + "relative to the primary icon's own 44x44 slot - smaller reads as \"less immediate\" "
             + "than the action about to happen right now.")]
    [SerializeField] private float followUpScale = 0.7f;

    [Tooltip("Alpha multiplier on a follow-up slot's icon and damage label - dimmer than the primary, "
             + "same reasoning as followUpScale. The backing plate is left at its own full alpha so "
             + "the row stays readable against busy background art.")]
    [SerializeField] private float followUpAlpha = 0.65f;

    [Tooltip("Horizontal gap, in the primary icon's own canvas units, between one slot's right edge "
             + "and the next slot's left edge - including between the primary and the first follow-up.")]
    [SerializeField] private float slotGap = 6f;

    private static readonly Color BgColor = new(0f, 0f, 0f, 0.6f);

    /// One follow-up slot: a small clone of intentIcon/intentRoll/damageLabel, grown on demand and
    /// reused, never destroyed - same pooling contract CharacterOverheadViewer.statusIconPool follows.
    private class Slot
    {
        public RectTransform root;
        public IntentRoll roll = new();
        public IntentDamageLabel damageLabel = new();
        public IntentKind shownKind = IntentKind.Wait;

        /// True the moment this slot is (re)activated for a plan that did not carry it across from
        /// the previous refresh - the signal to snap the icon in rather than roll it, matching the
        /// primary's own "no roll on spawn" rule for exactly the same reason: a slot popping into
        /// existence should not look like the kind it is showing just changed.
        public bool justActivated;
    }

    private readonly List<Slot> pool = new();

    private RectTransform primaryWindow;
    private IntentDamageLabel primaryDamageLabel;
    private Transform parent;
    private int layer;

    /// <summary>
    /// Records the geometry every follow-up slot is built and placed from - the primary icon's own
    /// window (see IntentRoll.Build) and its damage label. Call once, from
    /// CharacterOverheadViewer.Awake, after both of those have already been built; creates no slots
    /// yet, since most enemies never need one.
    /// </summary>
    public void Build(RectTransform primaryWindow, IntentDamageLabel primaryDamageLabel)
    {
        this.primaryWindow = primaryWindow;
        this.primaryDamageLabel = primaryDamageLabel;

        if (primaryWindow == null) { return; }

        parent = primaryWindow.parent;
        layer = primaryWindow.gameObject.layer;
    }

    /// <summary>
    /// Shows every step of `plan` past the first: rolls each follow-up's icon (only when its kind
    /// actually changed, or snaps it with no roll if the slot was just activated), updates its damage
    /// number, and lays the whole visible row out left-to-right from the primary icon's own right
    /// edge - which itself may have moved because its own damage label just appeared or disappeared.
    ///
    /// Safe to call on every StatsChanged/ActionResolved tick as well as every IntentChanged one - a
    /// step whose kind has not changed since the last call costs one comparison and repaints only its
    /// damage number, the same guard CharacterOverheadViewer.RefreshIntent applies to the primary icon.
    /// </summary>
    public void Show(IReadOnlyList<Intent> plan, IntentIcons icons, Character self)
    {
        if (primaryWindow == null) { return; }

        int followUps = Mathf.Max(0, plan.Count - 1);

        for (int i = 0; i < followUps; i++)
        {
            Slot slot = SlotAt(i);
            Intent intent = plan[i + 1];

            if (!slot.root.gameObject.activeSelf)
            {
                slot.root.gameObject.SetActive(true);
                slot.justActivated = true;
            }

            Sprite sprite = icons != null ? icons.For(intent.kind) : null;

            if (slot.justActivated)
            {
                slot.roll.Show(sprite);
                slot.shownKind = intent.kind;
                slot.justActivated = false;
            }
            else if (intent.kind != slot.shownKind)
            {
                Sprite from = icons != null ? icons.For(slot.shownKind) : null;
                slot.shownKind = intent.kind;
                slot.roll.Play(from, sprite);
            }

            bool showDamage = GameSettings.ShowIntentDamage
                               && intent.kind == IntentKind.Attack
                               && intent.card != null
                               && GridManager.Instance != null;

            slot.damageLabel.Show(
                showDamage ? intent.card.OutgoingDamage(self, GridManager.Instance.GetTile(intent.target)) : 0);
        }

        for (int i = followUps; i < pool.Count; i++)
        {
            if (!pool[i].root.gameObject.activeSelf) { continue; }

            pool[i].root.gameObject.SetActive(false);
            pool[i].shownKind = IntentKind.Wait;
        }

        LayOut(followUps);
    }

    /// Kills every slot's in-flight roll - CharacterOverheadViewer.OnDestroy calls this alongside its
    /// own intentRoll.Kill(), for the same reason: an enemy is destroyed the instant it dies, possibly
    /// mid-roll.
    public void Kill()
    {
        foreach (Slot slot in pool) { slot.roll.Kill(); }
    }

    private Slot SlotAt(int index)
    {
        while (pool.Count <= index) { pool.Add(BuildSlot()); }

        return pool[index];
    }

    /// <summary>
    /// Builds one inactive follow-up slot from scratch: a backing plate, an Image wrapped in its own
    /// IntentRoll, and its own IntentDamageLabel - the same trio CharacterOverheadViewer's authored
    /// intentIcon/intentRoll/damageLabel already are, at followUpScale.
    ///
    /// The icon (and, through IntentRoll.Build, the window built around it) is given a real fixed
    /// sizeDelta equal to the primary's own, centred on the slot root, rather than a stretched
    /// full-parent anchor - IntentDamageLabel.Build reads its slot's sizeDelta.x to place the number
    /// beside it, and a stretched anchor reports that as zero. localScale on the slot root - not a
    /// smaller sizeDelta - is what makes the whole thing render smaller, which is also what makes the
    /// icon, its plate and its number shrink together with no extra math anywhere else.
    /// </summary>
    private Slot BuildSlot()
    {
        Vector2 iconSize = primaryWindow.sizeDelta;

        GameObject rootGo = new("IntentFollowUp", typeof(RectTransform));
        RectTransform root = (RectTransform)rootGo.transform;
        root.SetParent(parent, false);
        rootGo.layer = layer;

        root.anchorMin = primaryWindow.anchorMin;
        root.anchorMax = primaryWindow.anchorMax;
        root.pivot = primaryWindow.pivot;
        root.sizeDelta = iconSize;
        root.localScale = Vector3.one * followUpScale;

        GameObject bgGo = new("IntentBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform bgRect = (RectTransform)bgGo.transform;
        bgRect.SetParent(root, false);
        bgGo.layer = layer;
        bgRect.anchorMin = new Vector2(0.5f, 0.5f);
        bgRect.anchorMax = new Vector2(0.5f, 0.5f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.sizeDelta = iconSize;
        bgRect.anchoredPosition = Vector2.zero;

        Image bg = bgGo.GetComponent<Image>();
        bg.color = BgColor;
        bg.raycastTarget = false;

        GameObject iconGo = new("IntentIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform iconRect = (RectTransform)iconGo.transform;
        iconRect.SetParent(root, false);
        iconGo.layer = layer;
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = iconSize;
        iconRect.anchoredPosition = Vector2.zero;

        Image icon = iconGo.GetComponent<Image>();
        icon.color = new Color(1f, 1f, 1f, followUpAlpha);
        icon.raycastTarget = false;

        Slot slot = new() { root = root };
        slot.roll.Build(icon);
        slot.damageLabel.Build(slot.roll.Window);

        rootGo.SetActive(false);

        return slot;
    }

    /// <summary>
    /// Places every currently-visible follow-up slot left-to-right, starting just past the primary
    /// icon's own right edge - which itself may sit further right than its bare window whenever its
    /// own damage label is showing a number, so this always re-measures rather than assuming a fixed
    /// primary width. Each slot's own damage label, if visible, pushes the next slot's start out in
    /// exactly the same way.
    /// </summary>
    private void LayOut(int followUps)
    {
        float halfIconVisual = primaryWindow.sizeDelta.x * 0.5f * followUpScale;
        float runningRight = PrimaryRightEdge();

        for (int i = 0; i < followUps; i++)
        {
            Slot slot = pool[i];

            float centre = runningRight + slotGap + halfIconVisual;
            slot.root.anchoredPosition = new Vector2(centre, primaryWindow.anchoredPosition.y);

            // LocalRightEdge is measured in this slot's own unscaled local units, relative to its
            // root's centre - see BuildSlot, where the icon (and everything IntentDamageLabel.Build
            // anchors beside it) is centred on that same origin - so it has to be scaled by
            // followUpScale before it means anything in the shared frame `centre` is expressed in.
            runningRight = centre + LocalRightEdge(slot) * followUpScale;
        }
    }

    /// The primary icon's own right edge, in the coordinate frame every slot's root is placed in -
    /// its damage label's right edge when one is showing a number, else the bare icon window's.
    private float PrimaryRightEdge()
    {
        if (primaryDamageLabel != null && primaryDamageLabel.Visible && primaryDamageLabel.Rect != null)
        {
            RectTransform r = primaryDamageLabel.Rect;
            return r.anchoredPosition.x + r.sizeDelta.x * (1f - r.pivot.x);
        }

        return primaryWindow.anchoredPosition.x + primaryWindow.sizeDelta.x * (1f - primaryWindow.pivot.x);
    }

    /// A follow-up slot's own damage label (if showing a number) or its icon window's right edge,
    /// measured in that slot's own unscaled local units relative to its root's centre - see LayOut,
    /// which scales this into the shared frame before using it.
    private static float LocalRightEdge(Slot slot)
    {
        if (slot.damageLabel.Visible && slot.damageLabel.Rect != null)
        {
            RectTransform r = slot.damageLabel.Rect;
            return r.anchoredPosition.x + r.sizeDelta.x * (1f - r.pivot.x);
        }

        RectTransform window = slot.roll.Window;
        return window.anchoredPosition.x + window.sizeDelta.x * (1f - window.pivot.x);
    }
}
