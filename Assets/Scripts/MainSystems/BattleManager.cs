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
///                  statuses tick once every enemy has acted, and tile effects tick after that -
///                  a wall stands for the whole round it was paid for, enemy movement included.
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
    ///
    /// Public so a HUD element (NextWavePanel) can read the wave schedule directly - reading
    /// RunManager.Instance.CurrentLevel instead would show nothing when this scene is played
    /// stand-alone, which is exactly the case the serialized-field fallback exists for.
    /// </summary>
    public LevelData CurrentLevel =>
        RunManager.Instance != null && RunManager.Instance.CurrentLevel != null
            ? RunManager.Instance.CurrentLevel
            : levelData;

    [Tooltip("Where spawned enemies are parented. Optional - tidiness only.")]
    [SerializeField] private Transform enemyParent;

    [Tooltip("Every body EncounterRoller may draw from for CurrentLevel.EncounterBudget. Optional - a "
             + "level whose budget fields are all 0 (the default) never needs one, same fallback story "
             + "as debugCampaign above.")]
    [SerializeField] private EnemyRegistry enemyRegistry;

    /// Resolved once, at battle start, by RollEncounter - see its own doc comment for why rolling
    /// happens exactly there and only there. Empty whenever CurrentLevel's EncounterBudget is all 0.
    private readonly List<EnemyPlacement> rolledStartingEnemies = new();

    private readonly List<EnemyWave> rolledWaves = new();

    [Tooltip("Frames the board once it is built, so the whole thing is on screen whatever size the "
             + "level asked for. Optional: leave it unassigned and the camera simply stays where it "
             + "is, which is what a scene that has not been through Tools/Board/2 - Wire Cameras does.")]
    [SerializeField] private BoardCamera boardCamera;

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

    /// <summary>
    /// The snapshot EnemyResolve is currently walking, and how far through it - null/-1 outside
    /// EnemyResolve. Exists so LockAimsOn can answer "which enemies still have an action point coming
    /// this phase" without iterating the LivingEnemies() generator anonymously: everyone from
    /// enemyResolveIndex onward (the enemy currently acting included) has a turn still ahead of it,
    /// everyone before it has already finished and would never read a lock this sets.
    /// </summary>
    private List<Character> enemyResolveOrder;

    private int enemyResolveIndex = -1;

    /// Which run record each spawned party member came from, so the state it finishes the level with
    /// can be written back to the run. Only party members are in here - an enemy has nothing that
    /// outlives the battle.
    private readonly Dictionary<Character, PartyMember> partyRecords = new();

    /// Set by LootManager while a reward panel is up. The other half of InputLocked.
    private bool rewardPanelUp;

    /// <summary>
    /// True while a modal owns the screen - a reward panel, a notification, the pause menu, or the
    /// debug tools. Not Time.timeScale, even though the pause menu does also freeze time -
    /// that would stall the WaitForSeconds inside EnemyResolve (an enemy shoving a hero onto loot
    /// takes that exact path) and would not actually block a click, since OnMouseDown is a physics
    /// raycast no uGUI panel intercepts. This is the real gate; OnTileClicked and
    /// CardPlayManager.OnCardClicked both check it.
    ///
    /// The notification and pause halves are *pulled* rather than pushed, the same way auras are. If
    /// Show/Hide
    /// wrote the flag instead, a notification opening over a reward panel would clear the panel's
    /// lock when it was dismissed - one bool cannot remember that two things wanted it held.
    /// </summary>
    public bool InputLocked =>
        rewardPanelUp
        || (NotificationManager.Instance != null && NotificationManager.Instance.IsShowing)
        || (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen)

        // The debug panel does not freeze time, so this is the only thing stopping a click that
        // lands beside its window from selecting whoever is standing there. A full-screen Canvas
        // is not enough on its own: tile clicks are OnMouseDown physics raycasts, which no uGUI
        // panel intercepts - the same reason this property exists at all rather than relying on
        // the reward panel covering the screen.
        || (DebugPanel.Instance != null && DebugPanel.Instance.IsOpen);

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

        // Above the tutorial gate on purpose, unlike everything below it. A debug spawn is not a move
        // the player is making, so a tutorial step that permits one specific tile has no business
        // refusing it - the point of the tool is to break the situation open and look at it.
        if (armedDebugSpawn != null)
        {
            GameObject prefab = armedDebugSpawn;
            bool asSummon = armedDebugIsSummon;

            // Cleared before placing, not after: a failed placement must not leave the click
            // armed, or the next click anywhere on the board tries again.
            ClearDebugSpawnArm();

            bool placed = asSummon ? DebugSummon(prefab, tile) : DebugSpawn(prefab, tile.Coordinates);

            if (!placed)
            {
                Debug.LogWarning($"debug spawn: {prefab.name} could not be placed at {tile.Coordinates}");
            }

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

    [Tooltip("Looping battle track. Played through AudioManager, which survives the scene reload"
             + " between levels so the music does not restart on each one.")]
    [SerializeField] private AudioClip battleMusic;

    private void Start()
    {
        // Every board mutation resolves through here, so it is the one hook the intent refresh pass
        // needs beyond the per-character subscriptions Subscribe already sets up.
        if (ActionManager.Instance != null) { ActionManager.Instance.ActionResolved += HandleActionResolved; }

        // Handed to AudioManager rather than played by a scene AudioSource: this scene reloads on
        // every level, and a scene-owned source restarts its track each time. PlayMusic is a no-op
        // when the same clip is already running, so level two continues level one's music.
        if (AudioManager.Instance != null) { AudioManager.Instance.PlayMusic(battleMusic); }

        // Before anything reads CurrentLevel. Does nothing when the player came here from the Main
        // Menu; starts a throwaway run when this scene was opened on its own.
        RunManager.EnsureRun(debugCampaign);

        // The board first: every placement below needs tiles to exist, and the size is this level's to
        // decide, which is why GridManager no longer builds one in its own Awake.
        //
        // The seed is rolled once here rather than inside BoardVisuals so a rebuild of the same battle
        // lays down the identical floor. BuildGrid runs again on every level load, and a floor that
        // reshuffled its blocks underneath the player each time would read as the board flickering.
        // Fully qualified: System.Random is used two lines down, so a bare `Random` here is ambiguous.
        int floorSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        TileSetData tileSet = CurrentLevel != null
            ? CurrentLevel.PickTileSet(new System.Random(floorSeed))
            : null;

        GridManager.Instance.BuildGrid(
            CurrentLevel != null ? CurrentLevel.BoardSize : Vector2Int.zero, tileSet, floorSeed);

        // Immediately after, and before anything is placed on it: the board's extent is the only input
        // the framing needs, and it is final the moment BuildGrid returns.
        if (boardCamera != null) { boardCamera.Frame(GridManager.Instance.BoardBounds); }

        // After the board exists (EncounterRoller needs BoardSize) and before SpawnEnemies reads
        // rolledStartingEnemies. Rolled once here rather than lazily per wave so NextWavePanel can
        // preview a generated wave's real portraits from the very first frame - see TryNextWave.
        RollEncounter();

        // Characters placed directly in the scene (see the tooltip on `characters`) never pass through
        // AddCharacter, so they are wired up here instead. SpawnParty/SpawnEnemies route through
        // AddCharacter and subscribe themselves - looping over `characters` after them would double up.
        foreach (Character character in characters)
        {
            if (character == null) { continue; }

            // The one thing AddCharacter does that a scene-placed body would otherwise miss. Harmless
            // on the party and on a friendly summon, which it declines by side - see ApplyDifficulty.
            ApplyDifficulty(character);

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
    /// Which of this level's spawn cells each roster index gets, honouring BattleRole where it is
    /// constrained and filling the rest by whoever is left. Cells are ranked by y - the same axis a
    /// level already authors its front-to-back layout on, e.g. Level3Menagerie's back cell sits one row
    /// higher than its other two - so a higher-y cell is "closer to the enemy" with no new authoring.
    ///
    /// Frontline-only members claim the highest-y cells first, Backline-only members claim the
    /// lowest-y cells first, and whatever remains in the middle goes to Both/None members in that
    /// order - "flexible filler", so a single-role member never loses its row to a flexible one. A
    /// roster index with no entry in the result ran out of cells to claim (more roster than authored
    /// cells, or more same-role members than that role had room for) and falls back to the caller's
    /// own last-cell behaviour.
    /// </summary>
    private static Dictionary<int, Vector2Int> AssignPartyCells(
        IReadOnlyList<PartyMember> roster, IReadOnlyList<Vector2Int> spawnCells)
    {
        List<Vector2Int> byDepth = new(spawnCells);
        byDepth.Sort((a, b) => b.y - a.y);

        int frontPtr = 0;
        int backPtr = byDepth.Count - 1;
        Dictionary<int, Vector2Int> assigned = new();

        for (int i = 0; i < roster.Count && frontPtr <= backPtr; i++)
        {
            PartyMember member = roster[i];
            if (member == null || member.prefab == null) { continue; }

            if (member.prefab.BattleRole == BattleRole.Frontline) { assigned[i] = byDepth[frontPtr++]; }
        }

        for (int i = 0; i < roster.Count && frontPtr <= backPtr; i++)
        {
            if (assigned.ContainsKey(i)) { continue; }

            PartyMember member = roster[i];
            if (member == null || member.prefab == null) { continue; }

            if (member.prefab.BattleRole == BattleRole.Backline) { assigned[i] = byDepth[backPtr--]; }
        }

        for (int i = 0; i < roster.Count && frontPtr <= backPtr; i++)
        {
            if (assigned.ContainsKey(i)) { continue; }

            assigned[i] = byDepth[frontPtr++];
        }

        return assigned;
    }

    /// <summary>
    /// Instantiates the run's party from RunManager and places it at this level's spawn cells,
    /// role-matched where BattleRole constrains a member and index-for-index otherwise - see
    /// AssignPartyCells. Skipped when either is missing - no run means this scene was opened stand-alone
    /// with no debug campaign to bootstrap from, and no level means there is nowhere authored to put
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
        Dictionary<int, Vector2Int> roleCells = AssignPartyCells(roster, spawnCells);

        for (int i = 0; i < roster.Count; i++)
        {
            PartyMember record = roster[i];

            if (record == null || record.prefab == null) { continue; }

            // A role-matched cell if AssignPartyCells found one; otherwise this member is past the
            // authored cells (or lost a same-role tie for them), and requests the last one same as
            // before role-awareness existed - NearestFreeSpawnTile then fans them out from there, the
            // same way an over-full enemy wave already spreads from its own requested cell.
            Vector2Int wanted = roleCells.TryGetValue(i, out Vector2Int roleCell)
                ? roleCell
                : spawnCells[^1];

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
    /// Fills rolledStartingEnemies/rolledWaves from CurrentLevel.EncounterBudget, once. A budget field
    /// left at 0 (the default) draws nothing, so a level that never touches EncounterBudget rolls two
    /// empty lists and behaves exactly as it did before this existed - see EncounterBudget's own doc
    /// comment. Uses UnityEngine.Random rather than a level-authored seed, the same choice
    /// LevelData.PickTileSet already made for its own per-playthrough variety.
    /// </summary>
    private void RollEncounter()
    {
        rolledStartingEnemies.Clear();
        rolledWaves.Clear();

        LevelData level = CurrentLevel;
        if (level == null) { return; }

        // Through EncounterRoller.RollFor, the same call the Encounter Simulator makes - so what the
        // simulator reports for a level is what a battle here actually rolls. Difficulty scaling
        // happens inside it; see RollFor for why that never touches the level asset.
        DifficultyTier tier = RunManager.Instance != null ? RunManager.Instance.CurrentTier : default;
        System.Random dice = new(UnityEngine.Random.Range(int.MinValue, int.MaxValue));

        RolledEncounter rolled =
            EncounterRoller.RollFor(level, enemyRegistry, tier, GridManager.Instance.BoardSize, dice);

        rolledStartingEnemies.AddRange(rolled.opening);
        rolledWaves.AddRange(rolled.waves);

        // Reported by the roller rather than logged by it, so the simulator can tally thousands of
        // these quietly. A real battle falling short is worth a line in the console, though: a designer
        // asking for more of a role than the level can supply should hear about it.
        if (!rolled.MetMinimums)
        {
            Debug.LogWarning($"{level.name}: could not meet the level's role minimums - short "
                             + $"{rolled.frontlineShortfall} frontline and {rolled.backlineShortfall} "
                             + "backline. Check the pool filter holds bodies of that role, and that "
                             + "anyPower can afford them. Tools/Levels/Encounter Simulator shows how often.");
        }
    }

    /// <summary>
    /// Instantiates this level's authored enemies plus RollEncounter's starting lineup, and adds them
    /// to the roster.
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

        foreach (EnemyPlacement placement in rolledStartingEnemies)
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
        Spawn(placement.prefab, placement.cell, placement.deckOverride);
    }

    /// <summary>
    /// Puts one enemy prefab on the board at (or near) `cell`, and returns whether it landed.
    ///
    /// The body SpawnPlacement used to hold, extracted so the debug panel can spawn without either
    /// duplicating these steps or reaching `enemyParent`, which is private and serialized. Every step
    /// here is load-bearing; see the comments on each.
    /// </summary>
    private bool Spawn(GameObject prefab, Vector2Int cell, List<CardData> deckOverride)
    {
        if (prefab == null) { return false; }

        // Resolved per spawn, inside the caller's loop rather than once for a whole wave: the sequence
        // below is fully synchronous, so the second enemy of a wave sees the first one's claim and
        // cannot be handed the same tile.
        GridTile tile = GridManager.Instance.NearestFreeSpawnTile(cell);

        // Before Instantiate: a board with no room left should cost no GameObject and no roster entry.
        if (tile == null)
        {
            Debug.LogWarning($"{prefab.name} not spawned: {cell} is taken and every border tile is occupied");
            return false;
        }

        GameObject enemyObject = Instantiate(prefab, enemyParent);
        Character enemy = enemyObject.GetComponent<Character>();
        enemy.name = $"{prefab.name} {tile.Coordinates.x},{tile.Coordinates.y}";

        if (deckOverride != null && deckOverride.Count > 0)
        {
            enemy.SetDeck(deckOverride);
        }

        // The resolved cell, never the requested one. PlaceOnGrid writes startCoordinates, and
        // Character.Start re-places itself from those at the end of the frame - handing it the
        // authored cell would quietly drag a relocated enemy back on top of whoever displaced it.
        enemy.PlaceOnGrid(tile.Coordinates);

        AddCharacter(enemy);

        return true;
    }

    /// <summary>
    /// Debug-only spawn: drops one enemy prefab on the board at the nearest free tile to `cell`.
    ///
    /// Goes through the same Spawn the level's own roster and its waves use, so a debug body is
    /// indistinguishable from an authored one - it takes a turn, it drops loot, it is on the roster.
    /// Keeps the prefab's authored deck; a deck override is a level-authoring concept with nothing to
    /// mean here.
    ///
    /// Returns false when the board had no room, so the caller can say so rather than leaving the
    /// player wondering where the enemy went.
    /// </summary>
    public bool DebugSpawn(GameObject prefab, Vector2Int cell) => Spawn(prefab, cell, null);

    /// <summary>
    /// Debug-only summon: puts a totem or ally prefab on `tile` as though the active character had
    /// played the card that summons it - but costing no energy and resolving no card.
    ///
    /// Goes through GridTile.SummonObject, which is the same primitive SummonAction uses, so the body
    /// is placed, registered with the roster and re-placed correctly on its own Start exactly like a
    /// summon from a real card. That method refuses an occupied tile by returning null, which is what
    /// this reports as false.
    ///
    /// Runs the OnSummoned pass too, because equipment that reacts to a summon (a totem-health relic,
    /// say) is part of what makes a summon behave normally - skipping it would make a debug totem
    /// quietly weaker than a played one, which is the opposite of a useful test. What it deliberately
    /// does NOT do is spend energy or apply a lifetime: the card owns those, and the point here is to
    /// place one without paying.
    /// </summary>
    public bool DebugSummon(GameObject prefab, GridTile tile)
    {
        if (prefab == null || tile == null) { return false; }

        Character summoned = tile.SummonObject(prefab);

        if (summoned == null) { return false; }

        Character source = ActiveCharacter;

        if (source != null)
        {
            foreach (Status status in source.ActiveStatuses()) { status.OnSummoned(source, summoned); }
        }

        return true;
    }

    /// <summary>
    /// The prefab a debug spawn is waiting to place, or null. Consumed by the next tile click.
    ///
    /// Held here rather than on the debug panel because OnTileClicked is the one door for tile clicks
    /// and it decides what a click meant - a panel that reached in to intercept clicks itself would be
    /// the second thing deciding that, which is the arrangement this codebase spent effort avoiding.
    /// </summary>
    private GameObject armedDebugSpawn;

    /// Whether the armed prefab should be placed as a SUMMON rather than as an enemy. The two
    /// take different paths: an enemy goes through Spawn (nearest-free-tile resolution, enemyParent,
    /// renaming), a totem through GridTile.SummonObject, which is the path a Summon card itself uses.
    private bool armedDebugIsSummon;

    public bool HasArmedDebugSpawn => armedDebugSpawn != null;

    /// <summary>
    /// Arms the next tile click to spawn `prefab`. The caller is expected to close whatever menu it was
    /// in and tell the player to click - see DebugPanel, which puts the prompt on AimHintLabel.
    /// </summary>
    public void ArmDebugSpawn(GameObject prefab)
    {
        armedDebugSpawn = prefab;
        armedDebugIsSummon = false;
    }

    /// <summary>
    /// Arms the next tile click to SUMMON `prefab` - a totem or an ally - rather than spawn it as an
    /// enemy.
    /// </summary>
    public void ArmDebugSummon(GameObject prefab)
    {
        armedDebugSpawn = prefab;
        armedDebugIsSummon = true;
    }

    public void ClearDebugSpawnArm()
    {
        armedDebugSpawn = null;
        armedDebugIsSummon = false;

        if (AimHintLabel.Instance != null) { AimHintLabel.Instance.Hide(); }
    }

    /// <summary>
    /// Spawns every LevelData wave plus every RollEncounter-generated wave keyed to the round
    /// TurnsElapsed just reached. Called from TurnStart before the per-character loop, so a wave enemy
    /// draws a hand and commits an intent the same round it lands - the same reasoning SpawnEnemies
    /// uses for the opening roster.
    /// </summary>
    private void SpawnDueWaves()
    {
        if (CurrentLevel != null)
        {
            foreach (EnemyWave wave in CurrentLevel.Waves)
            {
                if (wave.turn != TurnsElapsed || wave.enemies == null) { continue; }

                foreach (EnemyPlacement placement in wave.enemies)
                {
                    SpawnPlacement(placement);
                }
            }
        }

        foreach (EnemyWave wave in rolledWaves)
        {
            if (wave.turn != TurnsElapsed || wave.enemies == null) { continue; }

            foreach (EnemyPlacement placement in wave.enemies)
            {
                SpawnPlacement(placement);
            }
        }
    }

    /// <summary>
    /// The soonest upcoming wave across both LevelData's hand-authored waves and RollEncounter's
    /// generated ones - what NextWavePanel previews. LevelData.TryNextWave only sees the authored half;
    /// this is the merged version, possible only because RollEncounter resolves every generated wave up
    /// front at battle start rather than lazily when it fires, so there is always a real enemy list to
    /// preview instead of an empty placeholder.
    /// </summary>
    public bool TryNextWave(int turnsElapsed, out int turn, out List<EnemyPlacement> enemies)
    {
        turn = int.MaxValue;
        enemies = null;

        // The out variables' definite assignment has to stay tied to this one condition for the
        // compiler to trust them below - see NextWavePanel.Refresh's own comment on the same trap.
        if (CurrentLevel != null && CurrentLevel.TryNextWave(
                turnsElapsed, out int authoredTurn, out List<EnemyPlacement> authoredEnemies))
        {
            turn = authoredTurn;
            enemies = new List<EnemyPlacement>(authoredEnemies);
        }

        foreach (EnemyWave wave in rolledWaves)
        {
            if (wave.turn <= turnsElapsed || wave.enemies == null || wave.enemies.Count == 0) { continue; }
            if (wave.turn > turn) { continue; }

            if (wave.turn < turn)
            {
                turn = wave.turn;
                enemies = new List<EnemyPlacement>();
            }

            enemies.AddRange(wave.enemies);
        }

        return enemies != null;
    }

    private void Update()
    {
        ClearStuckHover();

        if (Keyboard.current == null) { return; }

        // Nothing below this fires while a modal owns the screen. Enter would otherwise end the
        // turn and Space draw a card straight through an open reward panel, notification or pause
        // menu - the pause menu is simply the first one obvious enough to make that unacceptable.
        if (InputLocked) { return; }

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
    /// Keeps every enemy's CommittedPlan - and so the icon row and damage numbers over its head - equal
    /// to what would actually happen if EnemyResolve ran right now, action point by action point. Runs
    /// once per frame rather than from inside each handler, so one card that queues three actions
    /// produces one recompute instead of three.
    ///
    /// DecidePlan is lock-aware per step (see its own doc comment), so this re-aims each enemy's
    /// already-committed cards at the moved board rather than picking new ones - a hero stepping closer
    /// changes where an archer's shot lands, never what card it is shooting with.
    ///
    /// Only while the player is rearranging the board. EnemyResolve clears each enemy's row the
    /// instant it finishes acting, and a pass landing after that would hand the row straight back
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
            List<Intent> next = DecidePlan(enemy, board, enemy.ActionPoints);

            // Every field of every step, not just kind - a re-aim onto a different tile or victim
            // (same locked card) still has to repaint that icon's position and damage number.
            // PlansMatch skips only a truly identical result, so re-assigning it never rolls an icon
            // over to the sprite it is already showing.
            if (PlansMatch(next, enemy.CommittedPlan)) { continue; }

            enemy.SetCommittedPlan(next);
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
            // A wave circle's hover pulse is the same story: WaveCircle.OnPointerExit never fires if a
            // reward panel opens over the HUD while the cursor rests on one.
            GridManager.Instance.ClearSpawnWarning();
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

        ApplyDifficulty(character);

        characters.Add(character);
        Subscribe(character);

        // A body that just joined has a hand and an energy pool of its own, so what the party can
        // still do has changed - a summoned ally is one more set of cards to light up.
        RaisePlayabilityChanged();
    }

    /// <summary>
    /// Raises a hostile body's health to what the run's difficulty tier asks for, as it joins.
    ///
    /// Called from AddCharacter rather than Spawn because Spawn is not the whole story: an enemy
    /// Summoner's skeletons arrive through GridTile.SummonObject, which never touches Spawn and calls
    /// AddCharacter directly. AddCharacter is the one door *every* body enters by - scene-placed
    /// characters, SpawnParty, Spawn and summons alike - and it already refuses a duplicate before
    /// reaching here, so nothing can be scaled twice.
    ///
    /// Gated on IsHostileToParty, not !IsPlayerControlled: the latter means "AI-resolved" and is true
    /// of a hero's own summoned ally, which must not pick up the enemy bonus.
    ///
    /// AddMaxHealth rather than writing a new maximum, because it already raises the ceiling and
    /// current health together - a body must not join the battle pre-damaged.
    /// </summary>
    private static void ApplyDifficulty(Character character)
    {
        if (!character.IsHostileToParty) { return; }

        RunManager run = RunManager.Instance;

        if (run == null) { return; }

        int bonus = run.CurrentTier.ExtraHealthFor(character.MaxHealth);

        if (bonus > 0) { character.AddMaxHealth(bonus); }
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
    ///
    /// Enforces the same single-occupant rule Character.Equip does for every slot but Ring: any record
    /// entry already in `item`'s slot is dropped before the new one is added. That has to happen here
    /// rather than only at the live Equip call, because the level-clear path above never touches a live
    /// Character at all - this is the only write the record ever gets for that reward.
    /// </summary>
    public void RecordRunEquipment(Character character, EquipmentData item)
    {
        if (character == null || item == null) { return; }

        if (!partyRecords.TryGetValue(character, out PartyMember record)) { return; }

        if (!EquipmentSlots.IsUnlimited(item.slot))
        {
            record.equipment.RemoveAll(existing => existing != null && existing.slot == item.slot);
        }

        record.equipment.Add(item);
    }

    /// <summary>
    /// Takes one copy of a card back out of the run record - RecordRunCard's inverse.
    ///
    /// Exists for the debug panel, which is the only thing that ever un-grants: a run has no way to
    /// lose a card it picked up. Without it a debug removal looks like it worked and is silently undone
    /// at the next level, because SpawnParty rebuilds the deck from this record.
    ///
    /// Removes one copy rather than every match, matching how the deck holds duplicates.
    /// </summary>
    public void RemoveRunCard(Character character, CardData card)
    {
        if (character == null || card == null) { return; }

        if (partyRecords.TryGetValue(character, out PartyMember record)) { record.deck.Remove(card); }
    }

    /// <summary>
    /// Takes one copy of an item back out of the run record - RecordRunEquipment's inverse, and the
    /// other half of what a debug unequip needs. Character.Unequip handles the live instance; this is
    /// what stops SpawnParty handing the item straight back on the next level.
    /// </summary>
    public void RemoveRunEquipment(Character character, EquipmentData item)
    {
        if (character == null || item == null) { return; }

        if (partyRecords.TryGetValue(character, out PartyMember record)) { record.equipment.Remove(item); }
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

            // The tutorial explains what the discard meant and what the enemy is about to do, and both
            // beats have to land in this gap - after the hand has cleared, before anything acts on it.
            // Same shape as the two waits above: the round must not roll over on top of something still
            // finishing.
            yield return new WaitUntil(() =>
                TutorialDirector.Instance == null || !TutorialDirector.Instance.HoldingRound);

            yield return StartCoroutine(EnemyResolve());

            // Tiles belong to nobody's "own phase" - ticked once per round, here at the end of the
            // whole round rather than split by side like TickStatuses. After EnemyResolve on purpose:
            // a wall laid during PlayerActing has to still be standing while the enemies move, so
            // Wall of Force blocks their pathing for the turn it was paid for and Wall of Flames bites
            // whoever is left standing in it once they have finished moving. See
            // GridManager.TickTileEffects.
            if (GridManager.Instance != null) { GridManager.Instance.TickTileEffects(); }

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
                    "Tutorial has been completed! You will now continue onto a real run where your Heroes can die.");
            }
            else if (finalLevel)
            {
                // Awarded here rather than in Finish, which loads the Main Menu synchronously and so
                // has nowhere to show what was earned. handingOver is checked above this, which is
                // what stops a cleared tutorial paying out.
                string earned = run != null ? run.AwardRunComplete() : null;

                NotificationManager.Instance.Show("Victory",
                    earned != null ? $"The party made it out!\n\n{earned}" : "The party made it out!");
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
            // with an empty hand has nothing to choose between and can only Wait. BonusHandSize is a
            // ring's "+1 card drawn each turn" - read here rather than baked into HandSize itself,
            // since HandSize is BattleManager/LevelData's authored number and this is a per-character
            // add-on.
            character.DrawCards(HandSize + character.BonusHandSize - character.Hand.Count);

            character.TickCardTimers();

            character.ResetEnergy();

            // Shield wipes itself in here. BattleManager does not know that - it just says the turn
            // started and lets each status decide what that means. See Character.OnTurnStart.
            // Dodge pays its deferred charge here too - see DodgeStatus.
            character.OnTurnStart();

            // A round-scoped lock, not a status - nothing pays a charge to clear it. EnemyResolve
            // already clears its own as each enemy finishes, so this only matters for one whose
            // action points ran out - died, Frozen - while still holding one.
            character.LockedAim = Intent.Wait();
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
            // Once per turn, not once per Decide - Decide runs again every frame the board changes
            // (LateUpdate below) and again per action point in EnemyResolve, and a totem hunt rerolled
            // that often would flicker the intent icon and make TryFindMove's tile scan incoherent.
            // Same reasoning TargetSelector.TryPick documents for resolving Random once per pick.
            enemy.RollTotemHunt();

            // Last round's lock has to be gone before DecidePlan runs, or it (via the lock-aware
            // re-aim inside it) would just re-aim the cards this enemy already discarded playing last
            // turn. No lock is held here, so every step is a fresh EnemyBrain.Decide.
            enemy.ClearIntentLock();

            List<Intent> opening = DecidePlan(enemy, board, enemy.ActionPoints);

            // The one and only place a lock is set - see Character.LockedPlan. Every recompute for the
            // rest of this turn re-aims these exact cards rather than picking new ones.
            enemy.LockPlan(opening);

            // SetCommittedPlan raises IntentChanged, which is what puts the icon row up - see
            // CharacterOverheadViewer.
            enemy.SetCommittedPlan(opening);

            if (opening.Count > 0)
            {
                Debug.Log($"{enemy.name} intends: {string.Join(" -> ", opening)}");
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

    /// <summary>
    /// Knocks out the Interruptible cards this enemy was committed to, because a Freeze just took its
    /// whole turn. Called from the frozen branch below, before the lock is cleared - the plan is the
    /// whole point, since it names what this enemy was *winding up* rather than everything it holds.
    ///
    /// Only cards carrying the Interruptible keyword are affected (Card.Interrupt no-ops otherwise),
    /// so freezing an enemy mid-poke still just costs it the swing. A card appearing in two steps of
    /// the same plan is interrupted once - Interrupt is idempotent within a turn anyway, since it
    /// assigns the magnitude rather than adding to it.
    ///
    /// Falls back to the committed plan when the lock is already empty: the lock is what BattleManager
    /// promises, but the icon row is what the player read before spending the Freeze, and those two
    /// disagreeing should not quietly cost them the interrupt they aimed for.
    /// </summary>
    private void InterruptCommittedCards(Character enemy)
    {
        if (enemy == null) { return; }

        IReadOnlyList<Intent> plan = enemy.LockedPlan.Count > 0 ? enemy.LockedPlan : enemy.CommittedPlan;

        foreach (Intent step in plan)
        {
            if (step.card == null) { continue; }

            if (step.card.Interrupt())
            {
                Debug.Log($"{enemy.name}'s {step.card.Data.cardName} was interrupted for "
                          + $"{step.card.InterruptedRemaining} turn(s)");
            }
        }
    }

    private IEnumerator EnemyResolve()
    {
        Phase = BattlePhase.EnemyResolve;

        enemyResolveOrder = new List<Character>(LivingEnemies());
        enemyResolveOrder.Sort(CompareByGridOrder);

        for (enemyResolveIndex = 0; enemyResolveIndex < enemyResolveOrder.Count; enemyResolveIndex++)
        {
            Character enemy = enemyResolveOrder[enemyResolveIndex];

            if (enemy == null || enemy.IsDead) { continue; }

            // Frozen burns the whole turn, not one action - there is no partial thaw. Clear the
            // intent too, or a frozen enemy would wear a ghost icon row into the next turn.
            if (!enemy.CanAct)
            {
                Debug.Log($"{enemy.name} is frozen and loses its turn");
                InterruptCommittedCards(enemy);
                enemy.ClearCommittedPlan();
                enemy.LockedAim = Intent.Wait();
                enemy.ClearIntentLock();
                continue;
            }

            for (int ap = 0; ap < enemy.ActionPoints && !enemy.IsDead; ap++)
            {
                // LockAimsOn may have frozen this enemy onto an Attack it was aiming at a dodger who
                // has since moved on - see DodgeStatus. Consuming that lock instead of deciding fresh
                // is the one exception to "the front of CommittedPlan is only ever what the icon row
                // shows"; every other action point is still decided outright against the live board,
                // same as always.
                bool committed = !enemy.LockedAim.IsWait;
                Intent step = committed ? enemy.LockedAim : Decide(enemy, GridManager.Instance.Read());

                enemy.LockedAim = Intent.Wait();

                if (step.IsWait) { break; }

                yield return StartCoroutine(Execute(enemy, step, committed));

                // Pops the step that just resolved off the front of both the lock and the row, so
                // the overhead icon row counts down one icon per action point instead of every icon
                // vanishing at once when this enemy is entirely done - see
                // Character.ConsumeLockedStep/ConsumeCommittedStep.
                enemy.ConsumeLockedStep();
                enemy.ConsumeCommittedStep();

                // AddAction resolves the first action synchronously, so "queued" is not "finished".
                // Also waits on LootManager: an enemy can shove a hero onto a loot tile, and the reward
                // panel that opens for it must resolve before the next action point spends.
                yield return new WaitUntil(() => ActionManager.Instance.IsIdle && LootIdle());
            }

            // Clears whatever is left of this enemy's row the moment it is done, rather than every
            // icon vanishing at once when EnemyResolve began - so mid-resolve you can see who is
            // still owed an action.
            enemy.ClearCommittedPlan();
            enemy.LockedAim = Intent.Wait();
            enemy.ClearIntentLock();
        }

        enemyResolveOrder = null;
        enemyResolveIndex = -1;

        // Actions resolve through the queue, and AddAction runs the first one synchronously, so
        // "queued" is not "finished". Without this the turn would roll over mid-animation.
        yield return new WaitUntil(() => ActionManager.Instance.IsIdle && LootIdle());

        TickStatuses(playerControlled: false);
    }

    /// <summary>
    /// Freezes any not-yet-finished enemy that is currently aiming an Attack at `dodger` onto that
    /// exact Intent, so it keeps swinging at `vacated` instead of re-Deciding once `dodger` has moved
    /// away. Called from DodgeStatus.OnTakeDamage, before the sidestep is queued - Decide has to see
    /// `dodger` still standing on `vacated` for its answer to be the tile being frozen.
    ///
    /// "Not yet finished" is enemyResolveOrder from enemyResolveIndex onward when EnemyResolve is
    /// running (the enemy currently acting included, for its next action point) - or every living
    /// enemy when it is not running yet, since nobody has acted this round at all. Locking an enemy
    /// whose own remaining action points run out before it is ever consumed is harmless: EnemyResolve
    /// clears LockedAim the moment that enemy finishes its turn.
    /// </summary>
    public void LockAimsOn(Character dodger, GridTile vacated)
    {
        if (dodger == null || GridManager.Instance == null) { return; }

        Board board = GridManager.Instance.Read();
        int start = enemyResolveOrder != null ? enemyResolveIndex : 0;
        IReadOnlyList<Character> order = enemyResolveOrder ?? new List<Character>(LivingEnemies());

        for (int i = start; i < order.Count; i++)
        {
            Character enemy = order[i];

            if (enemy == null || enemy.IsDead || !enemy.CanAct) { continue; }

            Intent decided = Decide(enemy, board);

            if (decided.kind == IntentKind.Attack && decided.victim == dodger)
            {
                enemy.LockedAim = decided;
            }
        }
    }

    /// True when no reward panel is up or queued. A LootManager-less scene (a test harness, or one
    /// that simply has no loot yet) must not block the turn loop forever, hence the null check.
    private static bool LootIdle() => LootManager.Instance == null || LootManager.Instance.IsIdle;

    /// <summary>
    /// What this character would do right now - Wait if it has no brain, which is every player.
    ///
    /// Lock-aware: if this character is still holding the card it committed to this turn (see
    /// Character.LockedCard, step 0 of LockedPlan), this re-aims that same card at the live board
    /// instead of asking Decide fresh, so every caller - LockAimsOn, and EnemyResolve's own re-ask per
    /// action point once ConsumeLockedStep has advanced the lock to that step - gets the same "same
    /// card, live aim" answer without needing to know the lock exists. Only falls through to a fresh
    /// Decide when the locked card can no longer be aimed at anything at all. See DecidePlan for the
    /// multi-step version this is the single-step building block of - TurnStart and LateUpdate call
    /// that instead, so a card dropped here for being briefly unplayable is not what silently
    /// re-locks; only DecidePlan's own result ever is.
    /// </summary>
    private static Intent Decide(Character character, Board board)
    {
        EnemyBrain brain = EnemyBrain.For(character.Brain);

        if (brain == null || character.Tile == null) { return Intent.Wait(); }

        if (character.LockedCard != null && character.Holds(character.LockedCard))
        {
            Intent held = brain.Reaim(character, character.LockedCard, character.LockedKind, board);

            if (!held.IsWait) { return held; }
        }

        return brain.Decide(character, board);
    }

    /// <summary>
    /// What this character would do over its next `steps` action points, in order - the multi-icon
    /// forecast behind the overhead intent row. Each step is decided the same lock-aware way Decide
    /// is (re-aiming a held lock, falling through to a fresh brain.Decide only once that lock has
    /// nothing left to offer), so a plan already locked this turn - see Character.LockedPlan - comes
    /// back as the same cards re-aimed live rather than a fresh guess every recompute.
    ///
    /// Every step's brain.Decide call excludes the cards earlier steps of this same forecast already
    /// chose (`spent`), so a two-card hand does not show the same card twice - see
    /// EnemyBrain.IsAvailable. A Move step temporarily relocates `character` onto its destination
    /// tile for the rest of the forecast (restored in the finally below) so a closing melee enemy
    /// reads Move-then-Attack rather than Move-then-Move; Character.MoveTo is safe to call and undo
    /// like this mid-decision because it only swaps GridTile occupancy references and bumps
    /// GridManager's route-cache version - it raises no events and never touches transform.position.
    /// Stops early on a Wait, so a plan shorter than `steps` is exactly as long as this enemy actually
    /// has something to do.
    /// </summary>
    private static List<Intent> DecidePlan(Character character, Board board, int steps)
    {
        List<Intent> plan = new(steps);

        EnemyBrain brain = EnemyBrain.For(character.Brain);

        if (brain == null || character.Tile == null || steps <= 0) { return plan; }

        List<Card> spent = new();
        GridTile origin = character.Tile;
        IReadOnlyList<Intent> lockedPlan = character.LockedPlan;

        try
        {
            for (int i = 0; i < steps; i++)
            {
                Intent step = default;
                bool decided = false;

                if (i < lockedPlan.Count && character.Holds(lockedPlan[i].card))
                {
                    Intent held = brain.Reaim(character, lockedPlan[i].card, lockedPlan[i].kind, board);

                    if (!held.IsWait) { step = held; decided = true; }
                }

                if (!decided) { step = brain.Decide(character, board, spent); }

                if (step.IsWait) { break; }

                plan.Add(step);
                spent.Add(step.card);

                if (step.kind == IntentKind.Move && i < steps - 1)
                {
                    GridTile destination = GridManager.Instance.GetTile(step.target);

                    if (destination == null) { break; }

                    character.MoveTo(destination);
                    board = GridManager.Instance.Read();
                }
            }
        }
        finally
        {
            if (character.Tile != origin) { character.MoveTo(origin); }
        }

        return plan;
    }

    /// True when every step of `a` matches the same step of `b` - the plan-level version of
    /// Intent.Matches, so LateUpdate's recompute only repaints a row when something about it actually
    /// changed rather than on every dirty frame.
    private static bool PlansMatch(IReadOnlyList<Intent> a, IReadOnlyList<Intent> b)
    {
        if (a.Count != b.Count) { return false; }

        for (int i = 0; i < a.Count; i++)
        {
            if (!a[i].Matches(b[i])) { return false; }
        }

        return true;
    }

    /// <summary>
    /// Plays the decided card, if it is still legal.
    ///
    /// The fizzle is Card.Refusal saying no - the same call the player's click is gated on. Decide is
    /// asked fresh against the live board immediately before this runs, so a fizzle here means the
    /// board changed in the single frame between deciding and acting, not that a stale plan met a
    /// rearranged board.
    ///
    /// `committed` means `step` came from Character.LockedAim rather than a fresh Decide - a Dodge
    /// froze this onto a tile its target has since left. That swing must still go off and whiff, not
    /// fizzle as "there is nobody there", so this asks Card.CommittedRefusal instead of Card.Refusal -
    /// range and cooldown only, no occupant check. See DodgeStatus and BattleManager.LockAimsOn.
    ///
    /// Resolution goes through ResolveEffects exactly as a played card does, so enemy attacks pick up
    /// Strength, Double Attack and the target's armor for free. Nothing in the card pipeline needed
    /// to learn that enemies exist.
    /// </summary>
    private IEnumerator Execute(Character enemy, Intent step, bool committed = false)
    {
        GridTile tile = GridManager.Instance.GetTile(step.target);

        string refusal = tile == null
            ? "that tile is gone"
            : committed ? step.card.CommittedRefusal(enemy, tile) : step.card.Refusal(enemy, tile);

        if (refusal != null)
        {
            //TODO: a visible fizzle. This currently only reads in the console, so a plan you broke
            //looks like an enemy that did nothing rather than like your block working.
            Debug.Log($"{enemy.name} tries {step.card.cardName} at {step.target} - {refusal}");
            yield break;
        }

        Debug.Log($"{enemy.name} plays {step.card.cardName} at {step.target}");

        // DiscardPlayed, matching CardPlayManager: an enemy resolving its intent is playing a card, so
        // a Rebound one should come back to its hand exactly as it would for a hero.
        enemy.DiscardPlayed(step.card);

        // Below the refusal above, so a fizzled plan never looks like a landed hit, and above
        // ResolveEffects so occupancy is still the pre-damage board - a body this attack kills should
        // light up rather than wink out. Fired on the same frame as the damage rather than leading it,
        // so the enemy turn keeps its pacing. DamageArea answers empty for a Move or a Summon, which is
        // why there is no IntentKind check here.
        foreach (GridTile hit in step.card.DamageArea(enemy, tile)) { hit.FlashThreat(); }

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

    /// Bottom-left to bottom-right, then up a row: the order EnemyResolve acts in, so it reads off
    /// board position rather than spawn order. y ascending first (bottom row before the ones above
    /// it), then x ascending within a row (left before right). A tileless enemy sorts last rather than
    /// throwing, though EnemyResolve never actually holds one.
    private static int CompareByGridOrder(Character a, Character b)
    {
        Vector2Int ca = a.Tile != null ? a.Tile.Coordinates : new Vector2Int(int.MaxValue, int.MaxValue);
        Vector2Int cb = b.Tile != null ? b.Tile.Coordinates : new Vector2Int(int.MaxValue, int.MaxValue);

        return ca.y != cb.y ? ca.y.CompareTo(cb.y) : ca.x.CompareTo(cb.x);
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

        // Whatever froze time - a notification, the pause menu - is dropped here, because none of
        // it survives the load while TimeFreeze's count does. A frozen Main Menu is a hard lockup
        // with no way out.
        TimeFreeze.ReleaseAll();

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
