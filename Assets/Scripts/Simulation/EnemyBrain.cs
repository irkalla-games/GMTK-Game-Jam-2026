using System.Collections.Generic;
using UnityEngine;

/// Which rule list an enemy runs. A dropdown on Character rather than a component, so authoring an
/// enemy is picking a type rather than remembering to attach the matching script.
public enum BrainType
{
    None = 0,
    Warrior = 1,
    Ranger = 2,
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
    private static readonly RangerBrain ranger = new();

    public static EnemyBrain For(BrainType type) => type switch
    {
        BrainType.Warrior => warrior,
        BrainType.Ranger => ranger,
        _ => null,
    };

    /// The category to announce at TurnStart. See Resolve for what actually plays.
    public abstract Intent Decide(Character self, Board board);

    /// <summary>
    /// A concrete card and tile inside a category this enemy already announced, against the board as
    /// it stands now. This is the whole promise: what was committed at TurnStart is the *kind*, never
    /// the card and never the tile - so an archer that showed a sword hits you wherever you moved to,
    /// as long as some legal shot exists.
    ///
    /// Summon is honoured first and is never preempted by an available shot - a ranger that can call
    /// in backup does it on cooldown, which is the same sentence RangerBrain.Decide already says.
    /// Only a Summon that has become *illegal* - every tile in range filled up - gives way, and it
    /// gives way to an attack rather than to nothing.
    ///
    /// Every remaining kind then tries to attack: Attack because that is what it promised, Move
    /// because a committed Move upgrades into an Attack the moment one becomes legal, and a blocked
    /// Summon because falling back to a swing beats standing still. A Boot turning into a hit only
    /// ever surprises the player upward.
    ///
    /// Neither a committed Attack nor a blocked Summon falls back to walking. This is the inverse of
    /// the usual and it is the point - burning the action point is what a block is bought to produce.
    ///
    /// Non-virtual: the flow is the same for every brain, and the one part that differs - where this
    /// kind of enemy wants to stand - is MoveScore.
    /// </summary>
    public Intent Resolve(Character self, Board board, IntentKind committed)
    {
        if (self.Tile == null || committed == IntentKind.Wait) { return Intent.Wait(); }

        TargetPriority priority = self.CurrentPriority;

        if (committed == IntentKind.Summon && TryFindSummon(self, out Intent summon)) { return summon; }

        if (TryFindAttack(self, priority, out Intent attack)) { return attack; }

        if (committed != IntentKind.Move) { return Intent.Wait(); }

        System.Func<Vector2Int, int> score = MoveScore(self, priority);

        return score != null && TryFindMove(self, score, out Intent move) ? move : Intent.Wait();
    }

    /// Where this kind of enemy wants to stand, given who the current TargetPriority points at. Null
    /// when there is nobody left to point at, which is a Wait rather than a scoreless wander.
    protected abstract System.Func<Vector2Int, int> MoveScore(Character self, TargetPriority priority);

    /// The character the current priority names, out of everyone alive on the other side. The move's
    /// destination and the attack's victim are the same question - see TargetSelector.
    protected static bool TryQuarry(Character self, TargetPriority priority, out Character quarry) =>
        TargetSelector.TryPick(
            priority, self.Tile.Coordinates, TargetSelector.LivingEnemiesOf(self), out quarry);

    /// <summary>
    /// The best card this character could play at something on the other side, or none.
    ///
    /// A card counts as an attack purely because it is legal on a tile holding an enemy - DamageEffect
    /// refuses empty tiles and its own side, so only attacks pass there, and MoveEffect refuses
    /// occupied tiles so it never does. No card needs to be labelled; the refusal rules already
    /// separate them.
    ///
    /// The victim is chosen among *legal* targets by the current TargetPriority - a Furthest archer
    /// that cannot reach the furthest hero still shoots the furthest one it can reach. Weakest is the
    /// old hardcoded tie-break, preserved as TargetSelector's fallback.
    /// </summary>
    protected static bool TryFindAttack(Character self, TargetPriority priority, out Intent intent)
    {
        intent = Intent.Wait();

        if (self.Tile == null || GridManager.Instance == null) { return false; }

        List<(Card card, GridTile tile, Character victim)> options = new();
        List<Character> victims = new();

        foreach (Card card in self.Hand)
        {
            foreach (GridTile tile in GridManager.Instance.GetTilesInRange(self.Tile, card.range))
            {
                Character occupant = tile.Occupant;

                if (occupant == null || !self.IsEnemyOf(occupant)) { continue; }

                if (card.Refusal(self, tile) != null) { continue; }

                options.Add((card, tile, occupant));

                if (!victims.Contains(occupant)) { victims.Add(occupant); }
            }
        }

        if (!TargetSelector.TryPick(priority, self.Tile.Coordinates, victims, out Character chosen))
        {
            return false;
        }

        foreach ((Card card, GridTile tile, Character victim) option in options)
        {
            if (option.victim != chosen) { continue; }

            intent = Intent.Play(IntentKind.Attack, option.card, option.tile.Coordinates);
            return true;
        }

        return false;
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
                intent = Intent.Play(IntentKind.Move, card, tile.Coordinates);
            }
        }

        return !intent.IsWait;
    }

    /// <summary>
    /// The best legal Summon this character could play, or none. Move and Summon are both only legal
    /// on an empty tile, so occupancy alone can't tell them apart the way it tells attacks from moves -
    /// this checks the card's effects directly instead. Picks the closest legal empty tile to itself.
    ///
    /// Cooldown needs no special handling here: an on-cooldown Summon card simply fails card.Refusal
    /// like anything else out of range or otherwise illegal, so this just returns false on its own
    /// during the cooldown window.
    /// </summary>
    protected static bool TryFindSummon(Character self, out Intent intent)
    {
        intent = Intent.Wait();

        if (self.Tile == null || GridManager.Instance == null) { return false; }

        int best = int.MaxValue;
        Vector2Int here = self.Tile.Coordinates;

        foreach (Card card in self.Hand)
        {
            if (!card.HasEffect<SummonEffect>()) { continue; }

            foreach (GridTile tile in GridManager.Instance.GetTilesInRange(self.Tile, card.range))
            {
                if (tile.Occupant != null || card.Refusal(self, tile) != null) { continue; }

                int distance = Board.ChebyshevDistance(tile.Coordinates, here);

                if (distance >= best) { continue; }

                best = distance;
                intent = Intent.Play(IntentKind.Summon, card, tile.Coordinates);
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
        TargetPriority priority = self.CurrentPriority;

        // Attack first. Cheapest to check and always better than repositioning.
        if (TryFindAttack(self, priority, out Intent attack)) { return attack; }

        // Opportunistic: reinforce only when there is nothing to swing at. No Warrior deck holds a
        // Summon card yet, so this is inert today, not dead code for a kit that will exist later.
        if (TryFindSummon(self, out Intent summon)) { return summon; }

        // Otherwise get closer to whoever the current priority names.
        System.Func<Vector2Int, int> score = MoveScore(self, priority);

        return score != null && TryFindMove(self, score, out Intent move) ? move : Intent.Wait();
    }

    protected override System.Func<Vector2Int, int> MoveScore(Character self, TargetPriority priority)
    {
        if (!TryQuarry(self, priority, out Character quarry)) { return null; }

        Vector2Int mark = quarry.Tile.Coordinates;

        return cell => Board.ChebyshevDistance(cell, mark);
    }
}

/// <summary>
/// Wants to be exactly as far away as its longest card reaches, and never adjacent.
///
/// Retreating before shooting is what makes it read as a ranger rather than a warrior with reach - one
/// that stands and trades in melee is just a bad warrior.
/// </summary>
public class RangerBrain : EnemyBrain
{
    public override Intent Decide(Character self, Board board)
    {
        TargetPriority priority = self.CurrentPriority;

        // Reinforcing comes first, ahead of even shooting - a ranger that can call in backup does it
        // on cooldown, not only when it has nothing better to do.
        if (TryFindSummon(self, out Intent summon)) { return summon; }

        System.Func<Vector2Int, int> score = MoveScore(self, priority);
        bool threatened = board.HasAdjacentEnemy(self.Tile.Coordinates, self.Affiliation);

        // Cornered comes first: back off before taking a shot, unless there is nowhere to back off to.
        if (threatened && score != null && TryFindMove(self, score, out Intent retreat)) { return retreat; }

        if (TryFindAttack(self, priority, out Intent shot)) { return shot; }

        return score != null && TryFindMove(self, score, out Intent reposition) ? reposition : Intent.Wait();
    }

    /// <summary>
    /// Scores a tile by how far it is from the ideal firing position on the quarry the current
    /// priority names: at the edge of its own reach, and never in melee. Being too close is punished
    /// hard, which is what produces the backing-away behaviour without a separate retreat rule.
    ///
    /// The quarry is fixed once per decision rather than re-picked per candidate tile, so every cell
    /// is measured against the same person the enemy is actually aiming at.
    /// </summary>
    protected override System.Func<Vector2Int, int> MoveScore(Character self, TargetPriority priority)
    {
        if (!TryQuarry(self, priority, out Character quarry)) { return null; }

        int reach = LongestReach(self);
        Vector2Int mark = quarry.Tile.Coordinates;

        return cell =>
        {
            int gap = Board.ChebyshevDistance(cell, mark);
            int penalty = Mathf.Abs(gap - reach);

            // Adjacent is far worse than merely badly spaced, so it will give up a shot to step away.
            return gap <= 1 ? penalty + 10 : penalty;
        };
    }
}
