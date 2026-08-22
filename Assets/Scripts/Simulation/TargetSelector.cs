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
///
/// Being the one answer is also what makes Taunt a small change: a status that names a forced quarry
/// is consulted here, ahead of the priority, and both callers inherit it at once - the swing and the
/// walk redirect together without either brain knowing the status exists.
/// </summary>
public static class TargetSelector
{
    /// <summary>
    /// Takes `self` rather than a bare origin coordinate: the distance metrics need where it stands,
    /// and Taunt needs the statuses it is carrying. Both callers were already passing
    /// self.Tile.Coordinates, so nothing about the ranking changed when this became a character.
    /// </summary>
    public static bool TryPick(
        TargetPriority priority, Character self, IReadOnlyList<Character> candidates, out Character picked)
    {
        picked = null;

        if (candidates == null || candidates.Count == 0) { return false; }

        if (self == null || self.Tile == null) { return false; }

        // Stealth drops a candidate before either the forced quarry or the priority gets to see it -
        // above ForcedQuarry on purpose. A hidden taunter must make its taunter stop being chased, not
        // survive as an unreachable forced quarry that then refuses every substitute the way an
        // out-of-reach one legitimately does.
        //
        // A totem is dropped here too when self.IgnoresTotems - a hard exclusion, not a rank, so a
        // Skeleton Warrior with nothing but a totem in reach gets no attack this action point rather
        // than hitting it anyway. TargetPriority.Totem (see Rank below) is the opposite knob: it only
        // ever reorders, so a Ranger whose totem hunt whiffs still falls back to its usual target.
        List<Character> visible = new();

        foreach (Character candidate in candidates)
        {
            if (candidate == null || IsHidden(candidate)) { continue; }
            if (self.IgnoresTotems && candidate.TryGetComponent(out Totem _)) { continue; }

            visible.Add(candidate);
        }

        if (visible.Count == 0) { return false; }

        Character forced = ForcedQuarry(self);

        if (forced != null)
        {
            // A taunt overrides the priority outright, and refuses everyone else rather than falling
            // back to it. That single choice reads differently in each caller purely because they pass
            // different candidates: TryFindAttack passes only enemies it could legally hit, so an
            // out-of-reach taunter comes back false and becomes "no attack this action point", while
            // TryQuarry passes everyone alive, so the brain's move heads for the taunter instead. The
            // enemy therefore spends the turn closing on whoever taunted it.
            foreach (Character candidate in visible)
            {
                if (candidate != forced) { continue; }

                picked = forced;

                return true;
            }

            return false;
        }

        Vector2Int from = self.Tile.Coordinates;

        if (priority == TargetPriority.Random)
        {
            // Reservoir sampling: candidate i replaces the pick so far with probability 1/(i+1),
            // which lands on a uniform choice without needing to know the count up front.
            int seen = 0;

            foreach (Character candidate in visible)
            {
                seen++;

                if (Random.Range(0, seen) == 0) { picked = candidate; }
            }

            return picked != null;
        }

        int best = int.MaxValue;

        foreach (Character candidate in visible)
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

    /// <summary>
    /// Who a status is forcing `self` to go after, or null if nothing is. Taunt, today.
    ///
    /// First answer wins, which is ActiveStatuses' own FIFO order - auras ahead of carried statuses,
    /// then in the order they were gained. Only one Taunt can be carried at a time (a second one merges
    /// into the first and replaces its taunter), so this only ever has something to arbitrate when a
    /// totem is projecting a taunt aura as well, and there the aura winning is the same precedence
    /// every other hook in the codebase uses.
    /// </summary>
    private static Character ForcedQuarry(Character self)
    {
        foreach (Status status in self.ActiveStatuses())
        {
            Character forced = status.ForcedQuarry(self);

            if (forced != null) { return forced; }
        }

        return null;
    }

    /// Whether any status is hiding `character` from being picked. Stealth. First answer wins, the
    /// same ActiveStatuses FIFO order ForcedQuarry uses - not that more than one Hides source could
    /// disagree, but it keeps the two queries symmetric.
    private static bool IsHidden(Character character)
    {
        foreach (Status status in character.ActiveStatuses())
        {
            if (status.Hides(character)) { return true; }
        }

        return false;
    }

    /// Added to a non-totem candidate's Totem-priority rank, so every totem within reach beats every
    /// non-totem no matter how close - and a totem hunt with no totem in reach still falls back to the
    /// nearest legal candidate instead of returning nothing.
    private const int TotemMiss = 1000;

    /// Lower is better, so every caller keeps the `value >= best -> continue` shape the Try* helpers
    /// already use. Random is not scored here; see TryPick.
    private static int Rank(TargetPriority priority, Vector2Int from, Character candidate)
    {
        return priority switch
        {
            TargetPriority.Closest => Board.ChebyshevDistance(candidate.Tile.Coordinates, from),
            TargetPriority.Furthest => -Board.ChebyshevDistance(candidate.Tile.Coordinates, from),
            TargetPriority.Strongest => -candidate.Health,
            TargetPriority.Totem =>
                (candidate.TryGetComponent(out Totem _) ? 0 : TotemMiss)
                + Board.ChebyshevDistance(candidate.Tile.Coordinates, from),
            _ => candidate.Health, // Weakest, and the fallback for anything unhandled.
        };
    }
}
