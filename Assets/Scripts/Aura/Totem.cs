using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Who an aura is for. Allies = 0 so a Totem authored before this field existed keeps the old
/// allies-only behaviour - the same load-bearing-zero rule RangeShape.Anywhere documents.
/// </summary>
public enum AuraAudience
{
    Allies = 0,
    Enemies = 1,
    Everyone = 2,
}

/// <summary>
/// Casts an aura around the Character it is attached to: statuses held by whoever currently stands in
/// range, and one-shot reactions that fire when a resolved action belongs to somebody standing there.
///
/// **Auras are pulled, not pushed.** A totem holds no membership list and never writes to a character.
/// Character.ActiveStatuses walks the totems and asks each one what it is projecting onto that
/// character *right now*, so standing in an aura is a fact about where you are standing rather than
/// bookkeeping somebody has to keep in sync. That deletes the whole class of problems the previous
/// push model existed to manage: no diffing membership on every resolved action, no withdrawing grants
/// when a character walks out, no cleanup when the totem dies. A dead totem's Covers returns false and
/// its auras are simply gone the next time anything asks.
///
/// The statuses handed out are built fresh on every query, which has one consequence worth knowing: an
/// aura-granted status that spends charges - Block, Parry, Double Attack - mutates a throwaway and so
/// never depletes while you stand in range. That is the right reading of a *maintained* aura, but it
/// makes those types much stronger as auras than as cards.
/// </summary>
[RequireComponent(typeof(Character))]
public class Totem : MonoBehaviour
{
    [SerializeField] private TargetRange range;

    [Tooltip("Who this aura is for. Allies is the default and matches how auras behaved before this "
             + "field existed.")]
    [SerializeField] private AuraAudience affects;

    [Tooltip("Auras projected onto everyone currently standing in range. Applied and withdrawn purely "
             + "by where they stand - nothing is written onto the character.")]
    [SerializeField] private List<AuraData> auras = new();

    [Tooltip("One-shot effects fired at whichever character triggers them, the moment a matching "
             + "action resolves while they stand in range.")]
    [SerializeField] private List<AuraReaction> reactions = new();

    [Tooltip("Colour of this totem's pulse on the board. Alpha is ignored - AuraPulse owns that.")]
    [SerializeField] private Color auraColor = DefaultAuraColor;

    [Tooltip("Shown as this totem's tooltip title. Falls back to \"Totem\" when blank, since "
             + "gameObject.name is just \"Totem(Clone)\" at runtime.")]
    [SerializeField] private string displayName;

    /// The dark blue every totem pulses in until somebody authors otherwise.
    private static readonly Color DefaultAuraColor = new(0.13f, 0.22f, 0.55f, 1f);

    private Character owner;

    /// <summary>
    /// Every totem currently on the board. A plain static list rather than a manager singleton: there
    /// is nothing to configure and nothing to place in a scene, so a totem that forgot to register
    /// would be a bug with no cause to point at.
    /// </summary>
    private static readonly List<Totem> active = new();

    /// <summary>
    /// Statics survive entering Play Mode when Domain Reload is disabled, which would leave the last
    /// session's destroyed totems in the list. Cheaper to clear once than to null-check forever.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active.Clear();

    /// Read-only so the registry stays OnEnable/OnDisable's to maintain. AuraPulse walks this to draw
    /// every totem's footprint.
    public static IReadOnlyList<Totem> Active => active;

    /// <summary>
    /// Appends every status the totems on the board are currently projecting onto this character.
    ///
    /// Walking every totem on every query is not free in the abstract, but a board holds a handful at
    /// most, and this is the same trade the previous implementation made when it walked the whole
    /// roster on every resolved action.
    /// </summary>
    public static void CollectAuras(Character character, List<Status> into)
    {
        if (character == null || into == null) { return; }

        foreach (Totem totem in active)
        {
            if (totem != null) { totem.Project(character, into); }
        }
    }

    private void Awake()
    {
        owner = GetComponent<Character>();
    }

    private void OnEnable()
    {
        active.Add(this);

        // The board visual has nothing to configure and nothing to place, so it self-creates on the
        // first totem rather than waiting in the scene - same reasoning as `active` being a plain
        // static list. A driver somebody forgot to drop in would be a bug with no cause to point at.
        AuraPulse.Ensure();
    }

    private void OnDisable()
    {
        active.Remove(this);

        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved -= HandleActionResolved; }
    }

    /// <summary>
    /// Subscribed in Start, not Awake, on the same reasoning as Character.PlaceOnStartTile: every Awake
    /// in the scene runs before any Start, so ActionManager.Instance is guaranteed set up by the time
    /// this runs. Only reactions need this - the passive half is pulled and needs no event at all.
    /// </summary>
    private void Start()
    {
        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved += HandleActionResolved; }
    }

    /// <summary>
    /// The shape of this totem's reach, and the tile it is measured from. Together these are the
    /// footprint AuraPulse draws - the same two values Covers feeds to range.Contains, so the pulse
    /// can never light a tile the aura does not actually reach.
    /// </summary>
    public TargetRange Range => range;

    public GridTile OriginTile => owner != null ? owner.Tile : null;

    /// <summary>
    /// This totem's colour on the board.
    ///
    /// Falls back rather than trusting the serialized value, on the same load-bearing-zero reasoning
    /// as RangeShape.Anywhere: a Totem authored before this field existed - Totem.prefab was written
    /// by hand - can deserialize it as all-zero, and transparent black is an aura you cannot see
    /// rather than an aura that is obviously misconfigured.
    /// </summary>
    public Color AuraColor => auraColor.maxColorComponent <= 0f ? DefaultAuraColor : auraColor;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Totem" : displayName;

    /// Read-only views for TotemTooltip - Totem itself builds no TooltipContent, the same reason
    /// Character never calls back into a viewer.
    public IReadOnlyList<AuraData> Auras => auras;

    public IReadOnlyList<AuraReaction> Reactions => reactions;

    /// <summary>
    /// Whether this totem has a zone worth drawing: alive, standing somewhere, and actually carrying
    /// something to project. Deliberately says nothing about `affects` - who benefits is a question
    /// about the character standing there, while the footprint is the totem's reach either way.
    /// </summary>
    public bool IsProjecting =>
        owner != null && !owner.IsDead && owner.Tile != null && (auras.Count > 0 || reactions.Count > 0);

    /// <summary>
    /// Whether this totem's aura reaches that character right now. False for a dead or unplaced totem,
    /// which is what makes a totem's auras vanish the instant it drops without anything cleaning up.
    /// </summary>
    public bool Covers(Character character)
    {
        if (character == null || character.IsDead || character.Tile == null) { return false; }

        if (owner == null || owner.IsDead || owner.Tile == null) { return false; }

        if (!range.Contains(owner.Tile, character.Tile)) { return false; }

        return affects switch
        {
            AuraAudience.Allies => Character.AreAllies(owner.Affiliation, character.Affiliation),
            AuraAudience.Enemies => Character.AreEnemies(owner.Affiliation, character.Affiliation),
            _ => true,
        };
    }

    /// <summary>
    /// Builds this totem's auras for one character and appends them.
    ///
    /// Each is a fresh Aura wrapping a fresh StatusEffect - the effect carries the rule (Strength's
    /// arithmetic, Poison's tick), the Aura marks it as projected rather than carried and points back
    /// here. Fresh every time; see the class comment for why that matters.
    /// </summary>
    private void Project(Character character, List<Status> into)
    {
        if (auras.Count == 0 || !Covers(character)) { return; }

        foreach (AuraData aura in auras)
        {
            StatusEffect projectedEffect = StatusEffect.Create(aura.Type, aura.Stacks, Status.Indefinite);

            if (projectedEffect != null) { into.Add(new Aura(this, projectedEffect)); }
        }
    }

    private void HandleActionResolved(GameAction action, ActionContext ctx)
    {
        TryReact(action, ctx);
    }

    /// Fires every reaction whose trigger matches the resolved action, provided it belongs to somebody
    /// this aura currently covers.
    private void TryReact(GameAction action, ActionContext ctx)
    {
        if (reactions.Count == 0 || ctx.source == null || !Covers(ctx.source)) { return; }

        Character triggeringCharacter = ctx.source;

        foreach (AuraReaction reaction in reactions)
        {
            if (!reaction.Matches(action) || reaction.Effect == null) { continue; }

            ActionContext reactionCtx = new(card: null, source: triggeringCharacter, target: triggeringCharacter.Tile);
            reaction.Effect.Resolve(reactionCtx);
        }
    }
}
