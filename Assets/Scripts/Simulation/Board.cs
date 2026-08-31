using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A read-only snapshot of where everyone is standing, in coordinates only.
///
/// The one genuinely Unity-free thing in the project: no GridTile, no Character, nothing that needs a
/// scene. That is the whole point - brains take a Board and can be reasoned about (and tested)
/// without opening the Editor, and they cannot accidentally move a real character while deciding.
///
/// Deliberately has no Clone() or Apply(). Those exist to chain a simulated plan several steps ahead,
/// and this game does not need one: only an enemy's first action is committed, and every action after
/// it is decided fresh against the live board. Simulating ahead would produce a plan that is thrown
/// away. Read() once per decision instead.
/// </summary>
public class Board
{
    /// 8-way, to match RangeShape.Chebyshev - the metric the Move card already uses. A 4-way board
    /// would disagree with the Move highlight about diagonals, and enemies would path differently
    /// from how the player can.
    private static readonly Vector2Int[] Steps =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    /// Shots travel straight only. An archer has no facing, so all four count.
    private static readonly Vector2Int[] Cardinals =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    };

    private readonly HashSet<Vector2Int> cells = new();

    /// Coordinate -> that occupant's affiliation. Absent means the tile is empty.
    private readonly Dictionary<Vector2Int, PlayableCharacter> occupants = new();

    /// Cells a tile effect refuses entry to - a Wall of Force. Separate from `cells` on purpose: a
    /// blocked tile still exists for range and line-of-fire purposes (Exists, TryLineTarget), it just
    /// cannot be walked onto. Folding it into `cells` would make a walled tile invisible to a card's
    /// reach, not just to movement.
    private readonly HashSet<Vector2Int> blocked = new();

    public void AddCell(Vector2Int cell) => cells.Add(cell);

    public void SetOccupant(Vector2Int cell, PlayableCharacter affiliation) => occupants[cell] = affiliation;

    public void SetBlocked(Vector2Int cell) => blocked.Add(cell);

    public bool Exists(Vector2Int cell) => cells.Contains(cell);

    public bool IsOccupied(Vector2Int cell) => occupants.ContainsKey(cell);

    /// A tile you could stand on: on the board, nobody there, and no wall refusing entry.
    public bool IsWalkable(Vector2Int cell) =>
        cells.Contains(cell) && !occupants.ContainsKey(cell) && !blocked.Contains(cell);

    public bool IsEnemyOf(Vector2Int cell, PlayableCharacter affiliation) =>
        occupants.TryGetValue(cell, out PlayableCharacter occupant) && Character.AreEnemies(occupant, affiliation);

    public bool HasAdjacentEnemy(Vector2Int from, PlayableCharacter affiliation)
    {
        foreach (Vector2Int step in Steps)
        {
            if (IsEnemyOf(from + step, affiliation)) { return true; }
        }

        return false;
    }

    /// <summary>
    /// Step counts from `start` to every tile reachable within maxSteps, walking only empty tiles.
    /// BFS rather than A* - the boards are tiny and this is twenty lines.
    ///
    /// `start` itself is included at distance 0 even though it is occupied by the walker.
    /// </summary>
    public Dictionary<Vector2Int, int> Flood(Vector2Int start, int maxSteps)
    {
        Dictionary<Vector2Int, int> distance = new() { [start] = 0 };
        Queue<Vector2Int> frontier = new();
        frontier.Enqueue(start);

        while (frontier.Count > 0)
        {
            Vector2Int cell = frontier.Dequeue();
            int next = distance[cell] + 1;

            if (next > maxSteps) { continue; }

            foreach (Vector2Int step in Steps)
            {
                Vector2Int neighbour = cell + step;

                if (!IsWalkable(neighbour) || distance.ContainsKey(neighbour)) { continue; }

                distance[neighbour] = next;
                frontier.Enqueue(neighbour);
            }
        }

        return distance;
    }

    /// Extra steps a body is worth detouring around. High enough that a route with an open alternative
    /// always wins it, low enough that a corridor with no alternative still gets walked - see
    /// CostField.
    public const int OccupiedCost = 4;

    /// <summary>
    /// Cost-to-reach from `start` to every cell on the board, walking through occupied tiles at a
    /// price rather than refusing them outright. Empty ground costs 1 a step; a cell with somebody
    /// standing on it costs 1 + OccupiedCost, so a body already in the room is a detour rather than a
    /// wall - a column of enemies in a one-wide corridor still shuffles forward as the front one
    /// clears its tile, instead of every rank behind it deciding there is no way round and standing
    /// still. Wall of Force (`blocked`) is not a detour: it is impassable exactly like Flood treats it.
    ///
    /// `ignore` is the cell the walker is deciding from, which is occupied by the walker itself and
    /// would otherwise price its own starting tile into the field.
    ///
    /// Dijkstra rather than BFS, since the two edge weights differ - but both are small fixed
    /// integers on a board with a few dozen cells, so a bucket queue indexed by tentative cost is the
    /// whole algorithm; no heap needed.
    /// </summary>
    public Dictionary<Vector2Int, int> CostField(Vector2Int start, Vector2Int ignore)
    {
        Dictionary<Vector2Int, int> cost = new() { [start] = 0 };
        List<Queue<Vector2Int>> buckets = new() { new Queue<Vector2Int>(new[] { start }) };

        for (int bucket = 0; bucket < buckets.Count; bucket++)
        {
            Queue<Vector2Int> frontier = buckets[bucket];

            while (frontier.Count > 0)
            {
                Vector2Int cell = frontier.Dequeue();

                // A cell can be enqueued into more than one bucket before its cheapest cost is
                // settled; skip a stale entry that no longer matches the cost it was recorded under.
                if (cost[cell] != bucket) { continue; }

                foreach (Vector2Int step in Steps)
                {
                    Vector2Int neighbour = cell + step;

                    if (!cells.Contains(neighbour) || blocked.Contains(neighbour)) { continue; }

                    bool occupied = neighbour != ignore && neighbour != start
                        && occupants.ContainsKey(neighbour);
                    int next = bucket + 1 + (occupied ? OccupiedCost : 0);

                    if (cost.TryGetValue(neighbour, out int known) && known <= next) { continue; }

                    cost[neighbour] = next;

                    while (buckets.Count <= next) { buckets.Add(new Queue<Vector2Int>()); }

                    buckets[next].Enqueue(neighbour);
                }
            }
        }

        return cost;
    }

    /// <summary>
    /// Walks backwards from `goal` to `start` through a Flood result, returning the steps to take in
    /// order and excluding the start tile. Empty if goal was never reached.
    ///
    /// The path is what gets stored on an Intent rather than just the destination - a blocked move
    /// has to advance as far as it can, and that needs to know the route, not the endpoint.
    /// </summary>
    public List<Vector2Int> PathTo(Dictionary<Vector2Int, int> distance, Vector2Int start, Vector2Int goal)
    {
        List<Vector2Int> path = new();

        if (!distance.TryGetValue(goal, out int remaining)) { return path; }

        Vector2Int cell = goal;

        while (cell != start)
        {
            path.Add(cell);
            remaining--;

            bool stepped = false;

            foreach (Vector2Int step in Steps)
            {
                Vector2Int previous = cell + step;

                if (distance.TryGetValue(previous, out int d) && d == remaining)
                {
                    cell = previous;
                    stepped = true;
                    break;
                }
            }

            // Defensive: a well-formed Flood always has a predecessor, but never loop forever on one.
            if (!stepped) { break; }
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// The first character standing in a straight line from `from`, and whether it is a target worth
    /// shooting. Shots stop at the first body, friend or foe - which is what makes body-blocking an
    /// archer work.
    /// </summary>
    public bool TryLineTarget(Vector2Int from, PlayableCharacter affiliation, int range, out Vector2Int hit)
    {
        foreach (Vector2Int direction in Cardinals)
        {
            Vector2Int cell = from + direction;

            for (int step = 1; step <= range && Exists(cell); step++)
            {
                if (IsOccupied(cell))
                {
                    if (IsEnemyOf(cell, affiliation))
                    {
                        hit = cell;
                        return true;
                    }

                    break;   // an ally is in the way - this line is spent
                }

                cell += direction;
            }
        }

        hit = default;
        return false;
    }

    /// Nearest enemy by Chebyshev distance, ignoring walls. Used to pick who to walk toward.
    public bool TryNearestEnemy(Vector2Int from, PlayableCharacter affiliation, out Vector2Int nearest)
    {
        nearest = default;
        int best = int.MaxValue;

        foreach (KeyValuePair<Vector2Int, PlayableCharacter> occupant in occupants)
        {
            if (!Character.AreEnemies(occupant.Value, affiliation)) { continue; }

            Vector2Int delta = occupant.Key - from;
            int distance = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));

            if (distance >= best) { continue; }

            best = distance;
            nearest = occupant.Key;
        }

        return best != int.MaxValue;
    }

    public static int ChebyshevDistance(Vector2Int a, Vector2Int b)
    {
        Vector2Int delta = a - b;
        return Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
    }

    public IEnumerable<Vector2Int> Cells => cells;
}
