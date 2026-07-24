using System.Collections;

/// <summary>
/// A single step of game logic, built by a CardData subclass each time its card is played.
///
/// Actions are stateless: their fields are readonly authoring numbers handed in at construction, and
/// everything that varies per play arrives through the ActionContext. Nothing is written to a field
/// during resolution - keep per-resolution state in coroutine locals, which are already per-call.
/// </summary>
public abstract class GameAction
{
    public abstract IEnumerator Execute(ActionContext ctx);
}
