using UnityEngine;

/// <summary>
/// The dice every game rule rolls - deck shuffles, loot, encounter rolls, random targeting, dodge
/// sidesteps. Cosmetic randomness (a damage number's jitter) keeps using UnityEngine.Random directly.
///
/// Its own state, apart from UnityEngine.Random's global one, because the engine rolls that one too:
/// URP's post-processing draws from it on every rendered frame. Sharing the stream made the rules'
/// outcomes depend on how many frames happened to render in between - so a battle came out
/// differently at a different frame rate, a recorded run could not be replayed at watching speed, and a
/// headless run (which renders nothing) could never match one in the Editor. Rolls here see only other
/// rolls here.
///
/// Same algorithm as UnityEngine.Random - the state is swapped in around each call - so every Range
/// behaves and distributes exactly as it did before this existed. Unseeded, it starts from a
/// time-based seed on first use, as random as ever; the balance bot seeds it (InitState) to make runs
/// reproducible.
/// </summary>
public static class GameDice
{
    private static Random.State state;
    private static bool seeded;

    public static void InitState(int seed)
    {
        Random.State outside = Random.state;

        Random.InitState(seed);
        state = Random.state;
        seeded = true;

        Random.state = outside;
    }

    /// The current state as text - what the balance bot's dice trace records. Reading it rolls nothing.
    public static string StateText
    {
        get
        {
            EnsureSeeded();
            return JsonUtility.ToJson(state);
        }
    }

    /// Integer in [minInclusive, maxExclusive) - UnityEngine.Random.Range(int, int).
    public static int Range(int minInclusive, int maxExclusive)
    {
        Random.State outside = Enter();
        int result = Random.Range(minInclusive, maxExclusive);
        Exit(outside);

        return result;
    }

    /// Float in [minInclusive, maxInclusive] - UnityEngine.Random.Range(float, float).
    public static float Range(float minInclusive, float maxInclusive)
    {
        Random.State outside = Enter();
        float result = Random.Range(minInclusive, maxInclusive);
        Exit(outside);

        return result;
    }

    /// Float in [0, 1] - UnityEngine.Random.value.
    public static float Value
    {
        get
        {
            Random.State outside = Enter();
            float result = Random.value;
            Exit(outside);

            return result;
        }
    }

    private static Random.State Enter()
    {
        EnsureSeeded();

        Random.State outside = Random.state;
        Random.state = state;

        return outside;
    }

    private static void Exit(Random.State outside)
    {
        state = Random.state;
        Random.state = outside;
    }

    private static void EnsureSeeded()
    {
        if (!seeded) { InitState(System.Environment.TickCount ^ (int)System.DateTime.Now.Ticks); }
    }

    /// Statics survive entering Play Mode when Domain Reload is off - a fresh session gets fresh dice.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => seeded = false;
}
