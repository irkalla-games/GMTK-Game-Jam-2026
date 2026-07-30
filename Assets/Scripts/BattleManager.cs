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
///   TurnStart      refresh energy + armor, tick statuses, draw back up, enemies commit an intent
///   PlayerActing   free-form, immediate resolution. Ends on End Turn or when nobody can act.
///   EnemyResolve   the committed intent executes right or wrong; everything after it is re-decided
///   -> TurnStart
///
/// Also owns the roster, who is active, and what a tile click means - the coherent half of what used
/// to be GameManager. The display half went to ActiveHandViewer.
/// </summary>
public class BattleManager : Singleton<BattleManager>
{
    [SerializeField] private TextMeshProUGUI turnCounter;
    [SerializeField] private TextMeshProUGUI manaCounter;

    /// <summary>
    /// One enemy to place when the battle starts.
    ///
    /// Stats and deck live on the prefab's own Character rather than being repeated here - there is
    /// one obvious place to tune a goblin, and it is the goblin. The deck override exists only so the
    /// same prefab can turn up twice with different cards without needing a second prefab.
    /// </summary>
    [System.Serializable]
    public struct EnemyPlacement
    {
        [Tooltip("Prefab with a Character on it. Health, brain, damage and reach all come from there.")]
        public Character prefab;

        [Tooltip("Grid cell it starts on.")]
        public Vector2Int cell;

        [Tooltip("Leave empty to use the prefab's own deck.")]
        public List<CardData> deckOverride;
    }

    [Tooltip("Characters already placed in the scene - the party.")]
    [SerializeField] private List<Character> characters = new();

    [Tooltip("Enemies spawned when the battle starts, and added to the roster above.")]
    [SerializeField] private List<EnemyPlacement> enemies = new();

    [Tooltip("Where spawned enemies are parented. Optional - tidiness only.")]
    [SerializeField] private Transform enemyParent;

    [Tooltip("Turns the player has to survive. Reaching 0 is the win.")]
    [SerializeField] private int turnsToSurvive = 10;

    [Tooltip("Hand is topped back up to this at the start of each turn - unplayed cards carry over.")]
    [SerializeField] private int handSize = 5;

    [Tooltip("Pause between an enemy's individual movement steps, so a walk reads as a walk.")]
    [SerializeField] private float stepDuration = 0.16f;

    [field: SerializeField, ReadOnlyField]
    public int TurnsRemaining { get; private set; }

    public BattlePhase Phase { get; private set; }

    /// Whose hand is on screen. Cards are played by this character and spend its energy.
    public Character ActiveCharacter { get; private set; }

    public IReadOnlyList<Character> Characters => characters;

    private bool endTurnRequested;

    /// Hook this to the End Turn button. Ends the turn early, with energy still banked.
    public void RequestEndTurn()
    {
        if (Phase == BattlePhase.PlayerActing) { endTurnRequested = true; }
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
            string who = occupant != null ? $"{occupant.name} is not player controlled" : "nobody here";
            Debug.Log($"tile clicked: {tile.Coordinates} - no card selected, {who}");
            return;
        }

        Debug.Log($"tile clicked: {tile.Coordinates} - activating {occupant.name}");
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
    }

    private void Start()
    {
        // Enemies first: they join the roster, and everything below walks it.
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
    /// Instantiates the authored enemies and adds them to the roster.
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
        foreach (EnemyPlacement placement in enemies)
        {
            if (placement.prefab == null) { continue; }

            Character enemy = Instantiate(placement.prefab, enemyParent);
            enemy.name = $"{placement.prefab.name} {placement.cell.x},{placement.cell.y}";

            if (placement.deckOverride != null && placement.deckOverride.Count > 0)
            {
                enemy.SetDeck(placement.deckOverride);
            }

            enemy.PlaceOnGrid(placement.cell);

            characters.Add(enemy);
        }
    }

    private void Update()
    {
        if (Keyboard.current == null) { return; }

        // Keyboard fallback so the loop is playable before an End Turn button exists in the scene.
        // Without it RequestEndTurn has no caller at all, and a turn can only end by running the
        // whole party out of playable cards.
        if (Keyboard.current.enterKey.wasPressedThisFrame) { RequestEndTurn(); }

        // Debug: draw a card for whoever is active.
        if (Keyboard.current.spaceKey.wasPressedThisFrame && ActiveCharacter != null)
        {
            ActiveCharacter.DrawCard();
        }
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
        TurnsRemaining = turnsToSurvive;
        turnCounter.text = turnsToSurvive.ToString();

        while (true)
        {
            yield return StartCoroutine(TurnStart());

            Phase = BattlePhase.PlayerActing;
            endTurnRequested = false;

            while (!endTurnRequested && CanAnyoneAct()) { yield return null; }

            yield return StartCoroutine(EnemyResolve());

            ReduceTurns();

            if (AllHeroesDead())
            {
                NotificationManager.Instance.Show("Defeat", "All Heroes were Slain.");
                yield return new WaitForSeconds(.15f);
                Finish("defeat - every hero is down");
                yield break;
            }

            if (TurnsRemaining <= 0)
            {
                NotificationManager.Instance.Show("Victory", "The party made it out!");
                yield return new WaitForSeconds(.15f);
                Finish("victory - survived to the end of the clock");
                yield break;
            }
        }
    }

    private void ReduceTurns()
    {
        TurnsRemaining--;
        turnCounter.text = TurnsRemaining.ToString();
    }

    private IEnumerator TurnStart()
    {
        Phase = BattlePhase.TurnStart;

        foreach (Character character in characters)
        {
            if (character == null || character.IsDead) { continue; }

            character.DiscardHand();
            character.DrawCards(handSize - character.Hand.Count);
            character.ResetEnergy();
            character.ResetArmor();

            // Poison lands here, so it can kill - hence the IsDead check before drawing.
            character.TickStatuses();

            if (character.IsDead) { continue; }

            // Everyone draws, enemies included. Their cards are how they act at all now, so a goblin
            // with an empty hand has nothing to choose between and can only Wait.
        }

        // ResetEnergy refills the pool but nothing tells the counter, which otherwise keeps showing
        // last turn's spent value until the next card is played. Refreshed here rather than from
        // inside ResetEnergy so Character stays unaware of any UI.
        if (ActiveCharacter != null) { ChangeActiveMana(ActiveCharacter.Energy); }

        // Enemies commit now, at the top of your turn, not at the end of it. That ordering is the
        // whole design: they announce one action, you spend the turn making it wrong, and it fires
        // anyway. Deciding at execution time instead would quietly undo every block you set up.
        Board board = GridManager.Instance.Read();

        GridManager.Instance.ClearIntents();

        foreach (Character enemy in LivingEnemies())
        {
            enemy.CommittedIntent = Decide(enemy, board);

            if (enemy.CommittedIntent.IsWait) { continue; }

            Debug.Log($"{enemy.name} intends: {enemy.CommittedIntent}");
            GridManager.Instance.ShowIntent(enemy, enemy.CommittedIntent);
        }

        yield return null;
    }

    /// <summary>
    /// Whether the player has any move left. Deliberately "can anyone afford anything in hand", not
    /// "is everyone at zero energy" - a character sitting on 1 energy holding only 2-cost cards would
    /// otherwise stall the turn forever, and an empty hand falls out of the same question. Frozen
    /// characters cannot act at all, so a fully frozen party ends the turn rather than hanging it.
    /// </summary>
    private bool CanAnyoneAct()
    {
        foreach (Character character in characters)
        {
            if (character == null || !character.IsPlayerControlled || !character.CanAct) { continue; }

            foreach (Card card in character.Hand)
            {
                if (character.CanAfford(card.cost)) { return true; }
            }
        }

        return false;
    }

    private IEnumerator EnemyResolve()
    {
        Phase = BattlePhase.EnemyResolve;

        // The promises have been kept or broken by now - leaving them drawn over the actual movement
        // would be worse than not drawing them at all.
        GridManager.Instance.ClearIntents();

        foreach (Character enemy in LivingEnemies())
        {
            // Frozen burns the whole turn, not one action - there is no partial thaw.
            if (!enemy.CanAct)
            {
                Debug.Log($"{enemy.name} is frozen and loses its turn");
                continue;
            }

            for (int ap = 0; ap < enemy.ActionPoints && !enemy.IsDead; ap++)
            {
                // Step 0 is the promise made at TurnStart and is executed as committed, right or
                // wrong. Every step after it is decided against the board as it stands, so it cannot
                // be stale - which is why only the first one can ever fizzle.
                Intent step = ap == 0
                    ? enemy.CommittedIntent
                    : Decide(enemy, GridManager.Instance.Read());

                if (step.IsWait) { break; }

                yield return StartCoroutine(Execute(enemy, step));

                // AddAction resolves the first action synchronously, so "queued" is not "finished".
                yield return new WaitUntil(() => ActionManager.Instance.IsIdle);
            }

            enemy.CommittedIntent = Intent.Wait();
        }

        // Actions resolve through the queue, and AddAction runs the first one synchronously, so
        // "queued" is not "finished". Without this the turn would roll over mid-animation.
        yield return new WaitUntil(() => ActionManager.Instance.IsIdle);
    }

    /// Asks this character's brain for one action. Wait if it has no brain, which is every player.
    private static Intent Decide(Character character, Board board)
    {
        EnemyBrain brain = EnemyBrain.For(character.Brain);

        if (brain == null || character.Tile == null) { return Intent.Wait(); }

        return brain.Decide(character, board);
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

        yield return new WaitForSeconds(stepDuration);
    }

    private IEnumerable<Character> LivingEnemies()
    {
        foreach (Character character in characters)
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

    private void Finish(string outcome)
    {
        Phase = BattlePhase.Finished;
        Debug.Log($"battle over: {outcome} ({TurnsRemaining} turns left)");
        SceneManager.LoadScene("MainMenu");
    }

    internal void ChangeActiveMana(int energy)
    {
        manaCounter.text = energy.ToString();
    }
}
