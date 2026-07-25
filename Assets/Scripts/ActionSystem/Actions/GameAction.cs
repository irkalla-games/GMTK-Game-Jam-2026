using System.Collections;

/// <summary>
/// A single step of game logic, built by a CardEffect each time its card is played.
///
/// Actions are stateless: their fields are readonly authoring numbers handed in at construction, and
/// everything that varies per play arrives through the ActionContext. Nothing is written to a field
/// during resolution - keep per-resolution state in coroutine locals, which are already per-call.
///
/// Deliberately a plain C# class, not a ScriptableObject. Actions are never assets - they are built
/// with `new` on every play, and most of them take constructor arguments, which ScriptableObject
/// cannot do (Unity would demand CreateInstance and only ever call a parameterless constructor).
/// CardEffect is the ScriptableObject in this pair; the action it queues is not.
/// </summary>
public abstract class GameAction
{
    /// <summary>
    /// How long the queue pauses after this action, so effects read one at a time rather than all
    /// landing on the same frame. Comes from ActionManager so the pacing of the whole game is one
    /// serialized number instead of a literal repeated in every action.
    ///
    /// Virtual, so an action that animates for longer - a multi-tile move - can lengthen its own.
    /// Falls back to the old literal if no ActionManager is in the scene, which keeps actions
    /// runnable from a test harness.
    /// </summary>
    protected virtual float ResolveDelay =>
        ActionManager.Instance != null ? ActionManager.Instance.DefaultResolveDelay : 0.15f;

    public abstract IEnumerator Execute(ActionContext ctx);
}
