using UnityEngine;

public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T Instance { get; private set; }

    protected virtual void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this as T;
    }

    /// <summary>
    /// Clears the static when this instance goes away. Without it, a scene reload leaves Instance
    /// pointing at a destroyed object; Unity's overloaded == reports that as null so the next
    /// instance does claim the slot, but anything holding the stale reference in a local sees a
    /// fake-null it never asked for.
    ///
    /// ReferenceEquals rather than ==, because the question here is "is the static pointing at me
    /// specifically", which is plain reference identity - not Unity's is-it-alive comparison.
    /// </summary>
    protected virtual void OnDestroy()
    {
        if (ReferenceEquals(Instance, this)) { Instance = null; }
    }

    protected virtual void OnApplicationQuit()
    {
        // No Destroy here. The scene is being torn down regardless, and destroying during shutdown
        // fires OnDestroy on components whose dependencies have already gone, which produces
        // null-reference spam at exactly the moment it is least useful.
        Instance = null;
    }
}

public abstract class PersistantSingleton<T> : Singleton<T> where T : MonoBehaviour
{
    protected override void Awake()
    {
        base.Awake();
        DontDestroyOnLoad(gameObject);
    }
}
