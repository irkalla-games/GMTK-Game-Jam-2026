using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

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

    [Tooltip("Every character on the board, both sides.")]
    [SerializeField] private List<Character> characters = new();

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
        // Before the loop, not after: TurnStart draws, and the hand viewer only builds viewers for
        // whoever is active. Order is safe either way now - ActiveHandViewer reads ActiveCharacter in
        // its own Start if it happened to subscribe after this fired.
        SetActiveCharacter(FirstPlayableCharacter());

        StartCoroutine(RunBattle());
    }

    private void Update()
    {
        if (Keyboard.current == null) { return; }

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
                Finish("defeat - every hero is down");
                yield break;
            }

            if (TurnsRemaining <= 0)
            {
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

            character.ResetEnergy();
            character.ResetArmor();

            // Poison lands here, so it can kill - hence the IsDead check before drawing.
            character.TickStatuses();

            if (character.IsDead) { continue; }

            if (character.IsPlayerControlled)
            {
                character.DrawCards(handSize - character.Hand.Count);
            }
        }

        // Enemies commit now, at the top of your turn, not at the end of it. That ordering is the
        // whole design: they announce one action, you spend the turn making it wrong, and it fires
        // anyway. Deciding at execution time instead would quietly undo every block you set up.
        Board board = GridManager.Instance.Read();

        foreach (Character enemy in LivingEnemies())
        {
            enemy.CommittedIntent = Decide(enemy, board);

            //TODO: telegraph CommittedIntent on the board. Until then the console is the only tell,
            //which makes disruption invisible - this is the next thing worth building.
            if (enemy.CommittedIntent.type != ActionType.Wait)
            {
                Debug.Log($"{enemy.name} intends: {enemy.CommittedIntent}");
            }
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

                if (step.type == ActionType.Wait) { break; }

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

        return brain.Decide(character.Tile.Coordinates, character.IsPlayerControlled,
                            character.MoveRange, board);
    }

    /// <summary>
    /// Carries out one intent. Both branches can come up empty, and that is the point - a committed
    /// intent is a promise made a whole turn ago against a board the player has been rearranging.
    /// </summary>
    private IEnumerator Execute(Character enemy, Intent step)
    {
        if (step.type == ActionType.Move)
        {
            yield return StartCoroutine(Advance(enemy, step));
            yield break;
        }

        GridTile tile = GridManager.Instance.GetTile(step.target);
        Character victim = tile != null ? tile.Occupant : null;

        // The miss. Whoever was standing here moved or died during your turn, and the swing still
        // costs the action point. This is what body-blocking and killing early actually buys.
        if (victim == null || victim.IsPlayerControlled == enemy.IsPlayerControlled)
        {
            //TODO: a whiff animation. A swing at nothing currently only reads in the console, and a
            //silent no-op looks like a bug rather than like your dodge working.
            Debug.Log($"{enemy.name} attacks {step.target} and hits nothing");
            yield break;
        }

        // Through the action queue with a null card, so enemy attacks pick up Strength, Double Attack
        // and the target's armor by the same path a played card would. Nothing about the damage
        // pipeline needed to know enemies exist.
        ActionManager.Instance.AddAction(new DamageAction(enemy.AttackDamage),
                                         new ActionContext(null, enemy, tile));
    }

    /// <summary>
    /// Walks the committed path one tile at a time, stopping at the first tile somebody is standing
    /// on rather than cancelling outright. A goblin shouldering up against the Knight reads far
    /// better than one that decided not to bother.
    ///
    /// MoveCharacter already refuses occupied tiles via MoveRefusal, so the block check is just its
    /// return value - checked per step rather than once, which is the whole difference between
    /// "destination taken" and "somebody is standing halfway along the route".
    /// </summary>
    private IEnumerator Advance(Character enemy, Intent step)
    {
        if (step.path == null) { yield break; }

        foreach (Vector2Int cell in step.path)
        {
            GridTile tile = GridManager.Instance.GetTile(cell);

            if (tile == null || !GridManager.Instance.MoveCharacter(enemy, tile))
            {
                Debug.Log($"{enemy.name} is blocked at {cell} and stops short");
                yield break;
            }

            yield return new WaitForSeconds(stepDuration);
        }
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
    }

    internal void ChangeActiveMana(int energy)
    {
        manaCounter.text = energy.ToString();
    }
}
