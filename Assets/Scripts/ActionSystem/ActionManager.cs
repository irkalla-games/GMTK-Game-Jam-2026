using System.Collections.Generic;
using UnityEngine;
using System.Collections;

public class ActionManager : MonoBehaviour
{
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
