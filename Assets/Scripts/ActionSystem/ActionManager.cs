using System;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;

/// <summary>
/// The queue every resolving action passes through, so effects land one at a time instead of all on
/// the same frame.
///
/// A singleton because both sides need it: card effects queue player actions, and enemy resolution
/// will queue its own. Reaching it through CardPlayManager - as effects used to - would make enemy
/// turns depend on the player's card UI, which may legitimately have no selection and no hand.
/// </summary>
public class ActionManager : Singleton<ActionManager>
{
    [Tooltip("Pause after each action resolves. Actions read this through GameAction.ResolveDelay, "
             + "so retuning the pacing of the whole game is this one number.")]
    [SerializeField] private float defaultResolveDelay = 0.15f;

    public float DefaultResolveDelay => defaultResolveDelay;

    private readonly Queue<(GameAction action, ActionContext ctx)> actions = new();

    private bool isRunning;

    /// <summary>
    /// Nothing left to resolve. The turn loop waits on this before letting the next enemy act -
    /// StartCoroutine runs synchronously up to the first yield, so AddAction resolves the first
    /// action *inside* the call and "I queued it" is not the same as "it finished".
    /// </summary>
    public bool IsIdle => !isRunning && actions.Count == 0;

    /// <summary>
    /// Raised after each action finishes resolving, action and context both included so a listener
    /// can filter by what happened (a DamageAction landing) and who it happened to (ctx.source).
    ///
    /// This is the one hook anything reactive - an aura, a future "on hit" trinket - observes the
    /// action pipeline through, rather than each such feature reaching into individual GameAction
    /// subclasses. Fires once per action, not once per card: a card that queues several actions raises
    /// this once for each of them, in resolution order.
    /// </summary>
    public event Action<GameAction, ActionContext> ActionResolved;

    public void AddAction(GameAction action, ActionContext ctx)
    {
        actions.Enqueue((action, ctx));

        if (!isRunning)
            StartCoroutine(ProcessQueue());
    }

    IEnumerator ProcessQueue()
    {
        isRunning = true;

        while (actions.Count > 0)
        {
            var (action, ctx) = actions.Dequeue();
            yield return action.Execute(ctx);
            ActionResolved?.Invoke(action, ctx);
        }

        isRunning = false;
    }
}
