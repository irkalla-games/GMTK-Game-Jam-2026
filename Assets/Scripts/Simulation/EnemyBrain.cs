using System.Collections.Generic;
using UnityEngine;

/// Which rule list an enemy runs. A dropdown on Character rather than a component, so authoring an
/// enemy is picking a type rather than remembering to attach the matching script.
public enum BrainType
{
    None = 0,
    Warrior = 1,
    Ranger = 2,

    /// Reinforces first, then swings. Written into every enemy prefab as an int - append, never
    /// reorder, for the same reason RangeShape.Anywhere is 0.
    Summoner = 3,
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
    private static readonly SummonerBrain summoner = new();

    public static EnemyBrain For(BrainType type) => type switch
    {
        BrainType.Warrior => warrior,
        BrainType.Ranger => ranger,
        BrainType.Summoner => summoner,
        _ => null,
    };

    /// What this enemy would open with, against the board as it stands. Asked once at TurnStart to
    /// establish this turn's committed card - see Character.LockedCard - and again whenever that lock
    /// itself lapses (the card stopped being legal anywhere). While a lock is held, BattleManager asks
    /// Reaim instead, which keeps the same card but re-aims it live. Also what runs as a last resort in
    /// EnemyResolve if the lock has lapsed by the time this enemy's action point comes up.
    public abstract Intent Decide(Character self, Board board);

    /// <summary>
    /// Re-aims a card this character already committed to earlier this turn, at the board as it
    /// stands right now - what BattleManager.Decide falls back to while a lock is held, instead of
    /// asking Decide fresh and risking a different card. Only the victim/tile move; `card` and `kind`
    /// never do, which is what makes the icon's damage number a promise rather than a forecast.
    ///
    /// Returns Intent.Wait() when `card` cannot be aimed at anything at all right now - out of range,
    /// blocked, refused - which is the caller's signal to fall through to a fresh Decide instead of
    /// standing still on a dead lock.
    /// </summary>
    public Intent Reaim(Character self, Card card, IntentKind kind, Board board)
    {
        TargetPriority priority = self.CurrentPriority;

        switch (kind)
        {
            case IntentKind.Attack:
                return TryFindAttack(self, priority, out Intent attack, only: card) ? attack : Intent.Wait();

            case IntentKind.Move:
                // allowLateral stays false here: this is the one call whose Wait is a *signal*, not
                // just a dead end. BattleManager.Decide reads a Wait from Reaim as "this lock has
                // nothing left to offer" and re-asks the brain fresh - which tries Attack before Move
                // again. Allowing a lateral shuffle here would mean a locked Move almost never fails
                // (some tied tile nearly always exists), so the lock would hold forever and an enemy
                // would keep reaffirming a stale Move even after a hero walked into attack range. The
                // shuffle-when-boxed-in behaviour is not lost: it still runs inside the fresh
                // brain.Decide() this falls through to, as that path's own last-resort move.
                System.Func<Vector2Int, int> score = MoveScore(self, priority, board);
                return score != null && TryFindMove(self, score, out Intent move, only: card)
                    ? move : Intent.Wait();

            case IntentKind.Summon:
                return TryFindSummon(self, out Intent summon, only: card) ? summon : Intent.Wait();

            default:
                return Intent.Wait();
        }
    }

    /// Where this kind of enemy wants to stand, given who the current TargetPriority points at. Null
    /// when there is nobody left to point at, which is a Wait rather than a scoreless wander.
    protected abstract System.Func<Vector2Int, int> MoveScore(
        Character self, TargetPriority priority, Board board);

    /// The character the current priority names, out of everyone alive on the other side. The move's
    /// destination and the attack's victim are the same question - see TargetSelector.
    ///
    /// "The current priority" is not the last word: a Taunt on this character overrides it inside
    /// TryPick, so a taunted enemy walks toward whoever taunted it whatever its pattern says.
    protected static bool TryQuarry(Character self, TargetPriority priority, out Character quarry) =>
        TargetSelector.TryPick(
            priority, self, TargetSelector.LivingEnemiesOf(self), out quarry);

    /// <summary>
    /// The best card this character could play at something on the other side, or none.
    ///
    /// A card counts as an attack by whether Card.DamageFootprint lands on anybody at all once aimed
    /// at a legal tile - not by whether the clicked tile itself holds an enemy, which is all a Single
    /// card ever needed but an area card may legally leave empty (Card.Refusal allows aiming a splash
    /// at open ground as long as the range check passes). No card needs to be labelled; DamageEffect's
    /// own Refusal already separates an attack from a Move or a buff.
    ///
    /// The victim is chosen among *legal* targets by the current TargetPriority, exactly as before -
    /// a Furthest archer that cannot reach the furthest hero still shoots the furthest one it can
    /// reach. Among the tiles that would catch that victim, the one hitting the most enemies and the
    /// fewest of this character's own side wins; a Single card's footprint is always exactly one
    /// enemy and zero allies, so that comparison never has anything to break and the first legal tile
    /// found wins, the same as before.
    ///
    /// A Taunt is the one thing that makes this answer false while enemies are standing in reach: it
    /// names a quarry inside TryPick and refuses every substitute, so a taunted character with its
    /// taunter out of reach takes no attack at all and falls through to the move below - which heads
    /// for that same taunter. See TargetSelector and TauntStatus.
    ///
    /// Note this governs who is *aimed at*, not who a footprint may catch: a taunted enemy swinging an
    /// area card at its taunter still prefers the tile that also splashes somebody else.
    /// </summary>
    protected static bool TryFindAttack(
        Character self, TargetPriority priority, out Intent intent, Card only = null)
    {
        intent = Intent.Wait();

        if (self.Tile == null || GridManager.Instance == null) { return false; }

        List<(Card card, GridTile tile, List<Character> enemiesHit, int alliesHit)> options = new();
        List<Character> reachableEnemies = new();

        foreach (Card card in self.Hand)
        {
            if (only != null && card != only) { continue; }

            foreach (GridTile tile in GridManager.Instance.GetTilesInRange(self.Tile, card.range))
            {
                if (card.Refusal(self, tile) != null) { continue; }

                List<Character> enemies = new();
                int allies = 0;

                foreach (GridTile landed in card.DamageFootprint(self, tile))
                {
                    Character occupant = landed.Occupant;

                    if (occupant == null) { continue; }

                    if (self.IsEnemyOf(occupant)) { enemies.Add(occupant); }
                    else if (Character.AreAllies(occupant.Affiliation, self.Affiliation)) { allies++; }
                }

                if (enemies.Count == 0) { continue; }

                options.Add((card, tile, enemies, allies));

                foreach (Character enemy in enemies)
                {
                    if (!reachableEnemies.Contains(enemy)) { reachableEnemies.Add(enemy); }
                }
            }
        }

        if (!TargetSelector.TryPick(priority, self, reachableEnemies, out Character chosen))
        {
            return false;
        }

        (Card card, GridTile tile, List<Character> enemiesHit, int alliesHit) best = default;
        bool found = false;

        foreach (var option in options)
        {
            if (!option.enemiesHit.Contains(chosen)) { continue; }
            if (found && !BetterAttack(option, best)) { continue; }

            best = option;
            found = true;
        }

        if (!found) { return false; }

        intent = Intent.Play(IntentKind.Attack, best.card, best.tile.Coordinates, chosen);
        return true;
    }

    /// True if `candidate` is the better of two attacks that both already cover the chosen victim:
    /// more enemies caught, then fewer of the attacker's own side caught. A Single card's footprint
    /// is always exactly (1 enemy, 0 allies), so this only ever discriminates between area footprints.
    private static bool BetterAttack(
        (Card card, GridTile tile, List<Character> enemiesHit, int alliesHit) candidate,
        (Card card, GridTile tile, List<Character> enemiesHit, int alliesHit) current)
    {
        if (candidate.enemiesHit.Count != current.enemiesHit.Count)
        {
            return candidate.enemiesHit.Count > current.enemiesHit.Count;
        }

        return candidate.alliesHit < current.alliesHit;
    }

    /// <summary>
    /// The best legal move, scored by the brain's own idea of a good place to stand. Lower is better.
    ///
    /// Movement distance is the move card's range, so a card that reaches three tiles moves three
    /// tiles - there is no separate move speed, and giving an enemy a longer stride means giving it a
    /// better card.
    ///
    /// Runs in up to two passes. The first only accepts a strict improvement over standing still,
    /// same as always. When that finds nothing and `allowLateral` is set, a second pass accepts a
    /// tile that merely ties the current score - a genuinely boxed-in enemy still takes a visible
    /// sideways step instead of reading as frozen. `allowLateral` is false on a few callers (Ranger's
    /// retreat-before-shooting check) where a lateral "improvement" would silently eat the shot that
    /// was supposed to follow it - see the callers for why.
    ///
    /// Ties are broken on coordinates (GridManager.IsEarlier), never on GetTilesInRange's own
    /// enumeration order: BattleManager.LateUpdate re-decides every frame the board is dirty and only
    /// repaints on a changed answer, so a tiebreak that could flip between two equally-good tiles
    /// with no board change would flicker the intent icon.
    /// </summary>
    protected static bool TryFindMove(
        Character self, System.Func<Vector2Int, int> score, out Intent intent,
        Card only = null, bool allowLateral = false)
    {
        intent = Intent.Wait();

        if (self.Tile == null || GridManager.Instance == null) { return false; }

        int anchor = score(self.Tile.Coordinates);

        // Pass 1: any strict improvement over standing still. First tile found at the lowest score
        // wins - unchanged from the single-pass behaviour this replaces.
        int best = anchor;

        foreach ((Card card, GridTile tile) in CandidateMoves(self, only))
        {
            int value = score(tile.Coordinates);

            if (value >= best) { continue; }

            best = value;
            intent = Intent.Play(IntentKind.Move, card, tile.Coordinates);
        }

        if (!intent.IsWait || !allowLateral) { return !intent.IsWait; }

        // Pass 2: nothing strictly better exists. A boxed-in enemy still shuffles rather than
        // freezing, so accept a tile merely tied with standing still - tie-broken on coordinates
        // rather than scan order, so an unchanged board answers this the same way every time it is
        // asked (BattleManager.LateUpdate asks it every dirty frame and only repaints on a change).
        GridTile lateral = null;

        foreach ((Card card, GridTile tile) in CandidateMoves(self, only))
        {
            if (score(tile.Coordinates) != anchor) { continue; }
            if (lateral != null && !GridManager.IsEarlier(tile.Coordinates, lateral.Coordinates))
            {
                continue;
            }

            lateral = tile;
            intent = Intent.Play(IntentKind.Move, card, tile.Coordinates);
        }

        return !intent.IsWait;
    }

    /// Every (card, tile) this character could legally stand on right now - a Move card in hand, a
    /// tile within its range, empty, and not refused for any other reason. The shared filter behind
    /// both of TryFindMove's passes.
    private static IEnumerable<(Card card, GridTile tile)> CandidateMoves(Character self, Card only)
    {
        foreach (Card card in self.Hand)
        {
            if (only != null && card != only) { continue; }

            foreach (GridTile tile in GridManager.Instance.GetTilesInRange(self.Tile, card.range))
            {
                if (tile.Occupant != null || card.Refusal(self, tile) != null) { continue; }

                yield return (card, tile);
            }
        }
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
    protected static bool TryFindSummon(Character self, out Intent intent, Card only = null)
    {
        intent = Intent.Wait();

        if (self.Tile == null || GridManager.Instance == null) { return false; }

        int best = int.MaxValue;
        Vector2Int here = self.Tile.Coordinates;

        foreach (Card card in self.Hand)
        {
            if (only != null && card != only) { continue; }
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

    /// The longest reach among this character's cards. An enemy's "range" is whatever it is holding -
    /// a card's own click range, plus how far its widest area entry reaches beyond wherever it is
    /// aimed. A splash card does not need to be clicked as close as its plain range suggests; the
    /// blast covers the rest of the gap, which is what keeps a Ranger holding one from walking in
    /// closer than it actually has to.
    protected static int LongestReach(Character self)
    {
        int longest = 1;

        foreach (Card card in self.Hand)
        {
            longest = Mathf.Max(longest, card.range.MaxDistance + card.WidestAreaReach());
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

        // Opportunistic: reinforce only when there is nothing to swing at. A body whose summoning is
        // the point of it wants SummonerBrain instead, which asks this first.
        if (TryFindSummon(self, out Intent summon)) { return summon; }

        // Otherwise get closer to whoever the current priority names. allowLateral: true - this is the
        // last thing a warrior tries, so a boxed-in one shuffles rather than standing frozen.
        System.Func<Vector2Int, int> score = MoveScore(self, priority, board);

        return score != null && TryFindMove(self, score, out Intent move, allowLateral: true)
            ? move : Intent.Wait();
    }

    protected override System.Func<Vector2Int, int> MoveScore(
        Character self, TargetPriority priority, Board board)
    {
        if (!TryQuarry(self, priority, out Character quarry)) { return null; }

        // Rooted at the quarry rather than measured in a straight line from self: a field built
        // outward from the mark answers "how far is that tile, really" for every candidate at once,
        // routing around a Wall of Force or a body in the way instead of scoring the direct line as
        // closest regardless of whether anything can actually walk it. `self.Tile` is excluded so the
        // walker's own cell does not price itself out of its own field.
        Dictionary<Vector2Int, int> field =
            board.CostField(quarry.Tile.Coordinates, self.Tile.Coordinates);

        return cell => field.TryGetValue(cell, out int cost) ? cost : int.MaxValue;
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

        System.Func<Vector2Int, int> score = MoveScore(self, priority, board);
        bool threatened = board.HasAdjacentEnemy(self.Tile.Coordinates, self.Affiliation);

        // Cornered comes first: back off before taking a shot, unless there is nowhere to back off
        // to. allowLateral stays false here on purpose - a lateral "retreat" is not really one, and
        // with nothing to actually gain from moving this must fall through to the shot below rather
        // than spend the action point shuffling sideways instead of firing.
        if (threatened && score != null && TryFindMove(self, score, out Intent retreat)) { return retreat; }

        if (TryFindAttack(self, priority, out Intent shot)) { return shot; }

        // Last resort, so a boxed-in ranger shuffles rather than freezing.
        return score != null && TryFindMove(self, score, out Intent reposition, allowLateral: true)
            ? reposition : Intent.Wait();
    }

    /// <summary>
    /// Scores a tile by how far it is from the ideal firing position on the quarry the current
    /// priority names: at the edge of its own reach, and never in melee. Being too close is punished
    /// hard, which is what produces the backing-away behaviour without a separate retreat rule.
    ///
    /// The quarry is fixed once per decision rather than re-picked per candidate tile, so every cell
    /// is measured against the same person the enemy is actually aiming at.
    ///
    /// Deliberately still straight-line Chebyshev, not a cost field: this measures a firing distance,
    /// not a walking distance, and a shot is not a walk - `board` goes unused here for that reason.
    /// </summary>
    protected override System.Func<Vector2Int, int> MoveScore(
        Character self, TargetPriority priority, Board board)
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

/// <summary>
/// Reinforces first, then fights like a warrior.
///
/// The whole difference from WarriorBrain is the order of the first two questions, and that order is
/// the entire point of the type: TryFindSummon is the *last* thing a warrior asks and the first thing
/// this asks. A boss holding both an attack and a summon would otherwise never summon at all - a card
/// it can always play, against a hero it can almost always reach, wins every round.
///
/// Nothing here re-derives legality or pacing. An on-cooldown or Dormant summon simply fails
/// card.Refusal and TryFindSummon returns false, so a boss that summoned last round falls straight
/// through to attacking without this needing to know a cooldown exists. That is what keeps the
/// summon-first ordering from turning into a boss that does nothing else.
/// </summary>
public class SummonerBrain : EnemyBrain
{
    public override Intent Decide(Character self, Board board)
    {
        TargetPriority priority = self.CurrentPriority;

        if (TryFindSummon(self, out Intent summon)) { return summon; }

        if (TryFindAttack(self, priority, out Intent attack)) { return attack; }

        System.Func<Vector2Int, int> score = MoveScore(self, priority, board);

        return score != null && TryFindMove(self, score, out Intent move, allowLateral: true)
            ? move : Intent.Wait();
    }

    /// Identical to a warrior's: close on the quarry, routing around bodies and walls via a cost
    /// field rather than a straight line. A summoner that could not reach anyone would otherwise
    /// stand still while its adds did all the walking.
    protected override System.Func<Vector2Int, int> MoveScore(
        Character self, TargetPriority priority, Board board)
    {
        if (!TryQuarry(self, priority, out Character quarry)) { return null; }

        Dictionary<Vector2Int, int> field =
            board.CostField(quarry.Tile.Coordinates, self.Tile.Coordinates);

        return cell => field.TryGetValue(cell, out int cost) ? cost : int.MaxValue;
    }
}
