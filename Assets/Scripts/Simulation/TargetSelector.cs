using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One answer to "who does this priority point at". Both users go through here: an attack asks it to
/// choose among the characters it can legally hit *right now*, and a move asks it to choose among
/// everyone alive on the other side. Attacking the pattern's quarry and walking toward the pattern's
/// quarry are the same question with different candidates, so they get the same function - two
/// implementations would drift the moment a fifth priority is added.
///
/// Note attacks pass *legal* targets, not "the quarry, or give up". A Furthest archer that cannot
/// reach the furthest hero still shoots the furthest one it can reach.
///
/// Random is resolved here, once per pick, by reservoir sampling - never as a per-comparison score. A
/// score function that redrew per call would make TryFindMove's tile scan incoherent, and the
/// movement quarry would disagree with the attack target inside the same decision.
/// </summary>
public static class TargetSelector
{
    public static bool TryPick(
        TargetPriority priority, Vector2Int from, IReadOnlyList<Character> candidates, out Character picked)
    {
        picked = null;

        if (candidates == null || candidates.Count == 0) { return false; }

        if (priority == TargetPriority.Random)
        {
            // Reservoir sampling: candidate i replaces the pick so far with probability 1/(i+1),
            // which lands on a uniform choice without needing to know the count up front.
            int seen = 0;

            foreach (Character candidate in candidates)
            {
                seen++;

                if (Random.Range(0, seen) == 0) { picked = candidate; }
            }

            return picked != null;
        }

        int best = int.MaxValue;

        foreach (Character candidate in candidates)
        {
            if (candidate == null || candidate.Tile == null) { continue; }

            int value = Rank(priority, from, candidate);

            if (picked != null && value >= best) { continue; }

            best = value;
            picked = candidate;
        }

        return picked != null;
    }

    /// Everyone alive on the other side and standing somewhere. The roster question, not a board
    /// question - Board is coordinates-only by design and Weakest needs Health, which no snapshot
    /// carries.
    public static List<Character> LivingEnemiesOf(Character self)
    {
        List<Character> found = new();

        if (self == null || BattleManager.Instance == null) { return found; }

        foreach (Character character in BattleManager.Instance.Characters)
        {
            if (character == null || character.IsDead || character.Tile == null) { continue; }

            if (!self.IsEnemyOf(character)) { continue; }

            found.Add(character);
        }

        return found;
    }

    /// Lower is better, so every caller keeps the `value >= best -> continue` shape the Try* helpers
    /// already use. Random is not scored here; see TryPick.
    private static int Rank(TargetPriority priority, Vector2Int from, Character candidate)
    {
        return priority switch
        {
            TargetPriority.Closest => Board.ChebyshevDistance(candidate.Tile.Coordinates, from),
            TargetPriority.Furthest => -Board.ChebyshevDistance(candidate.Tile.Coordinates, from),
            _ => candidate.Health, // Weakest, and the fallback for anything unhandled.
        };
    }
}
