using System.Collections.Generic;
using UnityEngine;

public enum ActionType
{
    Wait = 0,
    Move = 1,
    Attack = 2,
}

/// <summary>
/// One thing an enemy means to do, in coordinates. A brain's whole output.
///
/// Targets a tile, never a character - the same rule cards follow. Whoever is standing there is
/// resolved at execution time, which is exactly what makes an intent able to miss: the player has a
/// whole turn to move the target out from under a committed attack.
///
/// Move stores the full path rather than just the destination. A blocked move advances as far as it
/// can and stops, and that needs the route - with only an endpoint there is no way to tell "somebody
/// is standing on my destination" from "somebody is standing halfway along it".
/// </summary>
public struct Intent
{
    public ActionType type;

    /// Step by step, excluding the tile the mover starts on. Move only.
    public List<Vector2Int> path;

    /// The tile being struck. Attack only.
    public Vector2Int target;

    public static Intent Wait() => new() { type = ActionType.Wait };

    public static Intent MoveAlong(List<Vector2Int> path) =>
        path == null || path.Count == 0 ? Wait() : new Intent { type = ActionType.Move, path = path };

    public static Intent AttackAt(Vector2Int target) =>
        new() { type = ActionType.Attack, target = target };

    public override string ToString() => type switch
    {
        ActionType.Move => $"Move to {(path != null && path.Count > 0 ? path[^1].ToString() : "nowhere")}",
        ActionType.Attack => $"Attack {target}",
        _ => "Wait",
    };
}
