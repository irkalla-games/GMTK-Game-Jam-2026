using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One host's whole intent readout: every step of a committed plan, laid out left-to-right as equal-
/// size icons connected by chevrons, each with a damage number and a corner badge per secondary
/// effect. Both CharacterOverheadViewer (the world-space bar above an enemy) and
/// SelectedCharacterPanel (the screen-space enemy plate) own one of these and drive it from their own
/// Character - see Build's `rolls` argument for the one behavioural difference between the two hosts.
///
/// A [System.Serializable] class rather than a MonoBehaviour, following IntentRoll/IntentDamageLabel's
/// own precedent - the host owns exactly one, alongside its other serialized fields.
///
/// Every size below is a fraction of the primary icon's own measured width, never an absolute unit -
/// the overhead's icon is 44px, the panel's is 32px, and one shared set of absolute pixel values could
/// only ever look right on one of them. All of this is a brand new serialized block with no field name
/// any existing prefab or scene already writes, so every value here deserializes to exactly the
/// initialiser below everywhere this strip is used - see the class doc comment on
/// CharacterOverheadViewer's old intentRoll/damageLabel/intentRow fields, which this block replaces.
/// </summary>
[System.Serializable]
public class IntentStrip
{
    [Tooltip("Gap on both sides of the connecting chevron between two steps - between a step's own "
             + "reserved width (icon plus whatever a damage label or badge stack makes it overhang) "
             + "and the chevron, and between the chevron and the next step. A fraction of the primary "
             + "icon's own width.")]
    [SerializeField] private float slotGapScale = 0.18f;

    [Tooltip("The connecting chevron's size, as a fraction of the primary icon's own width. No "
             + "chevron is drawn at all while IntentIcons.Chevron is left unauthored.")]
    [SerializeField] private float chevronScale = 0.36f;

    [Tooltip("Alpha multiplier on a follow-up step's icon and lead-in chevron - dimmer than the "
             + "primary so the row still reads as \"next\" then \"after that\" even though every step "
             + "is now the same size. The backing plate, damage label and any badges are left at "
             + "their own full alpha so the row stays readable against busy background art.")]
    [SerializeField] private float followUpAlpha = 0.4f;

    [Tooltip("Vertical nudge applied once, at build time, to every step and everything built beside "
             + "it - the escape hatch for a host whose badges hang down into whatever sits just below "
             + "the row (a health bar, a status-chip row). 0 leaves the row exactly where authored.")]
    [SerializeField] private float rowLift;

    [Header("Damage number")]
    [SerializeField] private float damageFontScale = 0.64f;

    [SerializeField] private float damageGapScale = 0.14f;

    [SerializeField] private float damageBoxWidthScale = 1.36f;

    [SerializeField] private Color damageColor = Color.white;

    [Header("Rider badges")]
    [Tooltip("Corner badge size, as a fraction of the primary icon's own width.")]
    [SerializeField] private float badgeScale = 0.5f;

    [Tooltip("How much of a badge's own size sits over the icon it belongs to, on both axes - the "
             + "rest overhangs past the icon's own right and bottom edges. 0 would sit the badge "
             + "entirely outside the icon; 1 would hide it entirely behind the icon.")]
    [SerializeField] private float badgeOverlap = 0.55f;

    [SerializeField] private float badgeSpacingScale = 0.05f;

    [SerializeField] private float badgeNumberGapScale = 0.08f;

    [SerializeField] private float badgeNumberWidthScale = 0.34f;

    [SerializeField] private float badgeNumberFontScale = 0.4f;

    [Tooltip("Riders past this many are dropped, in the order Card.OutgoingRiders found them - the "
             + "same bargain StatusIcons already strikes for a status with no art: a rider you have "
             + "not made room for costs you the badge, not the widget. Unreachable with today's "
             + "content, whose richest card (VoidBarrier) carries 2.")]
    [SerializeField] private int maxBadges = 3;

    [Tooltip("A rider landing on whoever this card is aimed at - Root, Vulnerable, the ordinary case.")]
    [SerializeField] private Color targetAimedBadgeColor = new(0.46f, 0.12f, 0.14f, 0.92f);

    [Tooltip("A rider the card applies to the caster itself (entry.aimsAt == Source) - Strength while "
             + "attacking, say. A different colour from targetAimedBadgeColor so \"it curses you\" and "
             + "\"it buffs itself\" never read as the same badge.")]
    [SerializeField] private Color selfAimedBadgeColor = new(0.16f, 0.32f, 0.48f, 0.92f);

    [Header("Roll")]
    [Tooltip("Seconds an icon takes to fall in and push the previous one out. Never consulted when "
             + "this strip was built with rolls: false.")]
    [SerializeField] private float rollDuration = 0.5f;

    [SerializeField] private Ease rollEase = Ease.OutCubic;

    /// The name of the backing plate all 29 enemy/boss/ally prefabs already author beside their intent
    /// icon - a plain Image sibling at the icon's own rect. Slot 0 adopts that object rather than
    /// building a second one over the top of it (see BuildSlotExtras), the same way IntentRoll adopts
    /// the authored icon Image itself rather than replacing it. A host with no such sibling - the
    /// screen-space enemy plate, which never had one - gets one built instead.
    private const string AuthoredPlateName = "IntentBg";

    /// Fallback colour for a plate built from scratch, when there is no authored one to copy. The same
    /// look the old follow-up slots' own IntentBg used.
    private static readonly Color SlotBackingColor = new(0f, 0f, 0f, 0.6f);

    private readonly List<IntentSlot> pool = new();

    /// Pass-1 scratch space for LayOut's centring pre-pass - each visible slot's own x, and each
    /// follow-up's leading chevron's x, both relative to slot 0 sitting at a local origin of 0. Pooled
    /// rather than allocated per Show() call for the same reason every other pool in this file is.
    private readonly List<float> slotX = new();
    private readonly List<float> chevronX = new();

    private bool rolls;
    private Transform parent;
    private Vector2 anchorPoint;
    private int layer;

    /// The authored icon's own paint, copied so a from-scratch follow-up icon renders identically to
    /// it rather than falling back to Image's own defaults - SelectedCharacterPanel gets this for
    /// free today by cloning intentIcon directly; building from scratch loses it unless copied here.
    private Color primaryColor;
    private Image.Type primaryImageType;
    private bool primaryPreserveAspect;
    private Material primaryMaterial;

    /// Slot 0's backing plate's own paint, copied for the same reason primaryColor above is - a
    /// follow-up's plate is built from scratch and should match whatever slot 0 adopted or built.
    private Color primaryPlateColor = SlotBackingColor;
    private Sprite primaryPlateSprite;

    /// Whether the host authored a backing plate beside its icon at all. A follow-up step gets a plate
    /// only when slot 0 had one to match - the overhead prefabs all author one, the screen-space enemy
    /// plate never did, and giving that one plates now would put a black box behind an icon that has
    /// always sat directly on the panel.
    private bool primaryHasPlate;

    /// Where slot 0's icon was authored to sit, captured once at Build - the fixed origin LayOut lays
    /// the whole row out from and centres it on. Captured rather than re-read per frame because LayOut
    /// *moves* slot 0 whenever the row is longer than one step, so reading its live position would
    /// compound that shift a little further on every refresh.
    private float primaryLeftEdge;

    /// Slot 0's own window - what a hover, a tooltip anchor or the tutorial spotlight should read as
    /// "the intent icon", regardless of which host built this strip. Null until Build has run.
    public RectTransform PrimaryRect => pool.Count > 0 ? pool[0].Window : null;

    private float SlotWidth => pool.Count > 0 ? pool[0].Window.sizeDelta.x : 0f;

    /// <summary>
    /// Adopts `authored` - the single Image the host already exposes in the Inspector - as slot 0, and
    /// prepares this strip to grow follow-up slots to its right as a plan needs them.
    ///
    /// `rolls` is a property of which host this is, not a per-prefab knob: CharacterOverheadViewer
    /// passes true (an enemy's own icon changing should visibly roll), SelectedCharacterPanel passes
    /// false (the *shown character* can change between two refreshes there, and rolling from one
    /// enemy's icon to a different enemy's would read as a change in that enemy's intent rather than a
    /// change of who is being looked at). Kept out of the serialized fields above so the block stays
    /// free of a key that would mean nothing on whichever host does not use it.
    /// </summary>
    public void Build(Image authored, bool rolls)
    {
        if (authored == null) { return; }

        this.rolls = rolls;

        primaryColor = authored.color;
        primaryImageType = authored.type;
        primaryPreserveAspect = authored.preserveAspect;
        primaryMaterial = authored.material;

        // Found before roll.Build runs, while the authored icon is still sitting among its own
        // siblings - that call reparents it away into the masked window it creates.
        Image authoredPlate = FindAuthoredPlate(authored);

        primaryHasPlate = authoredPlate != null;

        if (primaryHasPlate)
        {
            primaryPlateColor = authoredPlate.color;
            primaryPlateSprite = authoredPlate.sprite;
        }

        IntentSlot slot0 = new();
        slot0.roll.Build(authored);

        parent = slot0.Window.parent;
        anchorPoint = slot0.Window.anchorMin;
        layer = slot0.Window.gameObject.layer;

        // Applied once, here, rather than every LayOut - rowLift is a fixed authoring choice for the
        // lifetime of this strip, and everything built below (the damage label, the badges) reads
        // this window's position at build time, so baking the lift in now is what every later
        // measurement inherits for free with no separate "+rowLift" anywhere else.
        if (!Mathf.Approximately(rowLift, 0f))
        {
            RectTransform window = slot0.Window;
            window.anchoredPosition = new Vector2(window.anchoredPosition.x, window.anchoredPosition.y + rowLift);
        }

        primaryLeftEdge = IntentSlot.LeftEdge(slot0.Window);

        BuildSlotExtras(slot0, authoredPlate);
        pool.Add(slot0);
    }

    /// The backing plate the host already authored beside its intent icon, or null for a host that
    /// never had one - see AuthoredPlateName. Looked up by name among the icon's own siblings, which is
    /// what every one of the 29 prefabs carrying one calls it; a rename there costs the adoption and
    /// falls back to building a plate, never an error.
    private static Image FindAuthoredPlate(Image icon)
    {
        Transform parent = icon.transform.parent;

        if (parent == null) { return null; }

        Transform found = parent.Find(AuthoredPlateName);

        if (found == null) { return null; }

        Image plate = found.GetComponent<Image>();

        return plate != null ? plate : null;
    }

    /// <summary>
    /// Repaints every visible step of `plan` - icon (rolled only if its resolved sprite actually
    /// changed), damage number, rider badges, and the chevrons connecting them - then lays the whole
    /// row out, centred as a whole, from slot 0's own originally authored position. Safe to call on
    /// every IntentChanged/StatsChanged/ActionResolved/TurnAdvanced tick; a step whose sprite has not
    /// changed repaints only its number and badges, and a plan shorter than the row currently is simply
    /// hides the steps past its end rather than rebuilding anything.
    ///
    /// `plan` may be null or empty (SelectedCharacterPanel passes null when nothing is selected) - slot
    /// 0 alone is still shown, carrying Wait, exactly as a single icon already did before this existed.
    ///
    /// This only decides *what* each step shows (sprite, damage amount, riders) and stores it on the
    /// slot - see IntentSlot.damageAmount/riders. Actually painting the damage label and badges happens
    /// in LayOut, once every step's window has a final position for this frame; see PaintSlot for why
    /// that cannot happen here instead.
    /// </summary>
    public void Show(IReadOnlyList<Intent> plan, Character self, IntentIcons icons, StatusIcons statusIcons)
    {
        if (pool.Count == 0) { return; }

        int count = plan != null ? plan.Count : 0;
        int visible = Mathf.Max(1, count);

        for (int i = 0; i < visible; i++)
        {
            IntentSlot slot = SlotAt(i);
            Intent intent = plan != null && i < plan.Count ? plan[i] : Intent.Wait();

            if (!slot.Window.gameObject.activeSelf) { slot.Window.gameObject.SetActive(true); }

            ApplySprite(slot, intent, icons);

            bool showDamage = GameSettings.ShowIntentDamage
                               && intent.kind == IntentKind.Attack
                               && intent.card != null
                               && GridManager.Instance != null;

            slot.damageAmount = showDamage
                ? intent.card.OutgoingDamage(self, GridManager.Instance.GetTile(intent.target))
                : 0;

            slot.riders.Clear();
            if (intent.card != null && GridManager.Instance != null)
            {
                intent.card.OutgoingRiders(self, GridManager.Instance.GetTile(intent.target), slot.riders);
            }

            float alpha = i == 0 ? 1f : followUpAlpha;
            slot.roll.SetAlpha(alpha);
            if (slot.leadIn != null) { SetImageAlpha(slot.leadIn, alpha); }
        }

        for (int i = visible; i < pool.Count; i++)
        {
            if (pool[i].Window.gameObject.activeSelf) { pool[i].Deactivate(); }
        }

        LayOut(visible, icons, statusIcons);
    }

    /// Kills every slot's in-flight roll - the host's own OnDestroy calls this, since an enemy can be
    /// destroyed the instant it dies, possibly mid-roll.
    public void Kill()
    {
        foreach (IntentSlot slot in pool) { slot.Kill(); }
    }

    private void ApplySprite(IntentSlot slot, Intent intent, IntentIcons icons)
    {
        Sprite want = icons != null ? icons.For(intent) : null;

        if (!rolls || slot.justActivated)
        {
            slot.roll.Show(want);
            slot.justActivated = false;
        }
        else if (want != slot.shownSprite)
        {
            slot.roll.Play(slot.shownSprite, want, rollDuration, rollEase);
        }

        slot.shownSprite = want;
    }

    private static void SetImageAlpha(Image image, float alpha)
    {
        Color color = image.color;
        color.a = alpha;
        image.color = color;
    }

    private IntentSlot SlotAt(int index)
    {
        while (pool.Count <= index)
        {
            IntentSlot slot = BuildFollowUpSlot();

            // No authored plate to adopt past slot 0 - a follow-up's is built from scratch, copying
            // whatever slot 0's turned out to be so the row stays uniform.
            BuildSlotExtras(slot, null);

            pool.Add(slot);
        }

        return pool[index];
    }

    /// <summary>
    /// Builds one follow-up slot from scratch, at the exact size, pivot and anchor scheme slot 0
    /// already established - see IntentSlot's own doc comment for why this converges with the primary
    /// into one type immediately after construction. Matching slot 0's pivot.y and anchoredPosition.y
    /// exactly (only pivot.x and anchoredPosition.x ever differ, and only pivot.x is fixed at 0 rather
    /// than copied) is what keeps every step's vertical extent identical with no separate alignment
    /// step anywhere else - LayOut only ever has to move a follow-up window horizontally.
    /// </summary>
    private IntentSlot BuildFollowUpSlot()
    {
        RectTransform primary = pool[0].Window;

        GameObject iconGo = new("IntentIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform iconRect = (RectTransform)iconGo.transform;
        iconRect.SetParent(parent, false);
        iconGo.layer = layer;
        iconRect.anchorMin = anchorPoint;
        iconRect.anchorMax = anchorPoint;
        iconRect.pivot = new Vector2(0f, primary.pivot.y);
        iconRect.sizeDelta = primary.sizeDelta;
        iconRect.anchoredPosition = new Vector2(0f, primary.anchoredPosition.y);

        Image icon = iconGo.GetComponent<Image>();
        icon.color = primaryColor;
        icon.type = primaryImageType;
        icon.preserveAspect = primaryPreserveAspect;
        icon.material = primaryMaterial;
        icon.raycastTarget = false;

        IntentSlot slot = new();
        slot.roll.Build(icon);

        return slot;
    }

    /// <summary>
    /// Builds the pieces every slot needs beyond its own rolling icon: a backing plate behind it, its
    /// own damage label, its own badge stack, and the chevron leading into it from the step before.
    /// Shared by Build's own slot-0 construction and SlotAt's follow-up construction.
    ///
    /// `authoredPlate` is the plate the host already had beside its icon, for slot 0 on a host that
    /// authored one, and null everywhere else. It is *adopted* - reparented into this slot's own
    /// window so it travels with the icon - rather than left where it was and covered by a second
    /// plate of our own: it is a sibling of the window, so leaving it behind means it does not move
    /// when LayOut centres the row, and it shows through as an empty black box wherever the icon used
    /// to be. Same adoption IntentRoll.Build already performs on the authored icon Image itself.
    /// </summary>
    private void BuildSlotExtras(IntentSlot slot, Image authoredPlate)
    {
        RectTransform window = slot.Window;
        float slotWidth = window.sizeDelta.x;

        Image plate = authoredPlate;

        if (plate == null && primaryHasPlate)
        {
            GameObject plateGo = new("IntentPlate", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            plateGo.layer = layer;
            plate = plateGo.GetComponent<Image>();
            plate.color = primaryPlateColor;
            plate.sprite = primaryPlateSprite;
        }

        if (plate != null)
        {
            // Filling the window exactly means the window's own RectMask2D never actually clips this,
            // and sibling index 0 keeps it behind the rolling icon stack.
            RectTransform plateRect = plate.rectTransform;
            plateRect.SetParent(window, false);
            plateRect.SetSiblingIndex(0);
            plateRect.anchorMin = Vector2.zero;
            plateRect.anchorMax = Vector2.one;
            plateRect.pivot = new Vector2(0.5f, 0.5f);
            plateRect.sizeDelta = Vector2.zero;
            plateRect.anchoredPosition = Vector2.zero;
            plate.raycastTarget = false;
        }

        slot.damageLabel.Build(window, slotWidth * damageFontScale, damageColor,
                                new Vector2(slotWidth * damageBoxWidthScale, window.sizeDelta.y));

        slot.badges.Build(parent, anchorPoint, layer);

        GameObject chevronGo = new("IntentChevron", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform chevronRect = (RectTransform)chevronGo.transform;
        chevronRect.SetParent(parent, false);
        chevronGo.layer = layer;
        chevronRect.anchorMin = anchorPoint;
        chevronRect.anchorMax = anchorPoint;
        chevronRect.pivot = new Vector2(0f, 0.5f);
        float chevronSize = slotWidth * chevronScale;
        chevronRect.sizeDelta = new Vector2(chevronSize, chevronSize);
        slot.leadIn = chevronGo.GetComponent<Image>();
        slot.leadIn.raycastTarget = false;
        slot.leadIn.preserveAspect = true;
        chevronGo.SetActive(false);
    }

    /// <summary>
    /// Places every visible step, centred as a whole on slot 0's own originally authored position -
    /// not left-anchored on slot 0's own left edge, which used to leave a two-step row visibly offset
    /// to the right of where a single icon used to sit, with the chevron nowhere near the centre.
    ///
    /// Two passes, and both are needed. Pass 1 walks left-to-right exactly as an uncentred row would -
    /// treating slot 0 as sitting at a local x of 0 - recording each step's own x and each follow-up's
    /// chevron x, plus the row's leftmost and rightmost reach; every width or overhang it needs is
    /// resolved analytically (IntentBadgeStack.PeekOverhangs, DamageOverhang), from what each step will
    /// show rather than from any RectTransform, since no window has a final position yet to read one
    /// from. Only once the whole row's extent is known can Pass 2 compute the single offset that
    /// recentres it on 0 - which means slot 0 itself moves too, whenever there is more than one step -
    /// and only then does it move every window and chevron into place and call PaintSlot, which is what
    /// actually paints each step's damage label and badges against its now-final position.
    /// </summary>
    private void LayOut(int visible, IntentIcons icons, StatusIcons statusIcons)
    {
        IntentSlot slot0 = pool[0];
        float slotWidth = SlotWidth;
        float gap = slotWidth * slotGapScale;
        float chevronSize = slotWidth * chevronScale;

        float badgeSize = slotWidth * badgeScale;
        IntentBadgeLayout badgeLayout = new(
            badgeSize, badgeOverlap, slotWidth * badgeSpacingScale, slotWidth * badgeNumberGapScale,
            new Vector2(slotWidth * badgeNumberWidthScale, badgeSize), slotWidth * badgeNumberFontScale);
        bool showNumbers = GameSettings.ShowIntentDamage;

        // Pass 1. Measured from where slot 0's icon was *authored* to sit, not from a bare 0 - every x
        // below is a real left edge in the strip's own shared anchor frame, so a one-step row lands
        // back exactly where the host placed its icon in the Inspector rather than at the frame's
        // origin (which is the panel's far left edge, and half an icon off on the overhead).
        slotX.Clear();
        chevronX.Clear();
        slotX.Add(primaryLeftEdge);

        IntentBadgeStack.PeekOverhangs(slot0.riders, badgeLayout, showNumbers, maxBadges, slotWidth,
                                        out float rightOverhang0, out float leftOverhang0);

        float rowLeft = primaryLeftEdge - leftOverhang0;
        float cursor = primaryLeftEdge + slotWidth + Mathf.Max(rightOverhang0, DamageOverhang(slot0, slotWidth));

        for (int i = 1; i < visible; i++)
        {
            IntentSlot slot = pool[i];

            IntentBadgeStack.PeekOverhangs(slot.riders, badgeLayout, showNumbers, maxBadges, slotWidth,
                                            out float rightOverhang, out float leftOverhang);

            cursor += gap;
            chevronX.Add(cursor);
            cursor += chevronSize + gap + leftOverhang;

            slotX.Add(cursor);

            cursor += slotWidth + Mathf.Max(rightOverhang, DamageOverhang(slot, slotWidth));
        }

        // Pass 2. The offset that puts the row's own centre back on the centre of where slot 0's icon
        // was authored, so a two-step row grows evenly either side of the spot a one-step row occupies
        // rather than running off to its right. Only applied across more than one step: a single icon
        // keeps its exact authored position rather than nudging itself to centre its own damage number
        // or badges against nothing.
        float rowCenter = (rowLeft + cursor) * 0.5f;
        float primaryCenter = primaryLeftEdge + slotWidth * 0.5f;
        float centeringOffset = visible > 1 ? primaryCenter - rowCenter : 0f;

        float rowY = slot0.Window.anchoredPosition.y;
        float chevronCenterY = IntentSlot.BottomEdge(slot0.Window) + slot0.Window.sizeDelta.y * 0.5f;
        Sprite chevronSprite = icons != null ? icons.Chevron : null;

        for (int i = 0; i < visible; i++)
        {
            IntentSlot slot = pool[i];
            IntentSlot.PlaceByLeftEdge(slot.Window, slotX[i] + centeringOffset, rowY);

            if (i > 0 && slot.leadIn != null)
            {
                bool showChevron = chevronSprite != null;
                slot.leadIn.sprite = chevronSprite;
                slot.leadIn.gameObject.SetActive(showChevron);

                if (showChevron)
                {
                    RectTransform chevronRect = (RectTransform)slot.leadIn.transform;
                    chevronRect.sizeDelta = new Vector2(chevronSize, chevronSize);
                    chevronRect.anchoredPosition = new Vector2(chevronX[i - 1] + centeringOffset, chevronCenterY);
                }
            }

            PaintSlot(slot, badgeLayout, showNumbers, statusIcons, icons, slotWidth);
        }
    }

    /// How far a step's damage label would reach past its icon's own right edge if it is showing a
    /// number - a Move or Summon step has none at all, in which case its badges alone (see
    /// IntentBadgeStack.PeekOverhangs) may still be what governs its reserved width.
    private float DamageOverhang(IntentSlot slot, float slotWidth) =>
        slot.damageAmount > 0 ? slotWidth * damageGapScale + slotWidth * damageBoxWidthScale : 0f;

    /// <summary>
    /// Paints one step's damage label and badges against its window's *current* position - called only
    /// from LayOut's second pass, after that window has already been moved to its final position for
    /// this frame. Calling this any earlier (from the main Show() loop, say, the way an earlier version
    /// of this file did) is exactly the bug that put a follow-up step's damage number to the left of
    /// its icon instead of the right: IntentDamageLabel.Reposition reads the window's position at the
    /// moment it is called, and a follow-up window's very first position is a placeholder near slot 0,
    /// not the position LayOut goes on to give it moments later in the same call.
    /// </summary>
    private void PaintSlot(IntentSlot slot, IntentBadgeLayout badgeLayout, bool showNumbers,
                            StatusIcons statusIcons, IntentIcons icons, float slotWidth)
    {
        slot.damageLabel.Show(slot.damageAmount);
        slot.damageLabel.Reposition(slot.Window, slotWidth * damageGapScale);

        slot.badges.Show(slot.riders, statusIcons, icons, slot.Window, badgeLayout,
                          selfAimedBadgeColor, targetAimedBadgeColor, showNumbers, maxBadges);
    }
}
