using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public enum BattlePhase
{
    NotStarted = 0,
    TurnStart = 1,
    PlayerActing = 2,
    EnemyResolve = 3,
    Finished = 4,
}

/// <summary>
/// The round. Not a turn order - inside PlayerActing there is none, and clicking a character still
/// makes it active exactly as before. The boundary exists for two reasons only: energy and armor need
/// a refresh point, and enemies need a moment to act.
///
///   TurnStart      refresh energy + armor, draw back up, enemies commit an opening intent
///   PlayerActing   free-form, immediate resolution. Ends on End Turn and nothing else - never on
///                  its own - then player-controlled statuses tick. Every enemy's intent is kept live
///                  the whole time - see LateUpdate - so the icon over its head always shows what it
///                  would actually do if EnemyResolve started this instant.
///   EnemyResolve   each enemy re-decides outright and acts on it - see Decide and Execute. Enemy
///                  statuses tick once every enemy has acted.
///   -> TurnStart
///
/// Statuses deliberately tick at the end of the phase they gate rather than at the shared TurnStart -
/// see TickStatuses(bool) below.
///
/// Also owns the roster, who is active, and what a tile click means - the coherent half of what used
/// to be GameManager. The display half went to ActiveHandViewer.
/// </summary>
public class BattleManager : Singleton<BattleManager>
{
    [SerializeField] private TextMeshProUGUI turnCounter;

    [Tooltip("Plays the giant turn-count roll between the enemy's turn and the next player turn. "
             + "Optional - a scene without one rolls straight into the next turn.")]
    [SerializeField] private TurnTransitionViewer turnTransition;

    [Tooltip("Characters already placed in the scene. When LevelData/RunState are both wired up this "
             + "is normally empty - SpawnParty and SpawnEnemies populate `characters` instead - but "
             + "anything placed here rides along too, which is what keeps a scene runnable stand-alone.")]
    [SerializeField] private List<Character> characters = new();

    [Tooltip("Enemy placements and the party's spawn cells for this battle. Only used when there is no "
             + "run under way - the run's current level wins wherever one exists.")]
    [SerializeField] private LevelData levelData;

    [Tooltip("Campaign started automatically when this scene is opened on its own, so a level is "
             + "playable in the Editor without going through the Main Menu. Ignored when the player "
             + "arrived here through a real run.")]
    [SerializeField] private RunData debugCampaign;

    /// The tutorial copy. Consts rather than serialized fields on purpose: a field added to a
    /// component the scene already holds deserializes to empty, not to its initialiser, so the text
    /// would silently vanish until someone retyped it in the Inspector.
    private const string TutorialTitle = "How to Play";

    private const string TutorialMessage =
        "You are a group of adventures trying to survive the countdown. Make it to the end to beat this Level!";

    /// <summary>
    /// The level being played. The run answers this wherever there is one; the serialized field is the
    /// last-resort fallback that keeps a Game scene with no campaign assigned playable, the same
    /// fallback story as turnsToSurvive and handSize below.
    /// </summary>
    private LevelData CurrentLevel =>
        RunManager.Instance != null && RunManager.Instance.CurrentLevel != null
            ? RunManager.Instance.CurrentLevel
            : levelData;

    [Tooltip("Where spawned enemies are parented. Optional - tidiness only.")]
    [SerializeField] private Transform enemyParent;

    [Tooltip("Turn limit used when no Level Data is assigned.")]
    [SerializeField] private int turnsToSurvive = 10;

    [Tooltip("Hand size used when no Level Data is assigned.")]
    [SerializeField] private int handSize = 5;

    /// Reaching 0 is the win. LevelData's value wins once one is assigned; the field above is only
    /// the fallback that keeps a Level-Data-less scene playable.
    private int TurnsToSurvive => CurrentLevel != null ? CurrentLevel.TurnsToSurvive : turnsToSurvive;

    /// Same fallback story as TurnsToSurvive.
    private int HandSize => CurrentLevel != null ? CurrentLevel.HandSize : handSize;

    [Tooltip("Pause between an enemy's individual movement steps, so a walk reads as a walk.")]
    [SerializeField] private float stepDuration = 0.16f;

    [field: SerializeField, ReadOnlyField]
    public int TurnsRemaining { get; private set; }

    /// Rounds since the battle started, counting up. TurnsRemaining only counts down, and deriving
    /// "elapsed" from TurnsToSurvive - TurnsRemaining would break the moment anything grants extra
    /// turns - so this is tracked independently. Drives wave spawning.
    public int TurnsElapsed { get; private set; }

    public BattlePhase Phase { get; private set; }

    /// Whose hand is on screen. Cards are played by this character and spend its energy.
    public Character ActiveCharacter { get; private set; }

    /// Whoever was last clicked, ally or enemy - what an info panel should be showing right now.
    /// Broader than ActiveCharacter on purpose: every occupant is selectable, but only a
    /// player-controlled one ever becomes active. See SetSelectedCharacter.
    public Character SelectedCharacter { get; private set; }

    public IReadOnlyList<Character> Characters => characters;

    private bool endTurnRequested;

    /// <summary>
    /// Whether End Turn has been taken and the round is winding down. Still true while PlayerActing is
    /// the phase - RunBattle does not leave that phase until EnemyResolve actually starts, and several
    /// waits sit in between (loot, the discard animation, the tutorial's hold).
    ///
    /// Exposed because "did they press End Turn" and "has the phase changed" are not the same question,
    /// and anything waiting on the press specifically - see TutorialDirector.EndTurn - deadlocks against
    /// its own hold if it asks the second one.
    /// </summary>
    public bool EndTurnRequested => endTurnRequested;

    /// <summary>
    /// Set whenever something the intent recompute cares about might have changed - an action
    /// resolving, a character's stats changing, or one joining or leaving the board - and drained once
    /// per frame in LateUpdate. Coalescing this way means a card that queues three actions produces one
    /// recompute pass and at most one icon roll per enemy, not three.
    /// </summary>
    private bool intentDirty;

    /// Which run record each spawned party member came from, so the state it finishes the level with
    /// can be written back to the run. Only party members are in here - an enemy has nothing that
    /// outlives the battle.
    private readonly Dictionary<Character, PartyMember> partyRecords = new();

    /// Set by LootManager while a reward panel is up. The other half of InputLocked.
    private bool rewardPanelUp;

    /// <summary>
    /// True while a modal owns the screen - a reward panel or a notification. Not Time.timeScale -
    /// that would stall the WaitForSeconds inside EnemyResolve (an enemy shoving a hero onto loot
    /// takes that exact path) and would not actually block a click, since OnMouseDown is a physics
    /// raycast no uGUI panel intercepts. This is the real gate; OnTileClicked and
    /// CardPlayManager.OnCardClicked both check it.
    ///
    /// The notification half is *pulled* rather than pushed, the same way auras are. If Show/Hide
    /// wrote the flag instead, a notification opening over a reward panel would clear the panel's
    /// lock when it was dismissed - one bool cannot remember that two things wanted it held.
    /// </summary>
    public bool InputLocked =>
        rewardPanelUp
        || (NotificationManager.Instance != null && NotificationManager.Instance.IsShowing);

    public void SetInputLocked(bool value) => rewardPanelUp = value;

    /// Last frame's InputLocked, so Update can spot the moment it goes up. See ClearStuckHover.
    private bool inputWasLocked;

    /// Hook this to the End Turn button. The only way out of PlayerActing, and it always works while
    /// the phase is live - energy left unspent is simply banked. The button dims itself while there
    /// is still something playable (see EndTurnButton) but never refuses the click; holding energy
    /// back is a decision the player is allowed to make.
    public void RequestEndTurn()
    {
        if (Phase != BattlePhase.PlayerActing || InputLocked) { return; }

        // One door for both the button and the Enter key in Update, so the tutorial only has to be asked
        // once - see TutorialDirector.
        string tutorialRefusal = TutorialDirector.RefuseEndTurn();

        if (tutorialRefusal != null)
        {
            Debug.Log($"end turn requested - ignored, {tutorialRefusal}");
            return;
        }

        endTurnRequested = true;
    }

    /// <summary>
    /// Every tile click lands here. With a card selected it is a play; with nothing selected it means
    /// "play as whoever is standing here" - characters have no colliders of their own, so the tile
    /// under one is what you click to pick it up.
    ///
    /// Here rather than on GridManager on purpose. Deciding what a click means depends on card
    /// selection and on the phase, so putting it on the board would make GridManager depend on the
    /// card UI and the turn system - the exact reverse of the direction it runs in now, where effects
    /// ask GridManager.MoveRefusal and it asks nobody anything.
    /// </summary>
    public void OnTileClicked(GridTile tile)
    {
        if (tile == null) { return; }

        if (InputLocked)
        {
            Debug.Log($"tile clicked: {tile.Coordinates} - ignored, a reward panel is up");
            return;
        }

        if (Phase != BattlePhase.PlayerActing && Phase != BattlePhase.NotStarted)
        {
            Debug.Log($"tile clicked: {tile.Coordinates} - ignored, not the player's turn ({Phase})");
            return;
        }

        // Above the dispatch below, so it covers both meanings a tile click can have - playing the
        // selected card here, and picking up whoever is standing here. The tutorial permits one specific
        // tile per step and it is the same declaration the spotlight's hole came from, so a lit tile is
        // exactly a clickable one.
        string tutorialRefusal = TutorialDirector.RefuseTile(tile);

        if (tutorialRefusal != null)
        {
            Debug.Log($"tile clicked: {tile.Coordinates} - ignored, {tutorialRefusal}");
            return;
        }

        CardPlayManager cardPlayManager = CardPlayManager.Instance;

        if (cardPlayManager != null && cardPlayManager.HasSelection)
        {
            cardPlayManager.PlaySelectedOn(tile);
            return;
        }

        Character occupant = tile.Occupant;

        if (occupant == null || !occupant.IsPlayerControlled)
        {
            if (occupant != null) { SetSelectedCharacter(occupant); }

            string who = occupant != null ? $"{occupant.name} is not player controlled" : "nobody here";
            Debug.Log($"tile clicked: {tile.Coordinates} - no card selected, {who}");
            return;
        }

        Debug.Log($"tile clicked: {tile.Coordinates} - activating {occupant.name}");
        SetSelectedCharacter(occupant);
        SetActiveCharacter(occupant);
    }

    /// <summary>
    /// Every tile hover starts here, the same centralizing OnTileClicked already does for clicks -
    /// GridTile has no OnMouseEnter/Exit of its own, but TileSelector's do, and TotemTooltip's own
    /// collider steals them the same way it already forwards its OnMouseDown here. Refreshes the
    /// selected card's aiming previews - the area footprint and the per-character damage preview -
    /// against the hovered tile; `tile` null means the cursor left it, which clears both.
    ///
    /// Not gated on InputLocked here - TileSelector.OnMouseEnter already refuses to report a hover
    /// while a modal is up, and the exit/null path has to stay live regardless so a preview does not
    /// get stuck lit behind a panel, matching ClearStuckHover's own reasoning for the tint itself.
    /// </summary>
    public void OnTileHovered(GridTile tile)
    {
        if (CardPlayManager.Instance != null) { CardPlayManager.Instance.RefreshAimPreviews(tile); }
    }

    /// <summary>
    /// Raised when the character you are playing as changes. ActiveHandViewer listens so the row on
    /// screen follows it.
    ///
    /// An event rather than calling the viewer directly, and that is load-bearing rather than taste.
    /// Reaching for ActiveHandViewer.Instance here put a view object on the battle's critical path:
    /// if it were ever null for one frame, the exception would abort Start *after* ActiveCharacter
    /// had been assigned but *before* StartCoroutine(RunBattle) - so clicking a character still
    /// appeared to work while nothing ever drew a card. The view depends on the battle; the battle
    /// must not depend on the view.
    /// </summary>
    public event System.Action<Character> ActiveCharacterChanged;

    /// Makes this character the one you are playing as. Whoever is drawing the hand follows along.
    public void SetActiveCharacter(Character character)
    {
        if (character == null || character == ActiveCharacter) { return; }
        
        ActiveCharacter = character;
        Debug.Log($"active character: {character.name} (energy {character.Energy}, {character.Hand.Count} in hand)");
        ActiveCharacterChanged?.Invoke(character);

        // The hand on screen is now somebody else's, so every card in it has to be re-asked against
        // a different energy pool.
        RaisePlayabilityChanged();
    }

    /// Raised when the selected character changes, for anything showing per-character info (health,
    /// statuses, block/parry) rather than a hand - unlike ActiveCharacterChanged this fires for enemies
    /// too, and can go to null when the selected character dies. Same reasoning as
    /// ActiveCharacterChanged for being an event rather than a direct call: the battle must not depend
    /// on the view.
    public event System.Action<Character> SelectedCharacterChanged;

    /// Marks this character as the one an info panel should describe. Unlike SetActiveCharacter this
    /// accepts null, so a selection can be cleared when its character leaves the board.
    public void SetSelectedCharacter(Character character)
    {
        if (character == SelectedCharacter) { return; }

        SelectedCharacter = character;
        SelectedCharacterChanged?.Invoke(character);

        // Selection is one of the things an outline is drawn from - see CharacterOutline, where it
        // is what makes a hero white rather than green.
        RaisePlayabilityChanged();
    }

    /// <summary>
    /// Raised when the turn machinery has finished mutating characters, for anything displaying their
    /// stats.
    ///
    /// This exists because ActionManager.ActionResolved is *not* enough, though it was long assumed to
    /// be. Poison bites in OnTurnEnd and Shield wipes itself in OnTurnStart, and both are called
    /// straight from this coroutine without ever going through an action - so a panel refreshing only
    /// on resolved actions showed a poisoned enemy at stale health until something else happened to
    /// touch it.
    ///
    /// Deliberately parameterless and coarse. The alternative - Character raising an event per stat -
    /// puts the burden on every future mutation to remember to announce itself, where this only has to
    /// be raised where the battle already knows it has just run a batch of hooks.
    /// </summary>
    public event System.Action TurnAdvanced;

    /// <summary>
    /// Raised when anything that could change what is playable right now has changed - energy spent
    /// or refilled, a card drawn or discarded, a status gained, a cooldown ticked, or the active or
    /// selected character moving.
    ///
    /// Everything that draws the playability channel hangs off this one event: the green border on a
    /// card, the outline on a hero, and the End Turn button's dim. They all answer the same question
    /// from the same predicate (Card.PlayRefusal), so they get the same signal rather than three
    /// subscriptions each to Character.StatsChanged, CardDrawn, CardDiscarded and TurnAdvanced.
    ///
    /// An event rather than polling, and that is not just taste: Character.CanAct walks
    /// ActiveStatuses(), which builds a fresh List every call, so a per-frame sweep of the party's
    /// hands would allocate every frame for an answer that changes a handful of times a turn.
    ///
    /// Parameterless and coarse, the same bargain TurnAdvanced strikes above - a listener re-asks
    /// PlayRefusal itself rather than being told what moved.
    /// </summary>
    public event System.Action PlayabilityChanged;

    private void RaisePlayabilityChanged() => PlayabilityChanged?.Invoke();

    /// <summary>
    /// Raised when a character is hooked into the battle, from either arrival route - the scene loop
    /// in Start or AddCharacter for anything spawned or summoned. General purpose on purpose: this is
    /// the notification a viewer needs to attach itself to every character without a component on
    /// every character prefab - FloatingTextManager is the reason it exists.
    /// </summary>
    public event System.Action<Character> CharacterJoined;

    /// The matching half, raised from Unsubscribe as a character leaves the board.
    public event System.Action<Character> CharacterLeft;

    /// <summary>
    /// Hooks a character up to the battle. Called from the two places a character can arrive: the
    /// scene loop in Start, and AddCharacter for anything spawned or summoned.
    ///
    /// One method rather than a line per event at each site: there are two arrival routes and four
    /// subscriptions now, and the failure mode of spelling them out twice is a character that raises
    /// Died correctly but never announces its energy - a hero whose cards silently stop updating.
    /// </summary>
    private void Subscribe(Character character)
    {
        character.Died += HandleCharacterDied;

        // Whose energy, hand and statuses these are does not matter: playability is asked about the
        // whole party at once (see CanAnyoneAct and the End Turn button), so any character moving is
        // reason enough to re-ask.
        character.StatsChanged += HandlePlayabilityMayHaveChanged;
        character.CardDrawn += HandlePlayabilityMayHaveChanged;
        character.CardDiscarded += HandlePlayabilityMayHaveChanged;

        // A character's health feeds Weakest/Strongest targeting, so any stat change can flip who an
        // enemy means to hit. A character arriving is itself a change - a fresh body on the board can
        // turn someone else's committed Move into an Attack.
        character.StatsChanged += HandleIntentMayHaveChanged;
        intentDirty = true;

        CharacterJoined?.Invoke(character);
    }

    private void Unsubscribe(Character character)
    {
        character.Died -= HandleCharacterDied;
        character.StatsChanged -= HandlePlayabilityMayHaveChanged;
        character.CardDrawn -= HandlePlayabilityMayHaveChanged;
        character.CardDiscarded -= HandlePlayabilityMayHaveChanged;

        character.StatsChanged -= HandleIntentMayHaveChanged;

        // A character leaving the board is itself a change - the enemy that meant to swing at it needs
        // to pick something else.
        intentDirty = true;

        CharacterLeft?.Invoke(character);
    }

    // Two shapes because Character's events carry different payloads; both mean the same thing here,
    // and both are discarded - see the doc on PlayabilityChanged for why it carries nothing.
    private void HandlePlayabilityMayHaveChanged(Character _) => RaisePlayabilityChanged();

    private void HandlePlayabilityMayHaveChanged(Character _, Card __) => RaisePlayabilityChanged();

    private void HandleIntentMayHaveChanged(Character _) => intentDirty = true;

    private void HandleActionResolved(GameAction action, ActionContext ctx) => intentDirty = true;

    private void Start()
    {
        // Every board mutation resolves through here, so it is the one hook the intent refresh pass
        // needs beyond the per-character subscriptions Subscribe already sets up.
        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved += HandleActionResolved; }

        // Before anything reads CurrentLevel. Does nothing when the player came here from the Main
        // Menu; starts a throwaway run when this scene was opened on its own.
        RunManager.EnsureRun(debugCampaign);

        // The board first: every placement below needs tiles to exist, and the size is this level's to
        // decide, which is why GridManager no longer builds one in its own Awake.
        GridManager.Instance.BuildGrid(CurrentLevel != null ? CurrentLevel.BoardSize : Vector2Int.zero);

        // Characters placed directly in the scene (see the tooltip on `characters`) never pass through
        // AddCharacter, so they are wired up here instead. SpawnParty/SpawnEnemies route through
        // AddCharacter and subscribe themselves - looping over `characters` after them would double up.
        foreach (Character character in characters)
        {
            if (character == null) { continue; }

            Subscribe(character);

            // Their own Start places them, but nothing orders that against this Start, and the board
            // above may not have existed when it ran. Placing again here costs nothing when it did.
            if (character.Tile == null) { character.PlaceOnStartTile(); }
        }

        // Party and enemies both join the roster before anything below walks it.
        SpawnParty();
        SpawnEnemies();

        // Before the loop, not after: TurnStart draws, and the hand viewer only builds viewers for
        // whoever is active. Order is safe either way now - ActiveHandViewer reads ActiveCharacter in
        // its own Start if it happened to subscribe after this fired.
        SetActiveCharacter(FirstPlayableCharacter());

        ShowTutorialIfWanted();

        StartCoroutine(RunBattle());
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved -= HandleActionResolved; }
    }

    /// <summary>
    /// The How to Play prompt, on the first level of a run that asked for it and nowhere else.
    ///
    /// Gated on the run rather than on a flag of our own: the Game scene is reloaded for every level,
    /// so anything scene-local forgets between levels and would show this again each time - which is
    /// exactly what it used to do. RunManager is the only thing that survives the load.
    ///
    /// The notification is null-checked because this sits between SetActiveCharacter and
    /// StartCoroutine(RunBattle): an exception here would leave a battle that looks alive - clicking
    /// a character still works - but never draws a card. See the doc on ActiveCharacterChanged.
    ///
    /// RunBattle deliberately starts behind the tutorial rather than waiting for it - the director's
    /// own first step waits for PlayerActing, so the board deals itself in underneath the opening box.
    ///
    /// Hands off to TutorialDirector where there is one and falls back to the old single modal where
    /// there is not, so a scene without the tutorial overlay wired up still explains itself rather than
    /// silently teaching nothing.
    /// </summary>
    private void ShowTutorialIfWanted()
    {
        RunManager run = RunManager.Instance;

        if (run == null || !run.TutorialEnabled || run.LevelNumber != 1) { return; }

        if (TutorialDirector.Instance != null && TutorialDirector.Instance.Begin()) { return; }

        // Loud, not silent. This run explicitly asked for the tutorial, so a missing director is a
        // half-wired scene, not a preference - and the fallback below looks enough like success (a
        // "How to Play" box appears) that it otherwise hides the problem completely: no dimming, no
        // spotlight, and nothing stopping the player doing whatever they like.
        Debug.LogError($"{name}: this run asked for the tutorial but there is no TutorialDirector in "
                       + "the scene - run Tools > Tutorial > 2 - Wire Tutorial Overlay and save. "
                       + "Falling back to the old How to Play prompt.");

        if (NotificationManager.Instance == null) { return; }

        NotificationManager.Instance.Show(TutorialTitle, TutorialMessage);
    }

    /// <summary>
    /// Instantiates the run's party from RunManager and places it at this level's spawn cells, index
    /// for index. Skipped when either is missing - no run means this scene was opened stand-alone with
    /// no debug campaign to bootstrap from, and no level means there is nowhere authored to put
    /// anyone - in both cases whatever is already sitting in `characters` from the scene is the whole
    /// party, exactly as it was before either of these existed.
    ///
    /// Each member is rebuilt from its PartyMember record rather than left as the prefab authored it:
    /// the deck it has accumulated over the run and the damage it is carrying both live there. The
    /// record is also kept in `partyRecords`, because winning means writing that state back.
    ///
    /// Explicit per-member placement for the same reason SpawnEnemies uses PlaceOnGrid rather than
    /// each character's own Start: a character instantiated during this Start would not run its own
    /// Start until the end of the frame, and the first TurnStart happens before that.
    ///
    /// A party can now be larger than the level's authored spawn cells (up to 4, chosen at the select
    /// screen) and can hold duplicates of the same hero, so placement goes through
    /// GridManager.NearestFreeSpawnTile exactly as SpawnPlacement already does for enemies, rather than
    /// writing PlaceOnGrid(spawnCells[i]) unconditionally - see SpawnPlacement's own comment for why an
    /// occupied cell is a real bug (Character.MoveTo claims a tile unconditionally, leaving whoever was
    /// already there alive, visible, and permanently unclickable) and not just a cosmetic overlap.
    /// </summary>
    private void SpawnParty()
    {
        LevelData level = CurrentLevel;

        if (RunManager.Instance == null || level == null) { return; }

        IReadOnlyList<PartyMember> roster = RunManager.Instance.Party;
        IReadOnlyList<Vector2Int> spawnCells = level.PartySpawnCells;

        if (spawnCells.Count == 0)
        {
            Debug.LogWarning($"{level.name} has no party spawn cells authored - nobody placed");
            return;
        }

        // Duplicates are allowed (two Knights, say), and every reader of DisplayName -
        // SelectedCharacterPanel, reward titles - would otherwise show two identical "Knight"s with no
        // way to tell them apart. A name that appears once in the roster is left exactly as authored.
        Dictionary<string, int> totalByName = new();

        foreach (PartyMember entry in roster)
        {
            if (entry?.prefab == null) { continue; }

            string baseName = entry.prefab.DisplayName;
            totalByName[baseName] = totalByName.GetValueOrDefault(baseName) + 1;
        }

        Dictionary<string, int> spawnedByName = new();

        for (int i = 0; i < roster.Count; i++)
        {
            PartyMember record = roster[i];

            if (record == null || record.prefab == null) { continue; }

            // Past the authored cells, everyone else requests the last one - NearestFreeSpawnTile then
            // fans them out from there, the same way an over-full enemy wave already spreads from its
            // own requested cell.
            Vector2Int wanted = i < spawnCells.Count ? spawnCells[i] : spawnCells[^1];

            // Resolved before Instantiate: a board with no room left should cost no GameObject and no
            // roster entry - same reasoning as SpawnPlacement.
            GridTile tile = GridManager.Instance.NearestFreeSpawnTile(wanted);

            if (tile == null)
            {
                Debug.LogWarning($"{record.prefab.name} not spawned: {wanted} is taken and every "
                                 + "border tile is occupied");
                continue;
            }

            Character member = Instantiate(record.prefab);
            member.name = record.prefab.name;

            // Both after Instantiate, never before: Awake has already run BuildDeck and set Health to
            // full by the time anything can reach a fresh instance. Equipment goes on first, ahead of
            // SetDeck - BuildDeck constructs every Card through Character.NewCard, which reads the
            // equipment list as it applies each card's modifiers, so this order is what makes a
            // returning hero's deck arrive already tuned instead of needing a second full-deck pass.
            member.SetEquipment(record.equipment);
            member.SetDeck(record.deck);
            member.SetHealth(record.currentHealth);

            string baseDisplayName = record.prefab.DisplayName;

            if (totalByName[baseDisplayName] > 1)
            {
                int occurrence = spawnedByName.GetValueOrDefault(baseDisplayName) + 1;
                spawnedByName[baseDisplayName] = occurrence;
                member.SetDisplayName($"{baseDisplayName} {occurrence}");
            }

            // The resolved cell, never `wanted` - PlaceOnGrid writes startCoordinates, and
            // Character.Start re-places itself from those at the end of the frame, so handing it the
            // requested cell would drag a relocated hero back on top of whoever displaced it. Same
            // reasoning as SpawnPlacement.
            member.PlaceOnGrid(tile.Coordinates);

            partyRecords[member] = record;

            AddCharacter(member);
        }
    }

    /// <summary>
    /// Instantiates this level's authored enemies and adds them to the roster.
    ///
    /// Here rather than in a spawner of its own because ordering is the whole difficulty: the roster
    /// has to be complete before anything walks it, and two components' Awakes have no guaranteed
    /// order between them. BattleManager already owns the roster, so it does the spawning.
    /// </summary>
    private void SpawnEnemies()
    {
        if (CurrentLevel == null) { return; }

        foreach (EnemyPlacement placement in CurrentLevel.Enemies)
        {
            SpawnPlacement(placement);
        }
    }

    /// <summary>
    /// Instantiates one authored placement onto the board and puts it on the roster.
    ///
    /// The cell it was authored on is a request, not a guarantee: Character.MoveTo claims a tile
    /// unconditionally, so spawning onto an occupied cell would overwrite that tile's Occupant and
    /// leave the character already standing there alive, visible, and permanently unclickable - it
    /// keeps its Tile pointer while the tile now points at the newcomer, and the tile under a body is
    /// the only thing you can click. GridManager answers where the body can actually go; a taken cell
    /// sends it to the nearest free border tile.
    ///
    /// Shared by the opening roster and the waves because the two had drifted into byte-for-byte
    /// duplicates, and an occupancy rule fixed in one of them is a rule the other still gets wrong.
    ///
    /// Placed explicitly here rather than left to the character's own Start - one instantiated during
    /// BattleManager.Start would not run its Start until the end of the frame, and the first TurnStart
    /// happens before that. It would be asked for an intent with no tile, and answer Wait.
    /// </summary>
    private void SpawnPlacement(EnemyPlacement placement)
    {
        if (placement.prefab == null) { return; }

        // Resolved per placement, inside the caller's loop rather than once for a whole wave: the
        // sequence below is fully synchronous, so the second enemy of a wave sees the first one's
        // claim and cannot be handed the same tile.
        GridTile tile = GridManager.Instance.NearestFreeSpawnTile(placement.cell);

        // Before Instantiate: a board with no room left should cost no GameObject and no roster entry.
        if (tile == null)
        {
            Debug.LogWarning($"{placement.prefab.name} not spawned: {placement.cell} is taken and "
                             + "every border tile is occupied");
            return;
        }

        GameObject enemyObject = Instantiate(placement.prefab, enemyParent);
        Character enemy = enemyObject.GetComponent<Character>();
        enemy.name = $"{placement.prefab.name} {tile.Coordinates.x},{tile.Coordinates.y}";

        if (placement.deckOverride != null && placement.deckOverride.Count > 0)
        {
            enemy.SetDeck(placement.deckOverride);
        }

        // The resolved cell, never placement.cell. PlaceOnGrid writes startCoordinates, and
        // Character.Start re-places itself from those at the end of the frame - handing it the
        // authored cell would quietly drag a relocated enemy back on top of whoever displaced it.
        enemy.PlaceOnGrid(tile.Coordinates);

        AddCharacter(enemy);
    }

    /// <summary>
    /// Spawns every LevelData wave keyed to the round TurnsElapsed just reached. Called from TurnStart
    /// before the per-character loop, so a wave enemy draws a hand and commits an intent the same round
    /// it lands - the same reasoning SpawnEnemies uses for the opening roster.
    /// </summary>
    private void SpawnDueWaves()
    {
        if (CurrentLevel == null) { return; }

        foreach (EnemyWave wave in CurrentLevel.Waves)
        {
            if (wave.turn != TurnsElapsed || wave.enemies == null) { continue; }

            foreach (EnemyPlacement placement in wave.enemies)
            {
                SpawnPlacement(placement);
            }
        }
    }

    private void Update()
    {
        ClearStuckHover();

        if (Keyboard.current == null) { return; }

        // Keyboard shortcut for the End Turn button, and the only other way out of PlayerActing -
        // nothing ends the turn on its own any more, so a scene whose button came unwired would
        // otherwise hang the round forever.
        if (Keyboard.current.enterKey.wasPressedThisFrame) { RequestEndTurn(); }

        // Debug: draw a card for whoever is active.
        if (Keyboard.current.spaceKey.wasPressedThisFrame && ActiveCharacter != null)
        {
            ActiveCharacter.DrawCard();
        }
    }

    /// <summary>
    /// Keeps every enemy's CommittedIntent - and so the icon over its head - equal to what would
    /// actually happen if EnemyResolve ran right now. Runs once per frame rather than from inside each
    /// handler, so one card that queues three actions produces one recompute instead of three.
    ///
    /// Only while the player is rearranging the board. EnemyResolve clears each enemy's icon the
    /// instant it finishes acting, and a pass landing after that would hand the icon straight back
    /// before the next enemy has even gone; TurnStart re-commits everyone from a fresh board the
    /// moment PlayerActing begins again, so a change dropped here while it is not our turn is never
    /// actually lost.
    /// </summary>
    private void LateUpdate()
    {
        if (!intentDirty) { return; }

        intentDirty = false;

        if (Phase != BattlePhase.PlayerActing) { return; }

        Board board = GridManager.Instance.Read();

        foreach (Character enemy in LivingEnemies())
        {
            Intent next = Decide(enemy, board);

            // Kind only - assigning an identical kind would still raise IntentChanged and roll the
            // icon over to the sprite it is already showing.
            if (next.kind == enemy.CommittedIntent.kind) { continue; }

            enemy.CommittedIntent = next;
        }
    }

    /// <summary>
    /// Drops the hover tint from the board the moment input locks.
    ///
    /// TileSelector gates OnMouseEnter, which stops a *new* tile lighting up, but a tile already lit
    /// when the modal opened never receives OnMouseExit - the cursor has not moved, it has just
    /// stopped mattering - so it sits there yellow behind the panel. Watching the rising edge here
    /// rather than having Show call in keeps the knowledge of what a lock means in one place, and
    /// covers the reward panels for free.
    /// </summary>
    private void ClearStuckHover()
    {
        bool locked = InputLocked;

        if (locked == inputWasLocked) { return; }

        inputWasLocked = locked;

        if (locked && GridManager.Instance != null)
        {
            GridManager.Instance.ClearHoveredTiles();
            // Same staleness this whole method exists to fix, one layer up: a tile lit red by the
            // area preview, or a health bar showing yellow from the damage preview, when the modal
            // opened gets no OnMouseExit either, since the cursor never moved.
            GridManager.Instance.ClearAreaPreview();
            GridManager.Instance.ClearDamagePreview();
        }
    }

    /// <summary>
    /// Registers a character that joins the battle after Start - a summon, not one of the characters
    /// placed in the scene or spawned from EnemyPlacement. TurnStart, EnemyResolve and the win/loss
    /// checks all walk `characters`, so anything summoned without going through here just stands there
    /// and never gets a turn.
    /// </summary>
    public void AddCharacter(Character character)
    {
        if (character == null || characters.Contains(character)) { return; }

        characters.Add(character);
        Subscribe(character);

        // A body that just joined has a hand and an energy pool of its own, so what the party can
        // still do has changed - a summoned ally is one more set of cards to light up.
        RaisePlayabilityChanged();
    }

    /// <summary>
    /// Reacts to a character leaving the board. Character.CheckDeath only clears its own board state
    /// (Tile/Occupant) - this is the "something else" that reacts to Died and owns everything that
    /// follows from it, the same split as CardDrawn/ActiveHandViewer.
    ///
    /// Runs while `character.Tile` is still the tile it died on - see the comment on CheckDeath - so
    /// loot lands where the character actually fell, not nowhere.
    /// </summary>
    private void HandleCharacterDied(Character character)
    {
        if (character.ItemDropPrefab != null && character.Tile != null)
        {
            // Not `CurrentLevel?.LootTable` - null-conditional on a UnityEngine.Object skips its
            // overloaded == and so misses the fake-null case, same reasoning as Singleton.OnDestroy.
            LevelData level = CurrentLevel;
            LootTable table = character.LootTable != null ? character.LootTable
                : level != null ? level.LootTable : null;

            if (table != null)
            {
                character.Tile.DropItem(character.ItemDropPrefab, table.Roll(), table);
            }

            // Loot this character stole by walking over it (see GridTile.TryPickUpItem) falls with it -
            // an enemy carrying a hero's near-miss reward does not get to leave the board with it.
            foreach ((Rarity rarity, LootTable carriedTable) in character.CarriedLoot)
            {
                character.Tile.DropItem(character.ItemDropPrefab, rarity, carriedTable);
            }
        }

        // A hero who falls is out of the run for good - not revived next level, and not holding a spawn
        // cell. Done here rather than in the victory write-back because the party can lose a member on
        // a level it goes on to win, and by then the body is long destroyed.
        if (partyRecords.TryGetValue(character, out PartyMember record))
        {
            partyRecords.Remove(character);

            if (RunManager.Instance != null) { RunManager.Instance.RemoveMember(record); }
        }

        if (character == ActiveCharacter) { SetActiveCharacter(FirstPlayableCharacter()); }
        if (character == SelectedCharacter) { SetSelectedCharacter(null); }

        // Before Destroy, while the events are still there to detach from. The object going away
        // would drop them anyway, but leaving a dead character subscribed means every one of its
        // stat changes on the way out raises PlayabilityChanged at listeners re-reading a corpse.
        Unsubscribe(character);

        Destroy(character.gameObject);
    }

    /// <summary>
    /// Writes a chosen reward into the run so it survives to the next level. Goes through the
    /// PartyMember record rather than Character.AuthoredDeck - AuthoredDeck is read-only for exactly
    /// this reason, see RunManager's PartyMember doc comment: handing a reward straight to the prefab's
    /// own list would persist it into the .asset file on disk once Play Mode exits.
    ///
    /// A no-op for an enemy or any character with no run record - loot rolled on a summon, say, is
    /// good for this battle only.
    /// </summary>
    public void RecordRunCard(Character character, CardData card)
    {
        if (character == null || card == null) { return; }

        if (partyRecords.TryGetValue(character, out PartyMember record)) { record.deck.Add(card); }
    }

    /// <summary>
    /// Writes an equipped item into the run so it survives to the next level - RecordRunCard's
    /// counterpart. Deliberately does not also call Character.Equip: LootManager already does that for
    /// the live instance at the moment of pickup, and calling it again here would apply the same item's
    /// Project/Apply a second time on a character who is about to be torn down anyway. A level-clear
    /// grant, which has no live Character left to equip, is the one path that reaches this and nothing
    /// else - see LootManager.OfferLevelClear.
    /// </summary>
    public void RecordRunEquipment(Character character, EquipmentData item)
    {
        if (character == null || item == null) { return; }

        if (partyRecords.TryGetValue(character, out PartyMember record)) { record.equipment.Add(item); }
    }

    private Character FirstPlayableCharacter()
    {
        foreach (Character character in characters)
        {
            if (character != null && character.IsPlayerControlled && !character.IsDead) { return character; }
        }

        return null;
    }

    private IEnumerator RunBattle()
    {
        TurnsRemaining = TurnsToSurvive;
        TurnsElapsed = 0;
        RefreshTurnCounter();

        while (true)
        {
            yield return StartCoroutine(TurnStart());

            Phase = BattlePhase.PlayerActing;
            endTurnRequested = false;

            // The phase flip is the one thing the End Turn button watches that no character event
            // announces, so it is said out loud here.
            RaisePlayabilityChanged();

            // Only the button ends the turn. This used to also break on !CanAnyoneAct(), which meant
            // the round could roll over underneath the player the instant the last affordable card
            // was played - no beat to read the board, and no way to deliberately hold energy back.
            // CanAnyoneAct survives as the *visual* gate: see EndTurnButton, which dims itself while
            // it is true rather than locking the click.
            while (!endTurnRequested) { yield return null; }

            // A pickup queued on the very last action of the turn must resolve before the round rolls
            // over - otherwise EnemyResolve starts underneath a reward panel that is still up.
            yield return new WaitUntil(LootIdle);

            // Heroes' hands clear the moment End Turn is pressed, not at the top of the next round -
            // the board should read as "your turn is over" while enemies act, not still show the hand
            // you just played. Enemies are untouched here: EnemyResolve is about to read straight out
            // of enemy.Hand, so their own discard-and-redraw stays where it always was, in TurnStart.
            foreach (Character hero in characters)
            {
                if (hero == null || hero.IsDead || !hero.IsPlayerControlled) { continue; }

                hero.DiscardHand();
            }

            // The viewers are still flying to the discard pile - the round must not roll over on top
            // of that animation, the same bargain the LootIdle wait above already strikes.
            yield return new WaitUntil(() => ActiveHandViewer.Instance == null || !ActiveHandViewer.Instance.Busy);

            TickStatuses(playerControlled: true);

            // Tiles belong to nobody's "own phase" - ticked once per round, here, rather than split by
            // side like TickStatuses. See GridManager.TickTileEffects.
            if (GridManager.Instance != null) { GridManager.Instance.TickTileEffects(); }

            // The tutorial explains what the discard meant and what the enemy is about to do, and both
            // beats have to land in this gap - after the hand has cleared, before anything acts on it.
            // Same shape as the two waits above: the round must not roll over on top of something still
            // finishing.
            yield return new WaitUntil(() =>
                TutorialDirector.Instance == null || !TutorialDirector.Instance.HoldingRound);

            yield return StartCoroutine(EnemyResolve());

            int turnsBefore = TurnsRemaining;

            ReduceTurns();

            if (AllHeroesDead())
            {
                if (NotificationManager.Instance != null)
                {
                    NotificationManager.Instance.Show("Defeat", "All Heroes were Slain.");
                }

                yield return WaitForAcknowledgement();
                Finish(victory: false);
                yield break;
            }

            // Between the two terminal checks on purpose. A wipe wants its modal, not a flourish, but
            // the winning turn does want to see the counter land on 0 before Victory. The label write
            // rides along inside the roll so the HUD and the giant number flip on the same frame; the
            // else branch is what keeps a scene with no viewer wired up correct.
            if (turnTransition != null)
            {
                yield return StartCoroutine(
                    turnTransition.Play(turnsBefore, TurnsRemaining, RefreshTurnCounter));
            }
            else
            {
                RefreshTurnCounter();
            }

            if (TurnsRemaining <= 0)
            {
                yield return StartCoroutine(EndLevel());
                yield break;
            }
        }
    }

    /// <summary>
    /// The winning turn. Which modal shows and whether rewards are offered both hinge on the one
    /// question - is there a level after this one - which RunManager.IsFinalLevel answers without
    /// moving the index the way AdvanceLevel does.
    ///
    /// Victory is the end of the *run*, not the end of a level. Showing it between levels spent the
    /// only beat the game has for its ending on a room the player is about to walk out of; the
    /// levels in between get a plainer acknowledgement instead.
    /// </summary>
    private IEnumerator EndLevel()
    {
        RunManager run = RunManager.Instance;

        // No run at all means this scene was opened stand-alone with nothing to advance to, which is
        // the last level by any useful definition.
        bool finalLevel = run == null || run.IsFinalLevel;

        // A cleared run with another queued behind it is not the end of anything - it is the tutorial
        // handing over. Checked before the final-level branch, because the tutorial *is* its run's final
        // level and would otherwise be congratulated for finishing the game.
        bool handingOver = run != null && run.HasFollowOn;

        if (NotificationManager.Instance != null)
        {
            if (handingOver)
            {
                NotificationManager.Instance.Show("Ready",
                    "That is everything you need. The real run starts now - same heroes, full decks.");
            }
            else if (finalLevel)
            {
                NotificationManager.Instance.Show("Victory", "The party made it out!");
            }
            else
            {
                NotificationManager.Instance.Show($"Level {run.LevelNumber} Cleared",
                    "The party pushes on. Another door, another room - take something with you.");
            }
        }

        yield return WaitForAcknowledgement();

        // Skipped on the last level: there is no next level to carry a new card into, and Finish
        // ends the run either way, so the offer would be a choice with nothing behind it.
        //
        // Before Finish, not inside it: Finish loads a scene synchronously, which would destroy the
        // very Characters each panel is named after, mid-offer.
        if (!finalLevel) { yield return StartCoroutine(OfferLevelClearRewards()); }

        Finish(victory: true);
    }

    private void ReduceTurns() => TurnsRemaining--;

    /// The HUD label. Deliberately not written by ReduceTurns: the giant countdown is what reveals the
    /// new number, and a small label that has already flipped spoils it. TurnTransitionViewer calls
    /// this back at the exact frame the new number takes the centre, so both flip on the same frame.
    private void RefreshTurnCounter() => turnCounter.text = TurnsRemaining.ToString();

    private IEnumerator TurnStart()
    {
        Phase = BattlePhase.TurnStart;
        TurnsElapsed++;

        SpawnDueWaves();

        foreach (Character character in characters)
        {
            if (character == null || character.IsDead) { continue; }

            character.DiscardHand();

            // Innate cards go back into hand before the top-up draw, so they occupy a real hand slot
            // rather than inflating hand size past HandSize.
            character.RestoreInnateCards();

            // Everyone draws, enemies included. Their cards are how they act at all now, so a goblin
            // with an empty hand has nothing to choose between and can only Wait.
            character.DrawCards(HandSize - character.Hand.Count);

            character.TickCardTimers();

            character.ResetEnergy();

            // Shield wipes itself in here. BattleManager does not know that - it just says the turn
            // started and lets each status decide what that means. See Character.OnTurnStart.
            character.OnTurnStart();
        }

        // Every OnTurnStart hook has run by here - Shield has wiped itself, and none of it went
        // through an action. Anything showing a character's stats needs telling.
        TurnAdvanced?.Invoke();

        // TickCardTimers above is the reason this is not left to ResetEnergy's StatsChanged: a
        // card coming off cooldown (or waking from Dormant) changes nothing about the character
        // carrying it, so the only announcement it would otherwise get is none.
        RaisePlayabilityChanged();

        // Enemies commit an opening intent now, at the top of your turn - LateUpdate takes over from
        // here and keeps every enemy's intent live for the rest of PlayerActing as the board changes.
        Board board = GridManager.Instance.Read();

        foreach (Character enemy in LivingEnemies())
        {
            // The setter raises IntentChanged, which is what puts the icon up - see
            // CharacterOverheadViewer.
            enemy.CommittedIntent = Decide(enemy, board);

            if (!enemy.CommittedIntent.IsWait)
            {
                Debug.Log($"{enemy.name} intends: {enemy.CommittedIntent}");
            }
        }

        yield return null;
    }

    /// <summary>
    /// Whether the player has any move left. Deliberately "can anyone still play something", not "is
    /// everyone at zero energy" - a character sitting on 1 energy holding only 2-cost cards has no
    /// move, and an empty hand falls out of the same question.
    ///
    /// Asks Card.PlayRefusal, the same predicate the click and the card highlight are built from, so
    /// a hand this reports as spent is exactly a hand with no green cards in it. It used to ask
    /// CanAct and CanAfford directly and so missed Cooldown - a hand of recharging cards read as
    /// playable while every one of them refused the click.
    ///
    /// Now purely a display question: it gates the End Turn button's dim, not the turn itself. The
    /// early-out on the character before touching its hand is worth keeping - PlayRefusal calls
    /// ActRefusal, which allocates.
    /// </summary>
    public bool CanAnyoneAct()
    {
        foreach (Character character in characters)
        {
            if (character == null || !character.IsPlayerControlled || !character.CanAct) { continue; }

            foreach (Card card in character.Hand)
            {
                if (card.PlayRefusal(character) == null) { return true; }
            }
        }

        return false;
    }

    private IEnumerator EnemyResolve()
    {
        Phase = BattlePhase.EnemyResolve;

        foreach (Character enemy in LivingEnemies())
        {
            // Frozen burns the whole turn, not one action - there is no partial thaw. Clear the
            // intent too, or a frozen enemy would wear a ghost icon into the next turn.
            if (!enemy.CanAct)
            {
                Debug.Log($"{enemy.name} is frozen and loses its turn");
                enemy.CommittedIntent = Intent.Wait();
                continue;
            }

            for (int ap = 0; ap < enemy.ActionPoints && !enemy.IsDead; ap++)
            {
                // Every action point decided outright against the live board. CommittedIntent is only
                // ever what the icon shows - it is kept live by the refresh pass, not consulted here.
                Intent step = Decide(enemy, GridManager.Instance.Read());

                if (step.IsWait) { break; }

                yield return StartCoroutine(Execute(enemy, step));

                // AddAction resolves the first action synchronously, so "queued" is not "finished".
                // Also waits on LootManager: an enemy can shove a hero onto a loot tile, and the reward
                // panel that opens for it must resolve before the next action point spends.
                yield return new WaitUntil(() => ActionManager.Instance.IsIdle && LootIdle());
            }

            // Clears this enemy's icon the moment it is done, rather than every icon vanishing at
            // once when EnemyResolve began - so mid-resolve you can see who is still owed an action.
            enemy.CommittedIntent = Intent.Wait();
        }

        // Actions resolve through the queue, and AddAction runs the first one synchronously, so
        // "queued" is not "finished". Without this the turn would roll over mid-animation.
        yield return new WaitUntil(() => ActionManager.Instance.IsIdle && LootIdle());

        TickStatuses(playerControlled: false);
    }

    /// True when no reward panel is up or queued. A LootManager-less scene (a test harness, or one
    /// that simply has no loot yet) must not block the turn loop forever, hence the null check.
    private static bool LootIdle() => LootManager.Instance == null || LootManager.Instance.IsIdle;

    /// Asks this character's brain for one action. Wait if it has no brain, which is every player.
    private static Intent Decide(Character character, Board board)
    {
        EnemyBrain brain = EnemyBrain.For(character.Brain);

        if (brain == null || character.Tile == null) { return Intent.Wait(); }

        return brain.Decide(character, board);
    }

    /// <summary>
    /// Plays the decided card, if it is still legal.
    ///
    /// The fizzle is Card.Refusal saying no - the same call the player's click is gated on. Decide is
    /// asked fresh against the live board immediately before this runs, so a fizzle here means the
    /// board changed in the single frame between deciding and acting, not that a stale plan met a
    /// rearranged board.
    ///
    /// Resolution goes through ResolveEffects exactly as a played card does, so enemy attacks pick up
    /// Strength, Double Attack and the target's armor for free. Nothing in the card pipeline needed
    /// to learn that enemies exist.
    /// </summary>
    private IEnumerator Execute(Character enemy, Intent step)
    {
        GridTile tile = GridManager.Instance.GetTile(step.target);

        string refusal = tile == null ? "that tile is gone" : step.card.Refusal(enemy, tile);

        if (refusal != null)
        {
            //TODO: a visible fizzle. This currently only reads in the console, so a plan you broke
            //looks like an enemy that did nothing rather than like your block working.
            Debug.Log($"{enemy.name} tries {step.card.cardName} at {step.target} - {refusal}");
            yield break;
        }

        Debug.Log($"{enemy.name} plays {step.card.cardName} at {step.target}");

        enemy.Discard(step.card);
        step.card.ResolveEffects(enemy, tile);

        // The one place the pattern is spent. Only attacks, and only ones that really went off - the
        // early `yield break` above on a refusal leaves an owed "Attack: Closest" still owed. A Move
        // or a blocked Summon that upgraded into an Attack does consume it, because by now it is one.
        if (step.kind == IntentKind.Attack) { enemy.AdvanceTargetingCursor(); }

        yield return new WaitForSeconds(stepDuration);
    }

    /// <summary>
    /// Ticks one side's statuses by one - player-controlled characters at the end of PlayerActing,
    /// enemies at the end of EnemyResolve. Each side ticks at the end of its *own* phase rather than
    /// both together at the shared TurnStart, so a duration always means "survives this many of my own
    /// turns" no matter which side applied the status. Ticking everyone at TurnStart instead would
    /// make that number direction-dependent: a status put on an enemy mid-PlayerActing would coast
    /// through that same cycle's EnemyResolve untouched, while one put on a player during EnemyResolve
    /// would get ticked down at the very next TurnStart before that player ever got to act on it -
    /// the same number would then mean two different things depending on who cast it.
    ///
    /// This only calls Character.OnTurnEnd. What a tick *does* is each status's own business now: see
    /// Status for the three shapes, and PoisonStatus for the one that both bites and decays here.
    /// </summary>
    private void TickStatuses(bool playerControlled)
    {
        foreach (Character character in characters)
        {
            if (character == null || character.IsDead || character.IsPlayerControlled != playerControlled)
            {
                continue;
            }

            character.OnTurnEnd();
        }

        // Poison has just bitten, and like the turn-start hooks it bypassed ActionManager entirely.
        TurnAdvanced?.Invoke();

        // OnTurnEnd is where a Frozen wears off, which un-dims a whole hand at once.
        RaisePlayabilityChanged();
    }

    /// <summary>
    /// A snapshot, not a live view over `characters`. EnemyResolve holds this enumerator open across
    /// yields while an enemy's actions resolve, and a Summon card resolving mid-loop appends to
    /// `characters` via AddCharacter - enumerating the live list would then throw "collection was
    /// modified" the next time this generator resumes. A character that joins mid-resolve simply
    /// doesn't get a turn until the next TurnStart, same as one spawned via SpawnEnemies.
    /// </summary>
    private IEnumerable<Character> LivingEnemies()
    {
        foreach (Character character in characters.ToArray())
        {
            if (character != null && !character.IsPlayerControlled && !character.IsDead)
            {
                yield return character;
            }
        }
    }

    private bool AllHeroesDead()
    {
        foreach (Character character in characters)
        {
            if (character != null && character.IsPlayerControlled && !character.IsDead) { return false; }
        }

        return true;
    }

    /// <summary>
    /// Blocks until the player dismisses the notification that is up.
    ///
    /// Not WaitForSeconds: NotificationManager.Show sets Time.timeScale to 0, and WaitForSeconds is
    /// scaled, so a wait behind a notification never elapses at all. That the old fixed 0.15s wait
    /// happened to do the right thing was an accident of exactly that bug - it resumed when Hide put
    /// the timescale back. A WaitUntil is the same behaviour said out loud, and coroutines still get a
    /// frame at timeScale 0 for it to poll in.
    /// </summary>
    private IEnumerator WaitForAcknowledgement()
    {
        NotificationManager notifications = NotificationManager.Instance;

        if (notifications == null) { yield break; }

        yield return new WaitUntil(() => notifications == null || !notifications.IsShowing);
    }

    /// <summary>
    /// Offers each surviving hero their own share of the level-clear reward, one at a time so only one
    /// panel is ever up. Runs before Finish, not inside it: Finish loads a new scene synchronously,
    /// which would destroy these very Characters (their names title each panel) mid-offer.
    ///
    /// Walks `characters` rather than `partyRecords` so the order the player sees is spawn order - a
    /// Dictionary makes no ordering promise. A hero who died this level is already out of both
    /// `characters` and `partyRecords` (see HandleCharacterDied), so they are skipped for free rather
    /// than needing a separate death check here.
    /// </summary>
    private IEnumerator OfferLevelClearRewards()
    {
        LootManager loot = LootManager.Instance;
        LevelData level = CurrentLevel;

        if (loot == null || level == null) { yield break; }

        foreach (Character hero in new List<Character>(characters))
        {
            if (hero == null || hero.IsDead || !hero.IsPlayerControlled) { continue; }
            if (!partyRecords.TryGetValue(hero, out PartyMember record)) { continue; }

            yield return StartCoroutine(loot.OfferLevelClear(hero, record, level.ClearRewardTable));
        }
    }

    /// <summary>
    /// Ends the battle and hands back to the run.
    ///
    /// Victory carries the party forward: what everyone finished the level with is written back to
    /// their run records before the level index moves, because the live characters are about to be
    /// destroyed with the scene and the records are all that survive. Defeat ends the run outright -
    /// there is no continue.
    ///
    /// The last level cleared lands in the same place as a defeat, the Main Menu, but by way of
    /// EndRun rather than in spite of it. Skipping that would leave a finished run sitting in the
    /// RunManager for the next Play button to resume. What tells the two apart for the player is the
    /// modal EndLevel put up on the way in, not anything here.
    /// </summary>
    private void Finish(bool victory)
    {
        Phase = BattlePhase.Finished;

        RunManager run = RunManager.Instance;

        // Show zeroed it and nothing else puts it back before the next scene loads - a frozen Main
        // Menu is a hard lockup with no way out.
        Time.timeScale = 1f;

        if (!victory)
        {
            Debug.Log($"battle over: defeat - every hero is down ({TurnsRemaining} turns left)");

            if (run != null) { run.EndRun(); }

            SceneManager.LoadScene("MainMenu");
            return;
        }

        foreach (KeyValuePair<Character, PartyMember> entry in partyRecords)
        {
            if (entry.Key != null) { entry.Value.currentHealth = entry.Key.Health; }
        }

        if (run != null && run.AdvanceLevel())
        {
            Debug.Log($"battle over: victory - on to level {run.LevelNumber}");
            SceneManager.LoadScene("Game");
            return;
        }

        // A cleared run with something queued behind it rolls straight on rather than ending. The
        // tutorial is the reason: it is its own prologue run, and finishing it should hand the player a
        // real party on their starter decks, not drop them back at the menu to press Play again.
        if (run != null && run.BeginFollowOn())
        {
            Debug.Log("battle over: victory - tutorial complete, starting the run proper");
            SceneManager.LoadScene("Game");
            return;
        }

        Debug.Log("battle over: victory - run complete");

        if (run != null) { run.EndRun(); }

        SceneManager.LoadScene("MainMenu");
    }
}
