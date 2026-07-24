using System.Collections;
using UnityEngine;

public abstract class GameAction : ScriptableObject
{
    public abstract IEnumerator Execute();
}
