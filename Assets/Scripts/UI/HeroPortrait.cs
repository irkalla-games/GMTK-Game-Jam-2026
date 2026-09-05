using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One hero's slot in the bottom-left party row: portrait art, a health/shield bar, energy pips and a
/// capped row of status chips. A view only - PartyPortraitPanel decides who is shown here, whether this
/// slot is the active one, and where it sits in the row. That split is StatusChip's own: a chip does
/// not know whose statuses they are, and a portrait does not know how many others share the row.
///
/// Reuses the same pieces SelectedCharacterPanel draws its own row from - HealthBarFill for the bar
/// arithmetic, the pooled StatusChip prefab for icons, and Glossary for what each one means - so a
/// status shown here and the same status shown on the enemy panel are never two answers to one
/// question.
/// </summary>
public class HeroPortrait : MonoBehaviour
{
    [SerializeField] private Button button;

    [SerializeField] private Image portraitImage;

    [Tooltip("Tinted between the active and rest colours by SetHighlighted. Scale and position are the "
             + "panel's business - this only ever changes colour.")]
    [SerializeField] private Image frame;

    [SerializeField] private TMP_Text nameLabel;

    [SerializeField] private Image healthFill;

    [SerializeField] private Image shieldFill;

    [SerializeField] private TMP_Text healthText;

    [Header("Energy pips")]
    [Tooltip("Pooled to Hero.EnergyCapacity, or further if Hero.Energy exceeds it - see RefreshPips.")]
    [SerializeField] private RectTransform pipParent;

    [SerializeField] private Image pipPrefab;

    [Tooltip("Matches the blue CardFaceV2's own mana pip uses, so the two never disagree about what "
             + "the game's mana colour is.")]
    [SerializeField] private Color pipFilledColor = new(0.22f, 0.514f, 0.863f, 1f);

    [SerializeField] private Color pipEmptyColor = new(0.22f, 0.514f, 0.863f, 0.25f);

    [SerializeField] private float pipSize = 14f;

    [SerializeField] private float pipSpacing = 3f;

    [Tooltip("X glyph laid over a spent pip. Null is tolerated - no cross is built and a spent pip "
             + "just dims, the old look.")]
    [SerializeField] private Sprite pipSpentSprite;

    [SerializeField] private Color pipSpentCrossColor = new(0.75f, 0.78f, 0.82f, 0.85f);

    [Tooltip("How much larger an unspent pip renders than a spent one - PlacePip's own job, not a "
             + "separate scale tween, since the slot pitch already keeps the column from jumping.")]
    [SerializeField] private float availablePipScale = 1.25f;

    [Tooltip("Seconds for one full breathe in and out of the available pips - same shared-clock idiom "
             + "DamagePreviewFill and AuraPulse use, so every hero's pips crest together.")]
    [SerializeField] private float pipPulsePeriod = 1.4f;

    [SerializeField] private float pipPulseMinAlpha = 0.75f;

    [Header("Status row")]
    [Tooltip("Chips are positioned inside this in a single left-to-right row - capped by maxChips, so "
             + "unlike SelectedCharacterPanel's row this deliberately never wraps.")]
    [SerializeField] private RectTransform chipParent;

    [SerializeField] private StatusChip chipPrefab;

    [SerializeField] private float chipSize = 28f;

    [SerializeField] private float chipSpacing = 2f;

    [Header("Frame colours")]
    [Tooltip("Both transparent by default - the portrait sits directly against whatever is behind the "
             + "HUD, no plate. SetHighlighted still tints frame between these two, so a border sprite or "
             + "a translucent accent colour can be authored here later without any code change.")]
    [SerializeField] private Color activeFrameColor = new(1f, 1f, 1f, 0f);

    [SerializeField] private Color restFrameColor = new(1f, 1f, 1f, 0f);

    /// Grown on demand and reused, one per visible chip plus one more for the overflow badge - never
    /// destroyed, same pooling contract StatusChip's other caller uses.
    private readonly List<StatusChip> chips = new();

    /// Reused across refreshes so cataloguing which statuses are active does not allocate a new list
    /// every time - this runs once per portrait per TurnAdvanced/StatsChanged, which adds up. `live` is
    /// the status actually in force, `carried` is every carried turn of that type added up (the badge),
    /// and `stacks` (auras and equipment summed) is what the tooltip sentence quotes.
    private readonly List<(StatusType type, int stacks, Status live, int carried)> activeStatuses = new();

    private readonly List<Image> pips = new();

    /// Index-parallel with pips; an entry is null when pipSpentSprite is unassigned, or the pip it
    /// belongs to predates that build (PipAt only ever appends, never backfills).
    private readonly List<Image> pipCrosses = new();

    /// How many leading pips Update should pulse - set once by RefreshPips and read every frame,
    /// rather than recomputed there, since Update has no reason to touch Hero at all.
    private int availablePips;

    /// Each held by direct reference and killed before being replaced, never by DOTween's id/target
    /// search - the same ownership convention CardViewer's own scaleTween/positionTween pair uses, so
    /// activation and a roster rebuild landing in the same frame can never stack competing tweens on
    /// this portrait's RectTransform.
    private Tweener positionTween;

    private Tweener scaleTween;

    public Character Hero { get; private set; }

    public RectTransform Rect { get; private set; }

    /// <summary>
    /// Exposed for the tutorial spotlight, which lights one hero's own pips while explaining mana.
    ///
    /// An array of the pips themselves rather than pipParent's own rect: PlacePip positions each pip by
    /// anchoredPosition, not a Layout Group, so pipParent's rect is whatever size it happened to be
    /// authored at and is never grown to bound its children - anchoring to it lit a small, wrongly
    /// placed square instead of the pips actually on screen. See TooltipAnchor.Of(RectTransform[], ...).
    /// </summary>
    public RectTransform[] ActivePipRects()
    {
        List<RectTransform> active = new();

        foreach (Image pip in pips)
        {
            if (pip != null && pip.gameObject.activeSelf) { active.Add((RectTransform)pip.transform); }
        }

        return active.ToArray();
    }

    /// Raised on click; the panel decides what that means (activate, or refuse while a card is being
    /// played from a different hero's hand), the same way CharacterSelectSlot's arrow events do.
    public event Action<HeroPortrait> Clicked;

    private void Awake()
    {
        Rect = (RectTransform)transform;

        if (button != null) { button.onClick.AddListener(() => Clicked?.Invoke(this)); }
    }

    /// Points this slot at a hero and shows what never changes turn to turn - portrait art and name.
    /// Stats and statuses are Refresh's job, called separately so the panel can rebind without also
    /// forcing a full stat readout it is about to do anyway.
    public void Bind(Character hero)
    {
        Hero = hero;

        if (portraitImage != null)
        {
            portraitImage.sprite = hero != null ? hero.Portrait : null;
            portraitImage.enabled = portraitImage.sprite != null;
        }

        if (nameLabel != null) { nameLabel.text = hero != null ? hero.DisplayName : string.Empty; }
    }

    /// Redraws health, energy and statuses against the hero's current numbers. Separate from Bind so a
    /// TurnAdvanced/StatsChanged refresh does not need to re-touch art and name every time.
    public void Refresh(StatusIcons icons, Glossary glossary, int maxChips, Canvas canvas)
    {
        if (Hero == null)
        {
            // Otherwise an unbound portrait keeps pulsing whatever it last showed.
            availablePips = 0;
            return;
        }

        int shield = HealthBarFill.Apply(healthFill, shieldFill, Hero);

        if (healthText != null)
        {
            healthText.text = shield > 0
                ? $"{Hero.Health}/{Hero.MaxHealth}  +{shield}"
                : $"{Hero.Health}/{Hero.MaxHealth}";
        }

        RefreshPips();
        RefreshStatuses(icons, glossary, maxChips, canvas);
    }

    /// Swaps the frame's colour only - the enlarge/shrink and the row spreading to make room for it are
    /// PartyPortraitPanel.Layout's job, since only the panel knows how many portraits share the row.
    public void SetHighlighted(bool active)
    {
        if (frame != null) { frame.color = active ? activeFrameColor : restFrameColor; }
    }

    /// <summary>
    /// Where this portrait sits in the row and how large it is right now - PartyPortraitPanel.Layout is
    /// the only caller, since only it knows how many portraits share the row and which one just became
    /// active. Mirrors CardViewer.SetLayoutTarget: one tween per property, killed and replaced together.
    /// </summary>
    public void SetLayoutTarget(Vector2 anchoredPosition, float scale, float duration)
    {
        positionTween?.Kill();
        scaleTween?.Kill();
        positionTween = Rect.DOAnchorPos(anchoredPosition, duration);
        scaleTween = Rect.DOScale(scale, duration);
    }

    /// Breathes the available pips' alpha on the shared Time.time clock - the same idiom
    /// DamagePreviewFill and AuraPulse use, so every hero's pips crest in phase rather than drifting
    /// out of sync with each other. Spent pips are untouched here; RefreshPips owns their colour.
    private void Update()
    {
        if (availablePips <= 0) { return; }

        float cycle = Mathf.Max(pipPulsePeriod, 0.01f);
        float bump = (Mathf.Sin(Time.time * Mathf.PI * 2f / cycle) + 1f) * 0.5f;

        Color c = pipFilledColor;
        c.a *= Mathf.Lerp(pipPulseMinAlpha, 1f, bump);

        for (int i = 0; i < availablePips && i < pips.Count; i++)
        {
            if (pips[i] != null) { pips[i].color = c; }
        }
    }

    /// <summary>
    /// Draws Hero.EnergyCapacity pips, or more if Energy is temporarily above it (GainEnergy is
    /// deliberately uncapped - see Character.EnergyCapacity) so a card that grants energy past the
    /// pool is never invisible here. Spent pips (index >= Hero.Energy) dim and gain a cross; the rest
    /// are left for Update to pulse.
    /// </summary>
    private void RefreshPips()
    {
        if (pipPrefab == null || pipParent == null) { return; }

        int max = Mathf.Max(Hero.EnergyCapacity, Hero.Energy);
        availablePips = Mathf.Clamp(Hero.Energy, 0, max);

        for (int i = 0; i < max; i++)
        {
            bool spent = i >= Hero.Energy;

            Image pip = PipAt(i);
            pip.color = spent ? pipEmptyColor : pipFilledColor;
            PlacePip((RectTransform)pip.transform, i, spent);

            Image cross = pipCrosses[i];

            if (cross != null)
            {
                cross.enabled = spent;
                cross.color = pipSpentCrossColor;
            }
        }

        for (int i = max; i < pips.Count; i++) { pips[i].gameObject.SetActive(false); }
    }

    private Image PipAt(int index)
    {
        while (pips.Count <= index)
        {
            Image pip = Instantiate(pipPrefab, pipParent);
            pips.Add(pip);
            pipCrosses.Add(BuildCross(pip));
        }

        pips[index].gameObject.SetActive(true);

        return pips[index];
    }

    /// One cross child per pooled pip, built once alongside it in PipAt - stretched to fill so it
    /// tracks the pip's own size and scale with no placement math of its own. Starts disabled;
    /// RefreshPips is the only thing that turns it on. Returns null when pipSpentSprite is unassigned,
    /// which RefreshPips treats as "no cross, dim only" - the old look.
    private Image BuildCross(Image pip)
    {
        if (pipSpentSprite == null) { return null; }

        GameObject go = new("Cross", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = pip.gameObject.layer; // SetParent does not carry the layer onto a fresh GameObject.
        go.transform.SetParent(pip.transform, false);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image cross = go.GetComponent<Image>();
        cross.sprite = pipSpentSprite;
        cross.preserveAspect = true;
        cross.raycastTarget = false;
        cross.enabled = false;

        return cross;
    }

    /// Bottom-to-top, one column - pipParent anchors just above the health text beside the bar, and pips
    /// stack upward from there so the first pip sits closest to the text. Unlike StatusChip's row this
    /// never needs to wrap sideways. Pivot is the pip's own centre rather than its bottom edge so
    /// availablePipScale grows a pip symmetrically about its slot instead of upward off the slot below
    /// it; the slot pitch (pipSize + pipSpacing) stays fixed regardless of scale, so the column never
    /// jumps as energy is spent.
    private void PlacePip(RectTransform rect, int index, bool spent)
    {
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(pipSize, pipSize);
        rect.anchoredPosition = new Vector2(0f, index * (pipSize + pipSpacing) + pipSize * 0.5f);
        rect.localScale = Vector3.one * (spent ? 1f : availablePipScale);
    }

    /// <summary>
    /// The walk SelectedCharacterPanel.LayOutStatuses uses, capped rather than wrapped: a portrait has
    /// no room to grow, so past maxChips - 1 the remaining statuses collapse into one more chip showing
    /// "+N", its tooltip built by folding every hidden status into a single TooltipContent.
    /// </summary>
    private void RefreshStatuses(StatusIcons icons, Glossary glossary, int maxChips, Canvas canvas)
    {
        if (chipPrefab == null || chipParent == null) { return; }

        activeStatuses.Clear();

        foreach (StatusType type in StatusTypes.Displayable)
        {
            // Shield is drawn on the bar - showing it here too would be the same number in two places,
            // same reasoning as the enemy/hero info panel.
            if (type == StatusType.Shield) { continue; }

            int stacks = Hero.StatusStacks(type);

            if (stacks > 0) { activeStatuses.Add((type, stacks, Hero.FindStatus(type), Hero.CarriedStatusStacks(type))); }
        }

        int cap = Mathf.Max(1, maxChips);
        int shown = activeStatuses.Count <= cap ? activeStatuses.Count : cap - 1;
        int hidden = activeStatuses.Count - shown;

        for (int i = 0; i < shown; i++)
        {
            (StatusType type, int stacks, Status live, int carried) = activeStatuses[i];

            // Every carried turn of this type added up, blank when a totem's aura is the one in force -
            // see SelectedCharacterPanel.LayOutStatuses, which this mirrors.
            string badge = live == null || live.IsProjected ? string.Empty : carried.ToString();

            StatusChip chip = ChipAt(i);
            chip.Show(icons != null ? icons.For(type) : null, badge);
            chip.Bind(TooltipFor(glossary, type, stacks, live), TooltipAnchor.Of(Rect, canvas));
            Place(chip, i);
        }

        if (hidden > 0)
        {
            StatusChip overflow = ChipAt(shown);
            TooltipContent content = new();

            for (int i = shown; i < activeStatuses.Count; i++)
            {
                (StatusType type, int stacks, Status live, _) = activeStatuses[i];
                glossary?.StatusContent(type, stacks, live, content);
            }

            overflow.Show(null, $"+{hidden}");
            overflow.Bind(content, TooltipAnchor.Of(Rect, canvas));
            Place(overflow, shown);

            shown++;
        }

        for (int i = shown; i < chips.Count; i++) { chips[i].Hide(); }
    }

    private TooltipContent TooltipFor(Glossary glossary, StatusType type, int stacks, Status live) =>
        glossary == null ? null : glossary.StatusContent(type, stacks, live);

    private StatusChip ChipAt(int index)
    {
        while (chips.Count <= index) { chips.Add(Instantiate(chipPrefab, chipParent)); }

        return chips[index];
    }

    private void Place(StatusChip chip, int index)
    {
        RectTransform rect = chip.Rect;

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(chipSize, chipSize);
        rect.anchoredPosition = new Vector2(index * (chipSize + chipSpacing), 0f);
    }
}
