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
/// a refresh point, and enemies need a moment to act on intents the player has spent the turn
/// disrupting.
///
///   TurnStart      refresh energy + armor, draw back up, enemies commit an intent
///   PlayerActing   free-form, immediate resolution. Ends on End Turn and nothing else - never on
///                  its own - then player-controlled statuses tick.
///   EnemyResolve   the committed intent executes right or wrong; everything after it is re-decided.
///                  Enemy statuses tick once every enemy has acted.
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
    [SerializeField] private TextMeshProUGUI manaCounter;

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

    /// Which run record each spawned party member came from, so the state it finishes the level with
    /// can be written back to the run. Only party members are in here - an enemy has nothing that
    /// outlives the battle.
    private readonly Dictionary<Character, PartyMember> partyRecords = new();

    /// <summary>
    /// True while a reward panel is up. Not Time.timeScale - that would stall the WaitForSeconds inside
    /// EnemyResolve (an enemy shoving a hero onto loot takes that exact path) and would not actually
    /// block a click, since OnMouseDown is a physics raycast no uGUI panel intercepts. This is the
    /// real gate; OnTileClicked and CardPlayManager.OnCardClicked both check it.
    /// </summary>
    public bool InputLocked { get; private set; }

    public void SetInputLocked(bool value) => InputLocked = value;

    /// Hook this to the End Turn button. The only way out of PlayerActing, and it always works while
    /// the phase is live - energy left unspent is simply banked. The button dims itself while there
    /// is still something playable (see EndTurnButton) but never refuses the click; holding energy
    /// back is a decision the player is allowed to make.
    public void RequestEndTurn()
    {
        if (Phase == BattlePhase.PlayerActing && !InputLocked) { endTurnRequested = true; }
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
        ChangeActiveMana(ActiveCharacter.Energy);
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

        CharacterJoined?.Invoke(character);
    }

    private void Unsubscribe(Character character)
    {
        character.Died -= HandleCharacterDied;
        character.StatsChanged -= HandlePlayabilityMayHaveChanged;
        character.CardDrawn -= HandlePlayabilityMayHaveChanged;
        character.CardDiscarded -= HandlePlayabilityMayHaveChanged;

        CharacterLeft?.Invoke(character);
    }

    // Two shapes because Character's events carry different payloads; both mean the same thing here,
    // and both are discarded - see the doc on PlayabilityChanged for why it carries nothing.
    private void HandlePlayabilityMayHaveChanged(Character _) => RaisePlayabilityChanged();

    private void HandlePlayabilityMayHaveChanged(Character _, Card __) => RaisePlayabilityChanged();

    private void Start()
    {
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
        NotificationManager.Instance.Show("How to Play",
            "You are a group of adventures waitng for your friend to open the Door. Survive as long until the turn counter reaches 0 to make it out alive! Select the Knight or Mage using the Left Mouse Button and then select cards to play. Each Character has their own deck and amount of energy each turn. Good Luck!");



        StartCoroutine(RunBattle());
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
    /// </summary>
    private void SpawnParty()
    {
        LevelData level = CurrentLevel;

        if (RunManager.Instance == null || level == null) { return; }

        IReadOnlyList<PartyMember> roster = RunManager.Instance.Party;
        IReadOnlyList<Vector2Int> spawnCells = level.PartySpawnCells;

        for (int i = 0; i < roster.Count; i++)
        {
            PartyMember record = roster[i];

            if (record == null || record.prefab == null) { continue; }

            if (i >= spawnCells.Count)
            {
                Debug.LogWarning($"{record.prefab.name} has no spawn cell in {level.name} - not placed");
                continue;
            }

            Character member = Instantiate(record.prefab);
            member.name = record.prefab.name;

            // Both after Instantiate, never before: Awake has already run BuildDeck and set Health to
            // full by the time anything can reach a fresh instance.
            member.SetDeck(record.deck);
            member.SetHealth(record.currentHealth);

            member.PlaceOnGrid(spawnCells[i]);

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
    ///
    /// Each one is placed explicitly rather than left to its own Start - a character instantiated
    /// during this Start would not run its Start until the end of the frame, and the first TurnStart
    /// happens before that. It would be asked for an intent with no tile, and answer Wait.
    /// </summary>
    private void SpawnEnemies()
    {
        if (CurrentLevel == null) { return; }

        foreach (EnemyPlacement placement in CurrentLevel.Enemies)
        {
            if (placement.prefab == null) { continue; }

            GameObject enemyObject = Instantiate(placement.prefab, enemyParent);
            Character enemy = enemyObject.GetComponent<Character>();
            enemy.name = $"{placement.prefab.name} {placement.cell.x},{placement.cell.y}";

            if (placement.deckOverride != null && placement.deckOverride.Count > 0)
            {
                enemy.SetDeck(placement.deckOverride);
            }

            enemy.PlaceOnGrid(placement.cell);

            AddCharacter(enemy);
        }
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
                if (placement.prefab == null) { continue; }

                GameObject enemyObject = Instantiate(placement.prefab, enemyParent);
                Character enemy = enemyObject.GetComponent<Character>();
                enemy.name = $"{placement.prefab.name} {placement.cell.x},{placement.cell.y}";

                if (placement.deckOverride != null && placement.deckOverride.Count > 0)
                {
                    enemy.SetDeck(placement.deckOverride);
                }

                enemy.PlaceOnGrid(placement.cell);

                AddCharacter(enemy);
            }
        }
    }

    private void Update()
    {
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

            TickStatuses(playerControlled: true);

            yield return StartCoroutine(EnemyResolve());

            int turnsBefore = TurnsRemaining;

            ReduceTurns();

            if (AllHeroesDead())
            {
                NotificationManager.Instance.Show("Defeat", "All Heroes were Slain.");
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
                NotificationManager.Instance.Show("Victory", "The party made it out!");
                yield return WaitForAcknowledgement();
                Finish(victory: true);
                yield break;
            }
        }
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

        // ResetEnergy refills the pool but nothing tells the counter, which otherwise keeps showing
        // last turn's spent value until the next card is played. Refreshed here rather than from
        // inside ResetEnergy so Character stays unaware of any UI.
        if (ActiveCharacter != null) { ChangeActiveMana(ActiveCharacter.Energy); }

        // Every OnTurnStart hook has run by here - Shield has wiped itself, and none of it went
        // through an action. Anything showing a character's stats needs telling.
        TurnAdvanced?.Invoke();

        // TickCardTimers above is the reason this is not left to ResetEnergy's StatsChanged: a
        // card coming off cooldown (or waking from Dormant) changes nothing about the character
        // carrying it, so the only announcement it would otherwise get is none.
        RaisePlayabilityChanged();

        // Enemies commit now, at the top of your turn, not at the end of it. That ordering is the
        // whole design: they announce one action, you spend the turn making it wrong, and it fires
        // anyway. Deciding at execution time instead would quietly undo every block you set up.
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
                // Step 0 keeps the *category* promised at TurnStart and re-picks a card and a tile
                // inside it against the live board - see EnemyBrain.Resolve. What was ever stale was
                // the card and the tile, not the threat. Every step after it is decided outright,
                // category included.
                Intent step = ap == 0
                    ? Resolve(enemy, GridManager.Instance.Read(), enemy.CommittedIntent.kind)
                    : Decide(enemy, GridManager.Instance.Read());

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

    /// Asks this character's brain for one action inside the category it already committed to. Wait
    /// if it has no brain, exactly like Decide.
    private static Intent Resolve(Character character, Board board, IntentKind committed)
    {
        EnemyBrain brain = EnemyBrain.For(character.Brain);

        if (brain == null || character.Tile == null) { return Intent.Wait(); }

        return brain.Resolve(character, board, committed);
    }

    /// <summary>
    /// Plays the committed card, if it is still legal.
    ///
    /// The fizzle is Card.Refusal saying no - the same call the player's click is gated on. An intent
    /// is a promise made a whole turn ago against a board you have spent the turn rearranging, so a
    /// goblin that meant to walk somewhere you are now standing, or swing at somebody who has moved
    /// or died, simply finds its card illegal and burns the action point.
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
    /// both together at the shared TurnStart, so turnsRemaining always means "survives this many of my
    /// own turns" no matter which side applied the status. Ticking everyone at TurnStart instead would
    /// make that number direction-dependent: a status put on an enemy mid-PlayerActing would coast
    /// through that same cycle's EnemyResolve untouched, while one put on a player during EnemyResolve
    /// would get ticked down at the very next TurnStart before that player ever got to act on it -
    /// the same turnsRemaining would then mean two different things depending on who cast it.
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
    /// Ends the battle and hands back to the run.
    ///
    /// Victory carries the party forward: what everyone finished the level with is written back to
    /// their run records before the level index moves, because the live characters are about to be
    /// destroyed with the scene and the records are all that survive. Defeat ends the run outright -
    /// there is no continue.
    ///
    /// The last level cleared lands in the same place as a defeat, the Main Menu, but by way of
    /// EndRun rather than in spite of it. Skipping that would leave a finished run sitting in the
    /// RunManager for the next Play button to resume.
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

        Debug.Log("battle over: victory - run complete");

        if (run != null) { run.EndRun(); }

        SceneManager.LoadScene("MainMenu");
    }

    internal void ChangeActiveMana(int energy)
    {
        manaCounter.text = energy.ToString();
    }
}
