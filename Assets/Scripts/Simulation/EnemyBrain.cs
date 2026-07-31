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
/// Decides one card to play, and where.
///
/// A brain supplies *tactics*, never capability. How hard an enemy hits, how far it reaches and how
/// far it walks are all properties of the cards in its hand - so a goblin is made dangerous by giving
/// it a better card, not by editing a number on the goblin. What separates a warrior from an archer
/// is what it does with the same information: one wants to be next to you, the other wants to be
/// exactly as far away as its longest card reaches.
///
/// That also means legality is never re-derived here. Card.Refusal already answers "may this card be
/// played on that tile", and it is the same call the player's click is gated on - so an enemy can
/// never play something a player in its position could not.
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

    public abstract Intent Decide(Character self, Board board);

    /// <summary>
    /// The best card this character could play at something on the other side, or none.
    ///
    /// A card counts as an attack purely because it is legal on a tile holding an enemy - DamageEffect
    /// refuses empty tiles and its own side, so only attacks pass there, and MoveEffect refuses
    /// occupied tiles so it never does. No card needs to be labelled; the refusal rules already
    /// separate them.
    ///
    /// Ties break toward the most hurt target, which is what makes a pack finish somebody off rather
    /// than spreading damage evenly across the party.
    /// </summary>
    protected static bool TryFindAttack(Character self, out Intent intent)
    {
        intent = Intent.Wait();

        if (self.Tile == null || GridManager.Instance == null) { return false; }

        int weakest = int.MaxValue;

        foreach (Card card in self.Hand)
        {
            foreach (GridTile tile in GridManager.Instance.GetTilesInRange(self.Tile, card.range))
            {
                Character occupant = tile.Occupant;

                if (occupant == null || !self.IsEnemyOf(occupant)) { continue; }

                if (card.Refusal(self, tile) != null) { continue; }

                if (occupant.Health >= weakest) { continue; }

                weakest = occupant.Health;
                intent = Intent.Play(card, tile.Coordinates);
            }
        }

        return !intent.IsWait;
    }

    /// <summary>
    /// The best legal move, scored by the brain's own idea of a good place to stand. Lower is better.
    ///
    /// Movement distance is the move card's range, so a card that reaches three tiles moves three
    /// tiles - there is no separate move speed, and giving an enemy a longer stride means giving it a
    /// better card.
    /// </summary>
    protected static bool TryFindMove(Character self, System.Func<Vector2Int, int> score, out Intent intent)
    {
        intent = Intent.Wait();

        if (self.Tile == null || GridManager.Instance == null) { return false; }

        int best = score(self.Tile.Coordinates);

        foreach (Card card in self.Hand)
        {
            foreach (GridTile tile in GridManager.Instance.GetTilesInRange(self.Tile, card.range))
            {
                if (tile.Occupant != null || card.Refusal(self, tile) != null) { continue; }

                int value = score(tile.Coordinates);

                if (value >= best) { continue; }

                best = value;
                intent = Intent.Play(card, tile.Coordinates);
            }
        }

        return !intent.IsWait;
    }

    /// The longest reach among this character's cards. An enemy's "range" is whatever it is holding.
    protected static int LongestReach(Character self)
    {
        int longest = 1;

        foreach (Card card in self.Hand)
        {
            longest = Mathf.Max(longest, card.range.MaxDistance);
        }

        return longest;
    }
}

/// <summary>
/// Closes and swings. Everything it can do is at short range, so its whole job is standing next to
/// somebody - which makes bodies in the way the counter, since a warrior that cannot reach anyone
/// spends its turn walking.
/// </summary>
public class WarriorBrain : EnemyBrain
{
    public override Intent Decide(Character self, Board board)
    {
        // Attack first. Cheapest to check and always better than repositioning.
        if (TryFindAttack(self, out Intent attack)) { return attack; }

        // Otherwise get closer to whoever is nearest.
        if (!board.TryNearestEnemy(self.Tile.Coordinates, self.Affiliation, out Vector2Int quarry))
        {
            return Intent.Wait();
        }

        return TryFindMove(self, cell => Board.ChebyshevDistance(cell, quarry), out Intent move)
            ? move
            : Intent.Wait();
    }
}

/// <summary>
/// Wants to be exactly as far away as its longest card reaches, and never adjacent.
///
/// Retreating before shooting is what makes it read as an archer rather than a warrior with reach -
/// an archer that stands and trades in melee is just a bad warrior.
/// </summary>
public class ArcherBrain : EnemyBrain
{
    public override Intent Decide(Character self, Board board)
    {
        Vector2Int here = self.Tile.Coordinates;
        bool threatened = board.HasAdjacentEnemy(here, self.Affiliation);

        // Cornered comes first: back off before taking a shot, unless there is nowhere to back off to.
        if (threatened && TryFindMove(self, Standoff(self, board), out Intent retreat)) { return retreat; }

        if (TryFindAttack(self, out Intent shot)) { return shot; }

        return TryFindMove(self, Standoff(self, board), out Intent reposition) ? reposition : Intent.Wait();
    }

    /// <summary>
    /// Scores a tile by how far it is from the ideal firing position: at the edge of its own reach,
    /// and never in melee. Being too close is punished hard, which is what produces the backing-away
    /// behaviour without a separate retreat rule.
    /// </summary>
    private static System.Func<Vector2Int, int> Standoff(Character self, Board board)
    {
        int reach = LongestReach(self);

        return cell =>
        {
            if (!board.TryNearestEnemy(cell, self.Affiliation, out Vector2Int quarry))
            {
                return 0;
            }

            int gap = Board.ChebyshevDistance(cell, quarry);
            int penalty = Mathf.Abs(gap - reach);

            // Adjacent is far worse than merely badly spaced, so it will give up a shot to step away.
            return gap <= 1 ? penalty + 10 : penalty;
        };
    }
}
