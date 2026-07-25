using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
/// GameManager keeps its job - who is active, whose hand is on screen - and gains nothing from this.
/// </summary>
public class BattleRunner : MonoBehaviour
{
    [Tooltip("Turns the player has to survive. Reaching 0 is the win.")]
    [SerializeField] private int turnsToSurvive = 10;

    [Tooltip("Hand is topped back up to this at the start of each turn - unplayed cards carry over.")]
    [SerializeField] private int handSize = 5;

    [field: SerializeField, ReadOnlyField]
    public int TurnsRemaining { get; private set; }

    public BattlePhase Phase { get; private set; }

    private bool endTurnRequested;

    /// Hook this to the End Turn button. Ends the turn early, with energy still banked.
    public void RequestEndTurn()
    {
        if (Phase == BattlePhase.PlayerActing) { endTurnRequested = true; }
    }

    private void Start()
    {
        StartCoroutine(RunBattle());
    }

    private IEnumerator RunBattle()
    {
        TurnsRemaining = turnsToSurvive;

        while (true)
        {
            yield return StartCoroutine(TurnStart());

            Phase = BattlePhase.PlayerActing;
            endTurnRequested = false;

            while (!endTurnRequested && CanAnyoneAct()) { yield return null; }

            yield return StartCoroutine(EnemyResolve());

            TurnsRemaining--;

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

    private IEnumerator TurnStart()
    {
        Phase = BattlePhase.TurnStart;

        foreach (Character character in GameManager.Instance.Characters)
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

        //TODO: enemies commit an intent here and telegraph it. Until brains exist there is nothing to
        //commit, so EnemyResolve has nothing to execute and the turn is player-only.

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
        foreach (Character character in GameManager.Instance.Characters)
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
            if (!enemy.CanAct) { continue; }

            //TODO: the AP loop goes here, and is the whole of the enemy turn. Step 0 executes the
            //intent committed at TurnStart, right or wrong - a blocked move advances as far as it
            //can, an attack on an empty tile visibly misses. Every later step is decided fresh
            //against the live board, so it can never be stale. Waiting on EnemyBrain; until then
            //enemies stand there and the turn is player-only.
        }

        // Actions resolve through the queue, and AddAction runs the first one synchronously, so
        // "queued" is not "finished". Without this the turn would roll over mid-animation.
        yield return new WaitUntil(() => ActionManager.Instance.IsIdle);
    }

    private IEnumerable<Character> LivingEnemies()
    {
        foreach (Character character in GameManager.Instance.Characters)
        {
            if (character != null && !character.IsPlayerControlled && !character.IsDead)
            {
                yield return character;
            }
        }
    }

    private bool AllHeroesDead()
    {
        foreach (Character character in GameManager.Instance.Characters)
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
}
