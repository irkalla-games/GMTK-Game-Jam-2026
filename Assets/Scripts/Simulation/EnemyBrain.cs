using System.Collections.Generic;
using UnityEngine;

/// Which rule list an enemy runs. A dropdown on Character rather than a component, so authoring an
/// enemy is picking a type rather than remembering to attach the matching script.
public enum BrainType
{
    None = 0,
    Warrior = 1,
    Archer = 2,
}

/// <summary>
/// Decides one action for one enemy. Plain C#, no Unity types beyond Vector2Int, no state - so the
/// same instance serves every goblin and can be exercised in a test without a scene.
///
/// One action, not a plan. Only an enemy's first action of a turn is committed in advance; the rest
/// are decided as they happen, so there is nothing to chain and nothing to simulate.
///
/// Rules are written as literal ordered ifs. Readability beats cleverness here - when a goblin does
/// something baffling, the fix should be findable by reading top to bottom.
/// </summary>
public abstract class EnemyBrain
{
    private static readonly WarriorBrain warrior = new();
    private static readonly ArcherBrain archer = new();

    public static EnemyBrain For(BrainType type) => type switch
    {
        BrainType.Warrior => warrior,
        BrainType.Archer => archer,
        _ => null,
    };

    /// <param name="self">Where this enemy is standing.</param>
    /// <param name="isPlayerControlled">Which side it is on - so brains work for either.</param>
    /// <param name="moveRange">How many steps it may take in one action.</param>
    public abstract Intent Decide(Vector2Int self, bool isPlayerControlled, int moveRange, Board board);
}

/// Walks at the nearest hero and swings when it gets there.
public class WarriorBrain : EnemyBrain
{
    public override Intent Decide(Vector2Int self, bool isPlayerControlled, int moveRange, Board board)
    {
        // 1. Adjacent to somebody? Hit them. Cheapest check first, and the one that matters most.
        foreach (Vector2Int cell in Adjacent(self))
        {
            if (board.IsEnemyOf(cell, isPlayerControlled)) { return Intent.AttackAt(cell); }
        }

        // 2. Otherwise close the distance toward whoever is nearest.
        if (!board.TryNearestEnemy(self, isPlayerControlled, out Vector2Int quarry)) { return Intent.Wait(); }

        Dictionary<Vector2Int, int> distance = board.Flood(self, moveRange);

        Vector2Int best = self;
        int bestGap = int.MaxValue;

        foreach (KeyValuePair<Vector2Int, int> reachable in distance)
        {
            int gap = Board.ChebyshevDistance(reachable.Key, quarry);

            // Ties broken by the shorter walk, so it does not wander to reach the same spot.
            if (gap < bestGap || (gap == bestGap && reachable.Value < distance[best]))
            {
                best = reachable.Key;
                bestGap = gap;
            }
        }

        return best == self ? Intent.Wait() : Intent.MoveAlong(board.PathTo(distance, self, best));
    }

    private static IEnumerable<Vector2Int> Adjacent(Vector2Int cell)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx != 0 || dy != 0) { yield return cell + new Vector2Int(dx, dy); }
            }
        }
    }
}

/// <summary>
/// Keeps its distance and shoots down straight lines. Retreating when threatened is what makes it
/// read as an archer rather than a warrior with reach.
/// </summary>
public class ArcherBrain : EnemyBrain
{
    public override Intent Decide(Vector2Int self, bool isPlayerControlled, int moveRange, Board board)
    {
        Dictionary<Vector2Int, int> distance = board.Flood(self, moveRange);

        // 1. Somebody is in melee. Get out first - a cornered archer that keeps shooting just dies.
        if (board.HasAdjacentEnemy(self, isPlayerControlled))
        {
            Vector2Int retreat = BestCell(board, distance, isPlayerControlled, self, preferSafety: true);

            if (retreat != self) { return Intent.MoveAlong(board.PathTo(distance, self, retreat)); }

            // Nowhere to run. Shoot whatever is in front of you rather than doing nothing.
        }

        // 2. A clear line right now? Take the shot.
        if (board.TryLineTarget(self, isPlayerControlled, out Vector2Int hit)) { return Intent.AttackAt(hit); }

        // 3. Otherwise walk to somewhere that *would* have a line. This is the step that produces
        //    move-then-shoot across two action points without any code saying so: the move happens,
        //    and next time round the line check above is simply true.
        Vector2Int firing = BestCell(board, distance, isPlayerControlled, self, preferSafety: false);

        return firing == self ? Intent.Wait() : Intent.MoveAlong(board.PathTo(distance, self, firing));
    }

    /// <summary>
    /// Scores every reachable tile and returns the best. preferSafety puts "not next to anybody"
    /// above "can shoot"; otherwise a firing line wins.
    /// </summary>
    private static Vector2Int BestCell(Board board, Dictionary<Vector2Int, int> distance,
                                       bool isPlayerControlled, Vector2Int self, bool preferSafety)
    {
        Vector2Int best = self;
        int bestScore = Score(board, self, isPlayerControlled, preferSafety);

        foreach (KeyValuePair<Vector2Int, int> reachable in distance)
        {
            if (reachable.Key == self) { continue; }

            int score = Score(board, reachable.Key, isPlayerControlled, preferSafety);

            if (score > bestScore)
            {
                bestScore = score;
                best = reachable.Key;
            }
        }

        return best;
    }

    private static int Score(Board board, Vector2Int cell, bool isPlayerControlled, bool preferSafety)
    {
        bool safe = !board.HasAdjacentEnemy(cell, isPlayerControlled);
        bool canShoot = board.TryLineTarget(cell, isPlayerControlled, out _);

        int safety = safe ? 1 : 0;
        int firing = canShoot ? 1 : 0;

        // Weighted rather than sorted so one comparison covers both, and the loser still breaks ties.
        return preferSafety ? safety * 4 + firing : firing * 4 + safety;
    }
}
