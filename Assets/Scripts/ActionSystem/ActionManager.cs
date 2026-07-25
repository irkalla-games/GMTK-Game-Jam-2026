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
        }

        isRunning = false;
    }
}
