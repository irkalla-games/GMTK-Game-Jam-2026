using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The coloured ring around a character's health bar: white while it is the one you are playing as,
/// green while it still has a card it could play, and nothing once it is spent.
///
/// One channel, two meanings, and the precedence between them matters - see Colour() below. It is the
/// same shape TileSelector uses for a tile (hover beats in-range beats idle): one component owns the
/// look, every state it can be in is listed in one place, and nothing outside writes the colour.
///
/// A separate component from CharacterOverheadViewer on purpose. That one owns the bar's fill amounts
/// and the enemy intent icon; this owns the ring around the outside of the whole assembly. Naming the
/// half rather than the concept is what stops a "character UI" class from slowly becoming everything
/// drawn near a character.
///
/// A plain enlarged, tinted Image sitting behind BarBack - not the GMTK/SpriteOutline dilation shader
/// cards use. Two things ruled that out here, both specific to going through a Canvas rather than a
/// SpriteRenderer: CanvasRenderer draws every pass in whatever shader it is given, with no LightMode
/// filtering, so the shader's two passes (needed so a plain SpriteRenderer works under either URP
/// renderer) both fired for the same Image and doubled the ring; and UGUI does not compose scale into
/// vertex growth the same predictable way a SpriteRenderer's Transform does, which is what actually
/// grew the ring to cover a quarter of the screen even after correcting for it the way CardBorder's
/// non-uniform scale is corrected. This technique sidesteps both: BarBack and this Image go through
/// the identical Image/CanvasRenderer path, so "N pixels bigger than BarBack" means exactly that
/// regardless of whatever Unity does internally to get it on screen - the same "drop a bigger copy of
/// the same shape behind it" trick CardBorder's own art already uses against CardBackground.
///
/// Heroes only. An enemy has no hand you can play from, so green would mean nothing on one, and the
/// white is an answer to "who am I playing as" - also a question only a hero can be the answer to.
/// Enemies still fill SelectedCharacterPanel when clicked; they just do not light up.
/// </summary>
[RequireComponent(typeof(Character))]
public class CharacterOutline : MonoBehaviour
{
    [Tooltip("The health bar's background Image (BarBack, under Overhead). Nothing else references " +
        "it today, so this is a free wire-up. Required - without it there is nowhere to build the " +
        "outline against.")]
    [SerializeField] private Image barBack;

    [Tooltip("A plain, non-sliced sprite for the backing ring - Assets/Sprites/WhiteBar.png is a " +
        "ready-made choice. BarBack's own sprite cannot be reused directly: it is imported Sliced " +
        "(a 9-slice), and forcing it to Image.Type.Simple here would stretch its border art across " +
        "the whole bar instead of framing it.")]
    [SerializeField] private Sprite outlineSprite;

    [Tooltip("How far the backing ring is grown past BarBack on each side, in RectTransform units - " +
        "which are pixels, since the canvas is Constant Pixel Size. It grows both the 160-wide and " +
        "the 8-tall axis by the same amount, so the short axis gets a much bigger proportional bump " +
        "than the long one.")]
    [SerializeField] private float outlinePadding = 4f;

    [Tooltip("Shown while this is the character you are playing as. Kept translucent by default so " +
        "the ring reads as a hint rather than a wall around the bar.")]
    [SerializeField] private Color selectedColor = new(1f, 1f, 1f, 0.5f);

    [Tooltip("Shown while this character still has a card it could play right now. Same translucency " +
        "reasoning as selectedColor.")]
    [SerializeField] private Color hasMovesColor = new(0.35f, 1f, 0.45f, 0.5f);

    private Character character;

    /// The backing Image actually drawing the ring. Null whenever barBack was never wired up - every
    /// method below no-ops on that rather than throwing, the same bargain every other optional
    /// reference in this codebase strikes.
    private Image outlineImage;

    /// Null while the outline is off. Held so a refresh that lands on the same answer can skip the
    /// write entirely.
    private Color? current;

    private void Awake()
    {
        character = GetComponent<Character>();

        if (barBack == null) { return; }

        BuildOutlineImage();
    }

    /// <summary>
    /// Builds the backing ring once, as a sibling inserted just behind BarBack and padded outward from
    /// it by outlinePadding on every side. Left on the default material (whatever
    /// Graphic.defaultGraphicMaterial resolves to, the same UI/Default every other bar piece already
    /// uses) - the ring is just a bigger, recoloured copy of the same rectangle, not a shader effect.
    ///
    /// Matching BarBack's own anchors/pivot rather than assuming full-stretch keeps this correct even
    /// if a future layout pass changes how the bar is anchored - only the padding is this component's
    /// own opinion. Forcing Image.Type.Simple guards against outlineSprite being (or becoming) a
    /// sliced or filled sprite, either of which would tile or chase a fill boundary instead of just
    /// filling the rect.
    /// </summary>
    private void BuildOutlineImage()
    {
        RectTransform barRect = barBack.rectTransform;

        GameObject outlineObject = new("HealthBarOutline", typeof(RectTransform), typeof(Image));
        RectTransform outlineRect = (RectTransform)outlineObject.transform;

        outlineRect.SetParent(barRect.parent, false);
        outlineRect.SetSiblingIndex(barRect.GetSiblingIndex());

        // Inherited explicitly - SetParent does not carry it, and a fresh GameObject starts on
        // Default, which the board camera culls and the UI camera would draw somewhere else entirely.
        // IntentRoll already does this for its own runtime children. See GameLayers.
        outlineObject.layer = barRect.parent.gameObject.layer;

        outlineRect.anchorMin = barRect.anchorMin;
        outlineRect.anchorMax = barRect.anchorMax;
        outlineRect.pivot = barRect.pivot;
        outlineRect.offsetMin = barRect.offsetMin - new Vector2(outlinePadding, outlinePadding);
        outlineRect.offsetMax = barRect.offsetMax + new Vector2(outlinePadding, outlinePadding);

        outlineImage = outlineObject.GetComponent<Image>();
        outlineImage.sprite = outlineSprite;
        outlineImage.type = Image.Type.Simple;
        outlineImage.raycastTarget = false;
        outlineImage.color = new Color(1f, 1f, 1f, 0f);
    }

    private void Start()
    {
        BattleManager battle = BattleManager.Instance;

        if (battle != null)
        {
            battle.PlayabilityChanged += Refresh;
            battle.SelectedCharacterChanged += OnSelectedCharacterChanged;
        }

        // A pull, not just a tidy default - the same reason CharacterOverheadViewer ends its Start
        // this way. SpawnParty instantiates a hero and mutates it on that same frame, before the
        // hero's own Start has run, so the events that went with all of it fired into nothing.
        Refresh();
    }

    private void OnDestroy()
    {
        if (BattleManager.Instance == null) { return; }

        BattleManager.Instance.PlayabilityChanged -= Refresh;
        BattleManager.Instance.SelectedCharacterChanged -= OnSelectedCharacterChanged;
    }

    private void OnSelectedCharacterChanged(Character _) => Refresh();

    /// <summary>
    /// What colour the ring should be wearing, or null for no ring.
    ///
    /// Selected wins over having moves, deliberately. Once two heroes are both green nothing tells you
    /// which one's hand is on screen, and that is the more urgent question - so the character you are
    /// playing as leaves the playability channel and joins the identity one.
    /// </summary>
    private Color? Colour()
    {
        if (character == null || character.IsDead || !character.IsPlayerControlled) { return null; }

        if (IsSelected()) { return selectedColor; }

        return HasPlayableCard() ? hasMovesColor : (Color?)null;
    }

    /// <summary>
    /// Whether this is the character the player is currently acting as.
    ///
    /// `SelectedCharacter ?? ActiveCharacter`, the rule SelectedCharacterPanel already documents, and
    /// needed here for the same reason: the battle sets an active character at the top of Start but
    /// never selects one, so reading SelectedCharacter alone would leave the hero whose hand is
    /// actually on screen wearing the green "somebody else's turn" colour until you clicked them.
    /// </summary>
    private bool IsSelected()
    {
        BattleManager battle = BattleManager.Instance;

        if (battle == null) { return false; }

        Character shown = battle.SelectedCharacter != null ? battle.SelectedCharacter : battle.ActiveCharacter;

        return shown == character;
    }

    /// Asks Card.PlayRefusal, the same predicate the card highlight and the click both use - so a
    /// green ring is exactly one with a green card in hand.
    private bool HasPlayableCard()
    {
        // Once per character rather than once per card: PlayRefusal asks ActRefusal, which walks the
        // totems and allocates a list every time.
        if (!character.CanAct) { return false; }

        foreach (Card card in character.Hand)
        {
            if (card.PlayRefusal(character) == null) { return true; }
        }

        return false;
    }

    private void Refresh()
    {
        Color? wanted = Colour();

        if (wanted == current) { return; }

        current = wanted;
        Apply();
    }

    /// The one place this component writes anything after Awake, and it writes exactly one property.
    /// Alpha doubles as the on/off switch - a fully transparent Image is indistinguishable from no
    /// ring at all, so "off" needs no separate flag.
    private void Apply()
    {
        if (outlineImage == null) { return; }

        outlineImage.color = current ?? new Color(1f, 1f, 1f, 0f);
    }
}
