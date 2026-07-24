using System.Collections.Generic;
using UnityEngine;
using System.Collections;

public class ActionManager : MonoBehaviour
{
    private Queue<GameAction> actions = new();

    private bool isRunning;

    public void AddAction(GameAction action)
    {
        actions.Enqueue(action);

        if (!isRunning)
            StartCoroutine(ProcessQueue());
    }

    IEnumerator ProcessQueue()
    {
        isRunning = true;

        while (actions.Count > 0)
        {
            yield return actions.Dequeue().Execute();
        }

        isRunning = false;
    }
}
