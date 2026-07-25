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
    public abstract IEnumerator Execute(ActionContext ctx);
}
