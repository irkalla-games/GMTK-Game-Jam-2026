#if UNITY_EDITOR || BOT_RUNNER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// What one candidate play would achieve, in the units BotWeights prices - see BotWeights' tooltips for
/// what each row means. Every number is measured, not guessed: damage and kills from Card.PreviewDamage,
/// who a shield or heal reaches from Card.LandedTiles, magnitudes through ActionContext.Amount.
/// </summary>
public sealed class BotFeatures
{
    public float damage, kills, threatRemoved, friendlyFire, mitigation, mitigationSpare, heal, lifeSaved, dot,
        debuff, buff, totemCover, totemBase, summon, moveSetup, moveSafety, loot, auraMove, draw, energy,
        selfDamage, discard, futureThreat, lootApproach, exposure, mitigationNeed, forecast, focus, hunt;

    public int cost;

    public float Score(BotWeights w) =>
        damage * w.damage + kills * w.kill + threatRemoved * w.threatRemoved + friendlyFire * w.friendlyFire
        + mitigation * w.mitigation + mitigationSpare * w.mitigationSpare + heal * w.heal
        + lifeSaved * w.lifeSaved + dot * w.dot + debuff * w.debuff + buff * w.buff
        + totemCover * w.totemCover + totemBase * w.totemBase + summon * w.summon + moveSetup * w.moveSetup
        + moveSafety * w.moveSafety + loot * w.loot + auraMove * w.auraMove + draw * w.draw + energy * w.energy
        + selfDamage * w.selfDamage + discard * w.discard + futureThreat * w.futureThreat
        + lootApproach * w.lootApproach + exposure * w.exposure + mitigationNeed * w.mitigationNeed
        + forecast * w.forecast + focus * w.focus + hunt * w.hunt - cost * w.costPenalty;

    public BotFeatures Clone() => (BotFeatures)MemberwiseClone();

    /// "dmg 6x1=6.0, kill 1x6=6.0, cost 1x-0.25" - every non-zero term, for the turn log.
    public string Breakdown(BotWeights w)
    {
        StringBuilder sb = new();

        void Term(string label, float value, float weight)
        {
            if (Mathf.Approximately(value, 0f) || Mathf.Approximately(weight, 0f)) { return; }

            if (sb.Length > 0) { sb.Append(", "); }

            sb.Append(label).Append(' ').Append(BotText.F(value, 1)).Append('x').Append(BotText.F(weight, 2))
              .Append('=').Append(BotText.F(value * weight, 1));
        }

        Term("dmg", damage, w.damage);
        Term("kill", kills, w.kill);
        Term("threat", threatRemoved, w.threatRemoved);
        Term("ff", friendlyFire, w.friendlyFire);
        Term("mit", mitigation, w.mitigation);
        Term("spare", mitigationSpare, w.mitigationSpare);
        Term("heal", heal, w.heal);
        Term("saved", lifeSaved, w.lifeSaved);
        Term("dot", dot, w.dot);
        Term("debuff", debuff, w.debuff);
        Term("buff", buff, w.buff);
        Term("totem", totemCover, w.totemCover);
        Term("totem+", totemBase, w.totemBase);
        Term("summon", summon, w.summon);
        Term("setup", moveSetup, w.moveSetup);
        Term("safety", moveSafety, w.moveSafety);
        Term("loot", loot, w.loot);
        Term("aura", auraMove, w.auraMove);
        Term("draw", draw, w.draw);
        Term("energy", energy, w.energy);
        Term("self", selfDamage, w.selfDamage);
        Term("discard", discard, w.discard);
        Term("future", futureThreat, w.futureThreat);
        Term("toLoot", lootApproach, w.lootApproach);
        Term("cover", exposure, w.exposure);
        Term("need", mitigationNeed, w.mitigationNeed);
        Term("forecast", forecast, w.forecast);
        Term("focus", focus, w.focus);
        Term("hunt", hunt, w.hunt);
        Term("cost", cost, -w.costPenalty);

        return sb.ToString();
    }
}

/// One legal play: this hero, this hand card, this tile, turned this many quarter turns.
public sealed class BotCandidate
{
    public Character Hero;
    public int HeroOrder;
    public int HandIndex;
    public Card Card;
    public GridTile Tile;
    public int AimTurns;
    public BotFeatures Features;
    public float Score;
    public BotCardInfo Info;

    /// What the game's previews said this play would do - measured once, scored many times by the turn
    /// search. Null on a replayed play.
    public BotMeasure Measure;

    /// Copies of this exact card (same CardKey) the hero holds - how many times a planned turn may use it.
    public int Copies = 1;

    /// Identity within one decision - what a refused play is banned by.
    public string Key => $"{HeroOrder}:{HandIndex}:{Tile.Coordinates.x},{Tile.Coordinates.y}:{AimTurns}";

    public string Describe() =>
        $"{Hero.DisplayName} {Card.cardName} -> ({Tile.Coordinates.x},{Tile.Coordinates.y})"
        + (AimTurns > 0 ? $" turned {AimTurns}" : string.Empty);
}

/// What the planner chose - a play, or ending the turn (Play null) - and why.
public sealed class BotDecision
{
    public BotCandidate Play;
    public string Reason;
    public readonly List<BotCandidate> Alternatives = new();
    public int Evaluated;
    public double PlannerMs;

    /// The weights this decision was scored with - an adaptive profile's blend, else the profile's own.
    public BotWeights Weights;

    /// 1 fully aggressive, 0 fully defensive; -1 for a profile that does not adapt.
    public float Aggression = -1f;

    /// The planned turn the play opens, for the turn log - empty for a greedy choice.
    public string Plan;

    public bool IsEndTurn => Play == null;
}

/// <summary>
/// One state-dependent thing a play does to one character - what the turn search re-scores against the
/// plays planned before it. Everything that does not depend on earlier plays is folded into
/// BotMeasure.Fixed once.
/// </summary>
public readonly struct BotOp
{
    public enum Kind { Shield, Charges, Heal, Stealth, Freeze, Weaken }

    public readonly Kind What;
    public readonly Character Who;
    public readonly int Amount;
    public readonly int PerHit;

    public BotOp(Kind what, Character who, int amount, int perHit = 0)
    {
        What = what;
        Who = who;
        Amount = amount;
        PerHit = perHit;
    }
}

/// <summary>
/// What the game's own previews say one candidate play does, measured once per decision: the hits
/// (Card.PreviewDamage), the per-ally defensive and healing ops (Card.LandedTiles), and every feature that
/// no earlier play in the same turn can change (draw, totems, pushes, moves...). The turn search scores a
/// play many times against different partial turns; only BotPlanner.Simulate's arithmetic runs again.
/// </summary>
public sealed class BotMeasure
{
    public readonly List<KeyValuePair<Character, int>> Hits = new();
    public readonly List<BotOp> Ops = new();
    public BotFeatures Fixed = new();

    /// Moves a body or fills a tile - every later preview in the turn would be measured against a board
    /// that no longer exists, so a planned turn ends with a play like this.
    public bool ChangesBoard;

    /// Energy the play gives back to whoever played it.
    public int EnergyGain;
}

/// <summary>
/// A partial turn: what the plays planned so far have used up and achieved. Small dictionaries copied per
/// branch - a turn is a handful of plays over a dozen characters.
/// </summary>
public sealed class BotTurnState
{
    public readonly Dictionary<Character, int> DamageTaken = new();
    public readonly HashSet<Character> Neutralised = new();

    /// Enemies a planned play has already weakened - a second Weaken cuts no deeper (WeakenStatus).
    public readonly HashSet<Character> Weakened = new();
    public readonly Dictionary<Character, int> Prevented = new();
    public readonly Dictionary<Character, int> Healed = new();
    public readonly Dictionary<Character, int> Energy = new();
    public readonly Dictionary<string, int> Used = new();
    public readonly List<BotCandidate> Plays = new();
    public float Total;
    public bool Closed;

    public static int Of<T>(Dictionary<T, int> map, T key) => map.TryGetValue(key, out int v) ? v : 0;

    public BotTurnState Copy()
    {
        BotTurnState copy = new() { Total = Total, Closed = Closed };

        foreach (var pair in DamageTaken) { copy.DamageTaken[pair.Key] = pair.Value; }
        foreach (Character c in Neutralised) { copy.Neutralised.Add(c); }
        foreach (Character c in Weakened) { copy.Weakened.Add(c); }
        foreach (var pair in Prevented) { copy.Prevented[pair.Key] = pair.Value; }
        foreach (var pair in Healed) { copy.Healed[pair.Key] = pair.Value; }
        foreach (var pair in Energy) { copy.Energy[pair.Key] = pair.Value; }
        foreach (var pair in Used) { copy.Used[pair.Key] = pair.Value; }

        copy.Plays.AddRange(Plays);

        return copy;
    }

    /// Order-free identity: two partial turns playing the same plays in a different order are one.
    public string Signature()
    {
        List<string> keys = new();

        foreach (BotCandidate play in Plays) { keys.Add(play.Key); }

        keys.Sort(StringComparer.Ordinal);

        return string.Join("|", keys);
    }
}

/// <summary>
/// How aggressive an adaptive profile is right now, 0..1: fully aggressive for the first part of the
/// battle (every kill then saves damage for many rounds, and an emptier board is what the waves arrive
/// onto), sliding to fully defensive towards the end (a kill saves little, a fall costs max health for the
/// rest of the run). A party low on health or buried under a crowd turns defensive sooner.
/// </summary>
public static class BotAggression
{
    public static float Of(BotBoardModel model, BotProfileData profile)
    {
        float p = model.Progress;
        float a;

        if (p <= profile.aggressiveUntil) { a = 1f; }
        else if (p >= profile.defensiveFrom) { a = 0f; }
        else { a = 1f - (p - profile.aggressiveUntil) / Mathf.Max(0.01f, profile.defensiveFrom - profile.aggressiveUntil); }

        float health = model.PartyHealth;

        if (profile.lowHpGuard > 0f && health < profile.lowHpGuard) { a *= health / profile.lowHpGuard; }

        if (profile.crowdGuard > 0f && model.Heroes.Count > 0)
        {
            float perHero = (float)model.Enemies.Count / model.Heroes.Count;

            if (perHero > profile.crowdGuard) { a *= profile.crowdGuard / perHero; }
        }

        return Mathf.Clamp(a, Mathf.Clamp01(profile.minAggression), 1f);
    }

    /// The weights to score this decision with: the blend for an adaptive profile, else its own, with the
    /// loot rows scaled down once the deck-building levels are past.
    public static BotWeights Weights(BotBoardModel model, BotProfileData profile, out float aggression)
    {
        aggression = -1f;

        BotWeights w;

        if (profile.adaptive && profile.lateWeights != null)
        {
            aggression = Of(model, profile);
            w = BotWeights.Lerp(profile.lateWeights, profile.weights, aggression);
        }
        else
        {
            w = profile.weights.Clone();
        }

        float scale = profile.rewards != null ? profile.rewards.LootScale(model.Level) : 1f;
        w.loot *= scale;
        w.lootApproach *= scale;

        return w;
    }
}

/// <summary>
/// The balance bot's in-battle brain. Every legal play for every hero (hand card x tile in range x
/// facing) is measured once with the game's own previews (BotMeasure), then either
///   - greedy (searchDepth 0, every original profile): each scored on its own as Σ weight × feature, and
///     the best one taken; or
///   - turn search (searchDepth > 0): a beam search over sequences of plays, each scored against what the
///     plays before it already did - damage already dealt, attackers already dead, incoming already
///     blocked, energy and cards already spent - and the first play of the best turn taken.
/// Either way the turn ends when nothing reaches the profile's pass threshold, and the pilot re-plans
/// after every play against the real board, so a plan is only ever a way to choose its first step.
///
/// Legality is never re-derived here: candidates come from GridManager.GetTilesInRange filtered by
/// Card.PlayRefusal and Card.Refusal, exactly as ShowPlayableTiles builds the highlight and
/// EnemyBrain.TryFindAttack builds an enemy's options, so the bot can only choose plays a click could
/// make. Nothing here spends a charge or rolls a die - see the determinism notes in BotPilot.
/// </summary>
public static class BotPlanner
{
    /// Plays whose move lookahead (the best follow-up attack from the destination) is worked out; the
    /// rest of the move candidates keep their cheap score.
    private const int MoveLookaheadCount = 4;

    /// The same, for a profile weighting Hunt - a move towards a dangerous enemy is rarely among the four
    /// best-scoring moves before it is credited for that.
    private const int HuntLookaheadCount = 12;

    private static readonly HashSet<string> warnedEffects = new();

    /// What one decision scores with - the blended weights and how many rounds a kill is credited for.
    private readonly struct Scoring
    {
        public readonly BotWeights W;
        public readonly int FutureRounds;

        /// Ally -> how much a point of damage stopped on them is worth this decision - see Needs.
        public readonly Dictionary<Character, float> Need;

        /// Whether Poison counts - BotProfileData.countPoison.
        public readonly bool Poison;

        /// Whether on-hit buffs count as what they do - BotProfileData.countOnHit.
        public readonly bool OnHit;

        /// Whether Weaken is measured as the cut it is - BotProfileData.exactWeaken.
        public readonly bool Weaken;

        public Scoring(BotWeights w, int futureRounds, Dictionary<Character, float> need, bool poison, bool onHit, bool weaken)
        {
            Weaken = weaken;
            W = w;
            FutureRounds = futureRounds;
            Need = need;
            Poison = poison;
            OnHit = onHit;
        }

        public float NeedOf(Character ally) => Need != null && ally != null && Need.TryGetValue(ally, out float n) ? n : 1f;
    }

    /// <summary>
    /// How badly each of ours needs the damage coming at them stopped: this round's incoming, as if it kept
    /// coming every round left, over the health they have - between the profile's floor and cap. Health
    /// is not carried between levels, so damage that never drops anyone costs nothing; what a hero who can
    /// soak the rest of the battle standing needs is the enemies dead, not another shield.
    /// </summary>
    private static Dictionary<Character, float> Needs(BotBoardModel model, BotProfileData profile)
    {
        Dictionary<Character, float> need = new();
        int rounds = Mathf.Max(1, model.TurnsRemaining);
        float floor = Mathf.Clamp01(profile.needFloor);
        float cap = Mathf.Max(floor, profile.needCap);

        foreach (Character ally in model.OurSide)
        {
            float projected = (float)model.IncomingOn(ally) * rounds;

            if (profile.countPoison) { projected += model.PoisonAhead(ally); }

            need[ally] = Mathf.Clamp(projected / Mathf.Max(1, ally.Health), floor, cap);
        }

        return need;
    }

    public static BotDecision Decide(
        BotBoardModel model, BotProfileData profile, System.Random rng, ICollection<string> banned)
    {
        Stopwatch timer = Stopwatch.StartNew();
        BotDecision decision = new();
        BotWeights weights = BotAggression.Weights(model, profile, out float aggression);
        Scoring scoring = new(weights, Mathf.Min(Mathf.Max(0, model.TurnsRemaining - 1), Mathf.Max(0, profile.futureRoundsCap)),
                              Needs(model, profile), profile.countPoison, profile.countOnHit, profile.exactWeaken);

        decision.Weights = weights;
        decision.Aggression = aggression;

        List<BotCandidate> candidates = Enumerate(model, scoring, banned);
        decision.Evaluated = candidates.Count;

        if (candidates.Count == 0)
        {
            decision.Reason = "no legal play";
        }
        else if (profile.randomness > 0f && rng.NextDouble() < profile.randomness)
        {
            // Enumeration order is already deterministic (party order, hand order, board order), and
            // the draw comes from the per-decision System.Random, never UnityEngine.Random.
            if (rng.NextDouble() < profile.endTurnChance)
            {
                decision.Reason = "random: end turn";
            }
            else
            {
                decision.Play = candidates[rng.Next(candidates.Count)];
                decision.Reason = "random";
            }
        }
        else
        {
            candidates.Sort(Compare);
            MoveLookahead(model, scoring, candidates);

            if (!Mathf.Approximately(weights.forecast, 0f)) { Forecast(model, profile, scoring, candidates); }

            candidates.Sort(Compare);

            decision.Play = PlayFirst(profile, candidates);

            if (decision.Play != null)
            {
                decision.Reason = $"play first: {decision.Play.Info.Primary}";
            }
            else if (profile.searchDepth > 0)
            {
                Search(model, profile, scoring, candidates, decision);
            }
            else if (candidates[0].Score >= profile.passThreshold)
            {
                decision.Play = candidates[0];
                decision.Reason = "best score";
            }
            else
            {
                decision.Reason = $"best play scores {BotText.F(candidates[0].Score, 1)}, under the pass threshold";
            }

            foreach (BotCandidate candidate in candidates)
            {
                if (decision.Alternatives.Count >= 3) { break; }
                if (candidate != decision.Play) { decision.Alternatives.Add(candidate); }
            }
        }

        if (aggression >= 0f) { decision.Reason += $" [aggr {BotText.F(aggression, 2)}]"; }

        timer.Stop();
        decision.PlannerMs = timer.Elapsed.TotalMilliseconds;

        return decision;
    }

    private static BotCandidate PlayFirst(BotProfileData profile, List<BotCandidate> sorted)
    {
        foreach (BotPlayCategory category in profile.playFirst)
        {
            foreach (BotCandidate candidate in sorted)
            {
                if (candidate.Info.Has(category) && candidate.Score >= profile.playFirstMinScore) { return candidate; }
            }
        }

        return null;
    }

    /// Highest score first; ties broken by cost, party order, hand order, board order and facing, so the
    /// same board always produces the same choice.
    private static int Compare(BotCandidate a, BotCandidate b)
    {
        int order = b.Score.CompareTo(a.Score);
        if (order != 0) { return order; }

        order = a.Card.cost.CompareTo(b.Card.cost);
        if (order != 0) { return order; }

        order = a.HeroOrder.CompareTo(b.HeroOrder);
        if (order != 0) { return order; }

        order = a.HandIndex.CompareTo(b.HandIndex);
        if (order != 0) { return order; }

        Vector2Int ca = a.Tile.Coordinates;
        Vector2Int cb = b.Tile.Coordinates;

        if (ca != cb) { return GridManager.IsEarlier(ca, cb) ? -1 : 1; }

        return a.AimTurns.CompareTo(b.AimTurns);
    }

    // ---------------------------------------------------------------------------------------------
    // Turn search

    /// <summary>
    /// Beam search over this turn's plays. Each kept partial turn is extended by its `branching` best next
    /// plays (scored against what it has already done); the `beamWidth` best partial turns by total score
    /// survive to the next depth, permutations of the same plays merged. A partial turn may also stop
    /// where it is - ending the turn is always a choice. The first play of the best turn found is taken,
    /// provided that turn is worth the pass threshold at all.
    /// </summary>
    private static void Search(
        BotBoardModel model, BotProfileData profile, Scoring scoring, List<BotCandidate> candidates, BotDecision decision)
    {
        BotTurnState root = new();

        foreach (Character hero in model.Heroes) { root.Energy[hero] = hero.Energy; }

        List<BotTurnState> beam = new() { root };
        BotTurnState best = root;
        int width = Mathf.Max(1, profile.beamWidth);
        int branching = Mathf.Max(1, profile.branching);

        for (int depth = 0; depth < profile.searchDepth && beam.Count > 0; depth++)
        {
            Dictionary<string, BotTurnState> next = new();

            foreach (BotTurnState state in beam)
            {
                if (state.Closed) { continue; }

                List<(BotCandidate play, float score)> options = new();

                foreach (BotCandidate candidate in candidates)
                {
                    if (!Affordable(state, candidate)) { continue; }

                    // At depth 0 a play's score is exactly its greedy score (lookahead included); deeper,
                    // it is re-scored against the plan so far.
                    float score = depth == 0
                        ? candidate.Score
                        : Simulate(model, scoring, candidate, state, null).Score(scoring.W);

                    if (score > 0f) { options.Add((candidate, score)); }
                }

                options.Sort((a, b) => b.score.CompareTo(a.score));

                for (int i = 0; i < options.Count && i < branching; i++)
                {
                    BotTurnState child = state.Copy();
                    Simulate(model, scoring, options[i].play, state, child);
                    child.Total += options[i].score;
                    child.Plays.Add(options[i].play);

                    string signature = child.Signature();

                    // Of two orders of the same plays, keep the better-scoring one; on a tie the one whose
                    // first play scores higher on its own - the order greedy would have taken.
                    if (next.TryGetValue(signature, out BotTurnState seen)
                        && (seen.Total > child.Total + 0.001f
                            || (Mathf.Abs(seen.Total - child.Total) <= 0.001f && seen.Plays[0].Score >= child.Plays[0].Score)))
                    {
                        continue;
                    }

                    next[signature] = child;
                }
            }

            List<BotTurnState> ranked = new(next.Values);
            ranked.Sort((a, b) => b.Total.CompareTo(a.Total));

            if (ranked.Count > width) { ranked.RemoveRange(width, ranked.Count - width); }

            foreach (BotTurnState state in ranked)
            {
                if (state.Total > best.Total + 0.001f) { best = state; }
            }

            beam = ranked;
        }

        if (best.Plays.Count == 0 || best.Total < profile.passThreshold)
        {
            decision.Reason = $"best turn scores {BotText.F(best.Total, 1)}, under the pass threshold";
            return;
        }

        decision.Play = best.Plays[0];

        List<string> steps = new();

        foreach (BotCandidate play in best.Plays) { steps.Add($"{play.Hero.DisplayName} {play.Card.cardName}"); }

        decision.Plan = string.Join(" > ", steps);
        decision.Reason = $"best turn {BotText.F(best.Total, 1)}: {decision.Plan}";
    }

    private static bool Affordable(BotTurnState state, BotCandidate candidate)
    {
        if (BotTurnState.Of(state.Used, UseKey(candidate)) >= candidate.Copies) { return false; }

        int energy = state.Energy.TryGetValue(candidate.Hero, out int e) ? e : candidate.Hero.Energy;

        return candidate.Card.cost <= energy;
    }

    private static string UseKey(BotCandidate candidate) => $"{candidate.HeroOrder}|{CardKey(candidate.Card)}";

    // ---------------------------------------------------------------------------------------------
    // Enumeration and measurement

    private static List<BotCandidate> Enumerate(BotBoardModel model, Scoring scoring, ICollection<string> banned)
    {
        List<BotCandidate> candidates = new();
        GridManager grid = GridManager.Instance;

        if (grid == null) { return candidates; }

        BotTurnState empty = new();

        foreach (Character hero in model.Heroes)
        {
            if (!hero.CanAct) { continue; }

            int heroOrder = model.HeroOrder(hero);
            Dictionary<string, int> copies = new();

            foreach (Card held in hero.Hand)
            {
                if (held == null) { continue; }

                string key = CardKey(held);
                copies[key] = BotTurnState.Of(copies, key) + 1;
            }

            HashSet<string> seenCards = new();

            for (int handIndex = 0; handIndex < hero.Hand.Count; handIndex++)
            {
                Card card = hero.Hand[handIndex];

                if (card == null || card.PlayRefusal(hero) != null) { continue; }

                // Two copies in the same state make the same plays - evaluate the first one only. The turn
                // search still knows how many there are (Copies), so it can plan to play both.
                if (!seenCards.Add(CardKey(card))) { continue; }

                int facings = card.CanRotateAim ? 4 : 1;

                foreach (GridTile tile in grid.GetTilesInRange(hero.Tile, card.range))
                {
                    card.ResetAim();

                    for (int turns = 0; turns < facings; turns++)
                    {
                        if (turns > 0) { card.RotateAim(hero, tile); }

                        // Asked per facing: a push can have room one way round and not another.
                        if (card.Refusal(hero, tile) != null) { continue; }

                        BotCandidate candidate = new()
                        {
                            Hero = hero,
                            HeroOrder = heroOrder,
                            HandIndex = handIndex,
                            Card = card,
                            Tile = tile,
                            AimTurns = turns,
                            Copies = BotTurnState.Of(copies, CardKey(card)),
                        };

                        if (banned != null && banned.Contains(candidate.Key)) { continue; }

                        candidate.Measure = Measure(model, scoring, candidate);
                        candidate.Features = Simulate(model, scoring, candidate, empty, null);
                        candidate.Score = candidate.Features.Score(scoring.W);
                        candidate.Info = BotCardInfo.Of(card);
                        candidates.Add(candidate);
                    }

                    card.ResetAim();
                }
            }
        }

        return candidates;
    }

    private static string CardKey(Card card) =>
        $"{(card.Data != null ? card.Data.name : card.cardName)}|{card.cost}|{card.LockedTurns}|{card.EntryCount}"
        + $"|{card.range.Shape}|{card.range.MinDistance}|{card.range.MaxDistance}";

    /// Everything the game's previews say about one play, with the aim already set on the card.
    private static BotMeasure Measure(BotBoardModel model, Scoring scoring, BotCandidate candidate)
    {
        Character hero = candidate.Hero;
        Card card = candidate.Card;
        GridTile tile = candidate.Tile;
        BotMeasure m = new();
        BotFeatures f = m.Fixed;

        foreach (KeyValuePair<Character, int> hit in card.PreviewDamage(hero, tile))
        {
            if (hit.Key != null && hit.Value > 0) { m.Hits.Add(hit); }
        }

        if (scoring.OnHit) { OnHitRiders(model, m, hero); }

        bool moves = false;
        bool pushed = false;

        for (int e = 0; e < card.EntryCount; e++)
        {
            CardEffectEntry entry = card.GetEntry(e);
            CardEffect effect = entry.effect;

            if (effect == null || effect is DamageEffect || effect is AnimateEffect) { continue; }

            // A conditional entry whose condition fails is skipped here exactly as Card.ResolveEffects
            // skips it - a Draw reads only the caster, so an empty landed list alone would still count it.
            if (!card.EntryLive(entry, hero)) { continue; }

            List<GridTile> landed = card.LandedTiles(hero, tile, e);
            ActionContext ctx = new(card, hero, landed, tile, card.EntryDelta(entry, hero), entry.amountPercent);

            switch (effect)
            {
                case ShieldEffect shield:
                    foreach (Character ally in Occupants(landed, ours: true))
                    {
                        m.Ops.Add(new BotOp(BotOp.Kind.Shield, ally, ctx.Amount(shield.ShieldAmount)));
                    }

                    break;
                case BlockEffect block:
                    foreach (Character ally in Occupants(landed, ours: true))
                    {
                        m.Ops.Add(new BotOp(BotOp.Kind.Charges, ally, ctx.Amount(block.BlockCount), BlockStatus.PerHitFor(ally)));
                    }

                    break;
                case ParryEffect parry:
                    foreach (Character ally in Occupants(landed, ours: true))
                    {
                        m.Ops.Add(new BotOp(BotOp.Kind.Charges, ally, ctx.Amount(parry.ParryCharges), int.MaxValue));
                    }

                    break;
                case HealEffect heal:
                    foreach (Character ally in Occupants(landed, ours: true))
                    {
                        m.Ops.Add(new BotOp(BotOp.Kind.Heal, ally, ctx.Amount(heal.RiderAmount)));
                    }

                    break;
                case CleansePoisonEffect:
                    foreach (Character ally in Occupants(landed, ours: true))
                    {
                        int poison = ally.StatusStacks(StatusType.Poison);
                        f.heal += Mathf.Min(40, poison * (poison + 1) / 2);
                    }

                    break;
                case CleanseEffect:
                    // Poison counts as above (the ticks it will no longer deal); every other curse as a
                    // buff stack taken off; and the heal for the total, up to the health missing.
                    foreach (Character ally in Occupants(landed, ours: true))
                    {
                        int poison = ally.StatusStacks(StatusType.Poison);
                        int total = 0;

                        foreach (StatusType curse in CleanseEffect.Curses) { total += ally.StatusStacks(curse); }

                        f.heal += Mathf.Min(40, poison * (poison + 1) / 2);
                        f.heal += Mathf.Min(ally.MaxHealth - ally.Health, total);
                        f.buff += total - poison;
                    }

                    break;
                case ApplyStatusEffect status:
                    foreach (Character target in Occupants(landed, ours: null))
                    {
                        StatusMeasure(model, scoring, m, target, status.Status, ctx.Amount(status.RiderAmount));
                    }

                    break;
                case TauntEffect:
                    // The taunted enemy swings at the taunter instead of whoever it meant to - half credit
                    // for what it had aimed at everyone else, since the taunter still takes it.
                    foreach (Character enemy in Occupants(landed, ours: false))
                    {
                        f.mitigation += 0.5f * (model.ThreatOf(enemy) - model.ThreatOf(enemy, hero));
                    }

                    break;
                case SummonEffect summon:
                    Summon(model, f, summon.SummonedObject, tile);
                    m.ChangesBoard = true;
                    break;
                case MoveEffect:
                    moves = true;
                    m.ChangesBoard = true;
                    break;
                case DrawEffect draw:
                    f.draw += ctx.Amount(draw.RiderAmount);
                    break;
                case EnergyEffect energy:
                    f.energy += WantsEnergy(hero, card) ? energy.Amount : energy.Amount * 0.2f;
                    m.EnergyGain += energy.Amount;
                    break;
                case SelfDamageEffect self:
                    f.selfDamage += self.Amount;
                    break;
                case DiscardEffect:
                    f.discard += 1f;
                    break;
                case PushEffect:
                    if (!pushed)
                    {
                        pushed = true;
                        Push(model, f, hero, card, tile);
                    }

                    m.ChangesBoard = true;
                    break;
                case ApplyTileEffect tileEffect:
                    if (tileEffect.Effect == TileEffectType.WallOfFlames)
                    {
                        int burn = ctx.Amount(tileEffect.Magnitude) * Mathf.Max(1, tileEffect.Turns);

                        foreach (Character enemy in Occupants(landed, ours: false))
                        {
                            f.dot += Mathf.Min(enemy.Health, burn);
                        }
                    }
                    else
                    {
                        f.buff += 0.5f * landed.Count;
                    }

                    break;
                case SwapEffect:
                    m.ChangesBoard = true;
                    break;
                default:
                    WarnUnknown(effect);
                    break;
            }
        }

        if (moves && tile.Occupant == null && hero.Tile != null && tile != hero.Tile)
        {
            MoveFeatures(model, f, hero, tile);
        }

        f.cost = card.cost;

        return m;
    }

    /// <summary>
    /// What `candidate` adds on top of the partial turn `before`: damage capped by what each enemy has
    /// left after earlier hits, kills only of the living, shields and blocks counted only against
    /// incoming nobody has stopped or killed yet, heals only into missing health. With `into` set, the
    /// play is also written into that state - the branch the search keeps.
    ///
    /// Against an empty state this is the one-play score the greedy profiles use.
    /// </summary>
    private static BotFeatures Simulate(
        BotBoardModel model, Scoring scoring, BotCandidate candidate, BotTurnState before, BotTurnState into)
    {
        BotMeasure m = candidate.Measure;
        BotFeatures f = m.Fixed.Clone();
        Dictionary<Character, int> prevented = new();
        Dictionary<Character, int> healed = new();
        List<Character> stopped = new();
        List<Character> weakened = new();

        foreach (KeyValuePair<Character, int> hit in m.Hits)
        {
            Character victim = hit.Key;
            int left = Mathf.Max(0, victim.Health - BotTurnState.Of(before.DamageTaken, victim));

            if (left <= 0) { continue; }

            int lost = Mathf.Min(hit.Value, left);

            switch (BotAssets.SideOf(victim))
            {
                case BotSide.Enemy:
                    f.damage += lost;

                    // The share of this enemy the hit takes off, times what it deals per round.
                    f.focus += Danger(model, scoring, victim) * lost / left;

                    if (hit.Value >= left)
                    {
                        f.kills += 1f;

                        if (!before.Neutralised.Contains(victim))
                        {
                            f.threatRemoved += model.ThreatOf(victim);

                            if (scoring.Poison) { f.threatRemoved += model.PoisonThreatOf(victim); }

                            stopped.Add(victim);
                        }

                        f.futureThreat += model.PerRoundOf(victim) * scoring.FutureRounds;

                        if (scoring.Poison) { f.futureThreat += model.PoisonPerRoundOf(victim) * scoring.FutureRounds; }
                    }

                    if (into != null) { into.DamageTaken[victim] = BotTurnState.Of(into.DamageTaken, victim) + lost; }

                    break;
                case BotSide.Hero:
                case BotSide.Ally:
                    f.friendlyFire += lost;
                    break;
            }
        }

        // What is still coming at an ally after the plan so far and this play's kills and blocks.
        int Removed(Character ally, bool includeThisPlay)
        {
            int removed = 0;

            foreach (Character enemy in before.Neutralised) { removed += model.ThreatOf(enemy, ally); }

            if (includeThisPlay)
            {
                foreach (Character enemy in stopped) { removed += model.ThreatOf(enemy, ally); }
            }

            return removed;
        }

        int Remaining(Character ally) =>
            Mathf.Max(0, model.IncomingOn(ally) - Removed(ally, true) - BotTurnState.Of(before.Prevented, ally)
                         - BotTurnState.Of(prevented, ally));

        foreach (BotOp op in m.Ops)
        {
            Character who = op.Who;

            switch (op.What)
            {
                case BotOp.Kind.Shield:
                {
                    if (op.Amount <= 0) { break; }

                    int useful = Mathf.Min(op.Amount, Remaining(who));
                    f.mitigation += useful;
                    f.mitigationNeed += useful * scoring.NeedOf(who);
                    f.mitigationSpare += op.Amount - useful;
                    prevented[who] = BotTurnState.Of(prevented, who) + useful;
                    break;
                }
                case BotOp.Kind.Charges:
                {
                    if (op.Amount <= 0) { break; }

                    // Each charge meets one hit, biggest first, and stops up to PerHit of it.
                    model.Hits.TryGetValue(who, out List<int> hits);

                    int covered = hits != null ? Mathf.Min(op.Amount, hits.Count) : 0;
                    int blocked = 0;

                    for (int i = 0; i < covered; i++) { blocked += Mathf.Min(op.PerHit, hits[i]); }

                    blocked = Mathf.Min(blocked, Remaining(who));

                    f.mitigation += blocked;
                    f.mitigationNeed += blocked * scoring.NeedOf(who);
                    f.mitigationSpare += (op.Amount - covered) * Mathf.Min(op.PerHit, 5);
                    prevented[who] = BotTurnState.Of(prevented, who) + blocked;
                    break;
                }
                case BotOp.Kind.Stealth:
                {
                    int hidden = Remaining(who);
                    f.mitigation += hidden;
                    f.mitigationNeed += hidden * scoring.NeedOf(who);
                    f.mitigationSpare += op.Amount;
                    prevented[who] = BotTurnState.Of(prevented, who) + hidden;
                    break;
                }
                case BotOp.Kind.Heal:
                {
                    int missing = who.MaxHealth - who.Health - BotTurnState.Of(before.Healed, who)
                                  - BotTurnState.Of(healed, who);
                    int amount = Mathf.Min(op.Amount, missing);

                    if (amount <= 0) { break; }

                    float pressure = Remaining(who) + (scoring.Poison ? model.PoisonAhead(who) : 0);
                    float urgency = Mathf.Min(3f, 1f + pressure / Mathf.Max(1f, who.Health));
                    f.heal += amount * urgency;
                    healed[who] = BotTurnState.Of(healed, who) + amount;
                    break;
                }
                case BotOp.Kind.Weaken:
                {
                    // Each of the enemy's attack steps this round loses PerHit, never below nothing - taken
                    // off what is coming at each of its victims in proportion, so a Weaken that keeps an
                    // ally standing counts through Life Saved like a shield would. The turns after this
                    // one are credited at half, as threat it will not deal.
                    if (before.Neutralised.Contains(who) || stopped.Contains(who) || before.Weakened.Contains(who)
                        || weakened.Contains(who))
                    {
                        break;
                    }

                    weakened.Add(who);

                    int threat = model.ThreatOf(who);
                    int steps = model.AttackSteps.TryGetValue(who, out int n) ? n : 0;
                    // Already weakened: the preview it attacks with has the cut in it - only the extra turns count.
                    int cut = who.StatusStacks(StatusType.Weaken) > 0 ? 0 : Mathf.Min(threat, op.PerHit * steps);

                    if (cut > 0 && model.ThreatOn.TryGetValue(who, out Dictionary<Character, int> victims))
                    {
                        foreach (KeyValuePair<Character, int> victim in victims)
                        {
                            int share = Mathf.Min(victim.Value, Mathf.CeilToInt((float)cut * victim.Value / threat));
                            int useful = Mathf.Min(share, Remaining(victim.Key));

                            f.mitigationNeed += useful * scoring.NeedOf(victim.Key);
                            prevented[victim.Key] = BotTurnState.Of(prevented, victim.Key) + useful;
                        }
                    }

                    int later = Mathf.Min(op.Amount - 1, model.TurnsRemaining - 1);

                    f.threatRemoved += cut + 0.5f * Mathf.Max(0, later) * Mathf.Min(model.PerRoundOf(who), op.PerHit);
                    f.debuff += 0.5f * op.Amount;
                    break;
                }
                case BotOp.Kind.Freeze:
                {
                    if (before.Neutralised.Contains(who) || stopped.Contains(who)) { break; }

                    f.threatRemoved += model.ThreatOf(who);

                    if (scoring.Poison) { f.threatRemoved += model.PoisonThreatOf(who); }

                    f.debuff += op.Amount;
                    stopped.Add(who);
                    break;
                }
            }
        }

        // An ally this round's intents would drop, who this play keeps standing - by shielding, healing,
        // or killing or freezing whoever was going to do it.
        foreach (Character ally in model.OurSide)
        {
            int health = ally.Health + BotTurnState.Of(before.Healed, ally)
                         - (scoring.Poison ? model.PoisonNowOn(ally) : 0);
            int incomingBefore = model.IncomingOn(ally) - Removed(ally, false) - BotTurnState.Of(before.Prevented, ally);

            if (incomingBefore < health) { continue; }

            if (Remaining(ally) < health + BotTurnState.Of(healed, ally))
            {
                f.lifeSaved += ally.IsPlayerControlled ? 1f : 0.5f;
            }
        }

        if (into != null)
        {
            foreach (Character enemy in stopped) { into.Neutralised.Add(enemy); }
            foreach (Character enemy in weakened) { into.Weakened.Add(enemy); }
            foreach (var pair in prevented) { into.Prevented[pair.Key] = BotTurnState.Of(into.Prevented, pair.Key) + pair.Value; }
            foreach (var pair in healed) { into.Healed[pair.Key] = BotTurnState.Of(into.Healed, pair.Key) + pair.Value; }

            Character hero = candidate.Hero;
            int energy = into.Energy.TryGetValue(hero, out int e) ? e : hero.Energy;
            into.Energy[hero] = energy - candidate.Card.cost + m.EnergyGain;

            string used = UseKey(candidate);
            into.Used[used] = BotTurnState.Of(into.Used, used) + 1;

            if (m.ChangesBoard) { into.Closed = true; }
        }

        return f;
    }

    /// The characters standing on `tiles`: ours (true), theirs (false) or anyone (null).
    private static IEnumerable<Character> Occupants(List<GridTile> tiles, bool? ours)
    {
        foreach (GridTile tile in tiles)
        {
            Character occupant = tile != null ? tile.Occupant : null;

            if (!BotAssets.IsLiving(occupant)) { continue; }

            if (ours == true && !BotAssets.IsOurSide(occupant)) { continue; }
            if (ours == false && BotAssets.SideOf(occupant) != BotSide.Enemy) { continue; }

            yield return occupant;
        }
    }

    private static void StatusMeasure(
        BotBoardModel model, Scoring scoring, BotMeasure m, Character target, StatusType type, int stacks)
    {
        if (stacks <= 0) { return; }

        BotFeatures f = m.Fixed;
        bool enemy = BotAssets.SideOf(target) == BotSide.Enemy;

        switch (type)
        {
            case StatusType.Poison:
                if (enemy) { f.dot += Mathf.Min(target.Health, stacks * (stacks + 1) / 2); }
                else { f.friendlyFire += stacks; }
                return;
            case StatusType.Frozen:
                if (enemy) { m.Ops.Add(new BotOp(BotOp.Kind.Freeze, target, stacks)); }
                return;
            case StatusType.Weaken when scoring.Weaken:
                if (enemy) { m.Ops.Add(new BotOp(BotOp.Kind.Weaken, target, stacks, WeakenStatus.BaseAmount)); }
                return;
            case StatusType.Weaken:
                if (enemy)
                {
                    int steps = model.AttackSteps.TryGetValue(target, out int n) ? n : 0;
                    f.threatRemoved += Mathf.Min(stacks * steps, model.ThreatOf(target));
                    f.debuff += 0.5f * stacks;
                }

                return;
            case StatusType.Stealth:
                if (!enemy) { m.Ops.Add(new BotOp(BotOp.Kind.Stealth, target, stacks)); }
                return;
            case StatusType.Dodge:
            case StatusType.Parry:
                if (!enemy) { m.Ops.Add(new BotOp(BotOp.Kind.Charges, target, stacks, int.MaxValue)); }
                return;
            case StatusType.Block:
                if (!enemy) { m.Ops.Add(new BotOp(BotOp.Kind.Charges, target, stacks, BlockStatus.PerHitFor(target))); }
                return;
            case StatusType.Shield:
                if (!enemy) { m.Ops.Add(new BotOp(BotOp.Kind.Shield, target, stacks)); }
                return;
            case StatusType.Regeneration:
                if (!enemy) { f.heal += Mathf.Min(target.MaxHealth, stacks * (stacks + 1) / 2) * 0.5f; }
                return;
            case StatusType.Lifesteal when scoring.OnHit && !enemy:
            {
                // Half of what the carrier will hit for, rounded up, over the turns it lasts - worth only
                // what fits into the health they are missing now plus what is coming at them meanwhile.
                int turns = Mathf.Min(stacks, Mathf.Max(1, model.TurnsRemaining));
                float returned = Mathf.Ceil(DamagePerTurn(target) / 2f) * turns;
                float room = target.MaxHealth - target.Health + model.IncomingOn(target) * turns
                             + (scoring.Poison ? model.PoisonAhead(target) : 0);
                f.heal += Mathf.Min(returned, room);
                return;
            }
            case StatusType.PoisonBlade when scoring.OnHit && !enemy:
            {
                // One Poison per landed hit; a stack applied with t ticks left is worth about min(t, 3).
                int turns = Mathf.Min(stacks, Mathf.Max(1, model.TurnsRemaining));
                f.dot += HitsPerTurn(target) * turns * Mathf.Min(3, model.FutureTicks);
                return;
            }
        }

        if (enemy) { f.debuff += stacks; }
        else { f.buff += stacks; }
    }

    /// <summary>
    /// A totem is worth what its aura reaches. Each covered character counts by side: an ally under an
    /// allies' aura counts 1 for a hero and 0.5 for a summon; an enemy under an enemies' aura counts by
    /// how dangerous it is (its per-round damage / 8, between 0.25 and 1.5), so a ring of pebbles is not
    /// worth a shield; an aura reaching everyone is what it gives us minus what it gives them. Capped at
    /// 4 - a totem has a few health and rarely lasts long in a crowd.
    /// </summary>
    private static void Summon(BotBoardModel model, BotFeatures f, GameObject prefab, GridTile tile)
    {
        if (prefab == null) { return; }

        if (prefab.TryGetComponent(out Totem totem))
        {
            f.totemBase += 1f;

            float ours = 0f;
            float theirs = 0f;

            foreach (Character ally in model.OurSide)
            {
                if (totem.Range.Contains(tile.Coordinates, ally.Tile.Coordinates)) { ours += ally.IsPlayerControlled ? 1f : 0.5f; }
            }

            foreach (Character enemy in model.Enemies)
            {
                if (totem.Range.Contains(tile.Coordinates, enemy.Tile.Coordinates))
                {
                    theirs += Mathf.Clamp(model.PerRoundOf(enemy) / 8f, 0.25f, 1.5f);
                }
            }

            float cover = totem.Affects switch
            {
                AuraAudience.Enemies => theirs,
                AuraAudience.Everyone => ours - theirs,
                _ => ours,
            };

            f.totemCover += Mathf.Min(4f, cover);
            return;
        }

        f.summon += prefab.TryGetComponent(out Character body) ? body.MaxHealth : 3;
    }

    /// Shoving an attacker out of reach of the ally it meant to hit cancels (roughly) half its threat -
    /// half, because it re-decides when it acts and may find someone else.
    private static void Push(BotBoardModel model, BotFeatures f, Character hero, Card card, GridTile tile)
    {
        foreach ((Character mover, GridTile destination) in card.PreviewPush(hero, tile))
        {
            if (mover == null || destination == null || BotAssets.SideOf(mover) != BotSide.Enemy) { continue; }

            int threat = model.ThreatOf(mover);

            if (threat <= 0) { continue; }

            bool stillReaches = false;

            foreach (Character ally in model.OurSide)
            {
                if (model.ThreatOf(mover, ally) <= 0) { continue; }

                if (BotBoardModel.Distance(destination.Coordinates, ally.Tile.Coordinates) <= model.ReachOf(mover))
                {
                    stillReaches = true;
                    break;
                }
            }

            if (!stillReaches) { f.threatRemoved += 0.5f * threat; }
        }
    }

    private static void MoveFeatures(BotBoardModel model, BotFeatures f, Character hero, GridTile destination)
    {
        Vector2Int from = hero.Tile.Coordinates;
        Vector2Int to = destination.Coordinates;

        foreach (Character enemy in model.Enemies)
        {
            int onHero = model.ThreatOf(enemy, hero);
            int reach = model.ReachOf(enemy);
            int before = BotBoardModel.Distance(enemy.Tile.Coordinates, from);
            int after = BotBoardModel.Distance(enemy.Tile.Coordinates, to);

            if (onHero > 0 && after > reach) { f.moveSafety += onHero; }
            else if (onHero == 0 && before > reach && after <= reach) { f.moveSafety -= 2f; }
        }

        f.loot += destination.Items.Count;

        if (destination.Items.Count == 0 && model.LootCells.Count > 0)
        {
            int lootNow = 999;
            int lootAfter = 999;

            foreach (Vector2Int cell in model.LootCells)
            {
                lootNow = Mathf.Min(lootNow, BotBoardModel.Distance(cell, from));
                lootAfter = Mathf.Min(lootAfter, BotBoardModel.Distance(cell, to));
            }

            f.lootApproach += Mathf.Max(0, lootNow - lootAfter);
        }

        f.exposure += OpenSides(hero, from, from) - OpenSides(hero, to, from);

        bool auraNow = model.AuraCells.Contains(from);
        bool auraAfter = model.AuraCells.Contains(to);

        if (auraAfter && !auraNow) { f.auraMove += 1f; }
        else if (auraNow && !auraAfter) { f.auraMove -= 1f; }

        int preferred = PreferredRange(hero);
        int nearNow = NearestEnemy(model, from);
        int nearAfter = NearestEnemy(model, to);

        if (nearNow < 999) { f.moveSetup += 0.5f * (Mathf.Abs(nearNow - preferred) - Mathf.Abs(nearAfter - preferred)); }
    }

    /// Neighbouring tiles of `cell` on the board that are not held by our side - where an enemy could
    /// stand to hit a hero there. `vacated` is the hero's own tile, open once it has left.
    private static int OpenSides(Character hero, Vector2Int cell, Vector2Int vacated)
    {
        GridManager grid = GridManager.Instance;
        int open = 0;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) { continue; }

                Vector2Int at = new(cell.x + dx, cell.y + dy);
                GridTile tile = grid != null ? grid.GetTile(at) : null;

                if (tile == null) { continue; }

                Character occupant = tile.Occupant;

                if (at != vacated && occupant != null && occupant != hero && BotAssets.IsOurSide(occupant)) { continue; }

                open++;
            }
        }

        return open;
    }

    private static int NearestEnemy(BotBoardModel model, Vector2Int cell)
    {
        int nearest = 999;

        foreach (Character enemy in model.Enemies)
        {
            nearest = Mathf.Min(nearest, BotBoardModel.Distance(enemy.Tile.Coordinates, cell));
        }

        return nearest;
    }

    /// The distance this hero's attacks want - its longest damage card across the whole deck, 1 for a
    /// hero with only melee. What "closing in" means differs for a Knight and a Mage.
    private static int PreferredRange(Character hero)
    {
        int longest = 0;

        void Consider(IReadOnlyList<Card> pile)
        {
            foreach (Card card in pile)
            {
                if (card == null || !card.HasEffect<DamageEffect>()) { continue; }

                int reach = card.range.Shape == RangeShape.Anywhere ? 3 : card.range.MaxDistance;
                longest = Mathf.Max(longest, reach);
            }
        }

        Consider(hero.Hand);
        Consider(hero.DrawPile);
        Consider(hero.DiscardPile);

        return Mathf.Max(1, longest);
    }

    /// Whether anything else in hand is out of reach of the energy left after this card.
    private static bool WantsEnergy(Character hero, Card card)
    {
        int left = hero.Energy - card.cost;

        foreach (Card other in hero.Hand)
        {
            if (other != null && other != card && other.LockedTurns == 0 && other.cost > left) { return true; }
        }

        return false;
    }

    /// <summary>
    /// For the best-scoring moves: step the hero onto the destination, ask every enemy's own brain what it
    /// would now do (BattleManager.ForecastPlan - the same DecidePlan the intent row is drawn from), total
    /// where those hits land, and step back. What the move takes off our side, need-weighted, is Forecast;
    /// an ally the move drops or saves this round counts through Life Saved. Same relocate-and-restore as
    /// FollowUp, and nothing is committed - the finally puts the board back exactly as it was.
    /// </summary>
    private static void Forecast(BotBoardModel model, BotProfileData profile, Scoring scoring, List<BotCandidate> sorted)
    {
        int done = 0;
        int limit = Mathf.Max(0, profile.forecastMoves);

        foreach (BotCandidate candidate in sorted)
        {
            if (done >= limit) { break; }

            if (!candidate.Card.HasEffect<MoveEffect>() || candidate.Tile.Occupant != null) { continue; }

            Character hero = candidate.Hero;
            GridTile origin = hero.Tile;

            if (origin == null || candidate.Tile == origin) { continue; }

            Dictionary<Character, int> after;
            Dictionary<Character, int> poisonAfter = new();

            try
            {
                hero.MoveTo(candidate.Tile);
                after = IncomingNow(model, scoring.Poison ? poisonAfter : null);
            }
            finally
            {
                hero.MoveTo(origin);
            }

            float forecast = 0f;
            float saved = 0f;

            foreach (Character ally in model.OurSide)
            {
                int before = model.IncomingOn(ally);
                int now = BotTurnState.Of(after, ally);
                float side = ally.IsPlayerControlled ? 1f : 0.5f;

                forecast += side * scoring.NeedOf(ally) * (before - now);

                int health = ally.Health;

                if (scoring.Poison)
                {
                    forecast += side * scoring.NeedOf(ally)
                                * (model.PoisonValue(ally, model.PoisonAddedOn(ally))
                                   - model.PoisonValue(ally, BotTurnState.Of(poisonAfter, ally)));
                    health -= model.PoisonNowOn(ally);
                }

                bool fallsBefore = before >= health;
                bool fallsAfter = now >= health;

                if (fallsBefore && !fallsAfter) { saved += side; }
                else if (!fallsBefore && fallsAfter) { saved -= side; }
            }

            candidate.Measure.Fixed.forecast += forecast;
            candidate.Measure.Fixed.lifeSaved += saved;
            candidate.Features.forecast += forecast;
            candidate.Features.lifeSaved += saved;
            candidate.Score = candidate.Features.Score(scoring.W);
            done++;
        }
    }

    /// Ally -> damage every enemy's freshly forecast plan would deal them on the board as it stands now;
    /// with `poison` set, the Poison stacks those plans would add go there.
    private static Dictionary<Character, int> IncomingNow(BotBoardModel model, Dictionary<Character, int> poison)
    {
        Dictionary<Character, int> incoming = new();
        GridManager grid = GridManager.Instance;

        if (grid == null) { return incoming; }

        foreach (Character enemy in model.Enemies)
        {
            foreach (Intent step in BattleManager.ForecastPlan(enemy))
            {
                if (step.kind != IntentKind.Attack || step.card == null) { continue; }

                GridTile aim = grid.GetTile(step.target);

                if (aim == null) { continue; }

                if (poison != null) { BotBoardModel.PoisonFrom(enemy, step.card, aim, poison); }

                foreach (KeyValuePair<Character, int> hit in step.card.PreviewDamage(enemy, aim))
                {
                    if (hit.Key == null || hit.Value <= 0 || !BotAssets.IsOurSide(hit.Key)) { continue; }

                    incoming[hit.Key] = BotTurnState.Of(incoming, hit.Key) + hit.Value;
                }
            }
        }

        return incoming;
    }

    /// <summary>
    /// For the few best-scoring moves: step the hero onto the destination, find the best attack it could
    /// then make with the rest of its hand, and step it back - the same relocate-and-restore
    /// BattleManager.DecidePlan uses to forecast an enemy's walk. Character.MoveTo only swaps occupancy
    /// (plus a sort-order refresh and GridManager's route-cache version), raises no events and rolls
    /// nothing, and the finally puts the board back exactly as it was.
    /// </summary>
    private static void MoveLookahead(BotBoardModel model, Scoring scoring, List<BotCandidate> sorted)
    {
        BotWeights weights = scoring.W;
        bool hunting = !Mathf.Approximately(weights.hunt, 0f);
        int limit = hunting ? HuntLookaheadCount : MoveLookaheadCount;
        int done = 0;

        foreach (BotCandidate candidate in sorted)
        {
            if (done >= limit) { break; }

            if (!candidate.Card.HasEffect<MoveEffect>() || candidate.Tile.Occupant != null) { continue; }

            float followUp = FollowUp(model, scoring, candidate, out float hunt);

            // The original four keep their follow-up exactly as before; the extra moves a hunting profile
            // looks at are credited through Hunt alone.
            if (done < MoveLookaheadCount)
            {
                // Into the measurement too, so the turn search re-scores the move with its follow-up.
                candidate.Measure.Fixed.moveSetup += followUp;
                candidate.Features.moveSetup += followUp;
            }

            if (hunting)
            {
                candidate.Measure.Fixed.hunt += hunt;
                candidate.Features.hunt += hunt;
            }

            candidate.Score = candidate.Features.Score(weights);
            done++;
        }
    }

    /// The best attack the hero could make after the move, by damage (the return value) and by Hunt: the
    /// share of each enemy hit that it takes off, times what that enemy deals per round - Focus, one move
    /// ahead. What walks a Knight towards the archer rather than the pebble beside it.
    private static float FollowUp(BotBoardModel model, Scoring scoring, BotCandidate move, out float hunt)
    {
        Character hero = move.Hero;
        GridTile origin = hero.Tile;
        GridManager grid = GridManager.Instance;
        float best = 0f;
        hunt = 0f;

        if (origin == null || grid == null) { return 0f; }

        int energyLeft = hero.Energy - move.Card.cost;

        try
        {
            hero.MoveTo(move.Tile);

            foreach (Card other in hero.Hand)
            {
                if (other == null || other == move.Card || other.LockedTurns > 0 || !other.HasEffect<DamageEffect>())
                {
                    continue;
                }

                bool affordable = other.cost <= energyLeft;

                foreach (GridTile target in grid.GetTilesInRange(move.Tile, other.range))
                {
                    if (other.Refusal(hero, target) != null) { continue; }

                    int damage = 0;
                    float danger = 0f;

                    foreach (KeyValuePair<Character, int> hit in other.PreviewDamage(hero, target))
                    {
                        if (hit.Key != null && hit.Key.IsHostileToParty)
                        {
                            int health = Mathf.Max(0, hit.Key.Health);
                            int lost = Mathf.Min(hit.Value, health);

                            damage += lost;

                            if (health > 0) { danger += Danger(model, scoring, hit.Key) * lost / health; }
                        }
                    }

                    float share = affordable ? 1f : 0.5f;
                    best = Mathf.Max(best, damage * share);
                    hunt = Mathf.Max(hunt, danger * share);
                }
            }
        }
        finally
        {
            hero.MoveTo(origin);
        }

        return best;
    }

    /// What an enemy deals per round, its Poison included when the profile counts it - Focus and Hunt.
    private static float Danger(BotBoardModel model, Scoring scoring, Character enemy) =>
        model.PerRoundOf(enemy)
        + (scoring.Poison ? model.PoisonPerRoundOf(enemy) / Mathf.Max(1, model.FutureTicks) : 0f);

    /// <summary>
    /// What an attack made right now adds through the hero's on-hit buffs: Lifesteal heals half of each
    /// landed hit, rounded up (LifestealStatus) - a heal op on the hero, so it is capped by the health they
    /// are missing like any heal; Poison Blade puts one Poison on every enemy it hits.
    /// </summary>
    private static void OnHitRiders(BotBoardModel model, BotMeasure m, Character hero)
    {
        bool lifesteal = hero.StatusStacks(StatusType.Lifesteal) > 0;
        bool blade = hero.StatusStacks(StatusType.PoisonBlade) > 0;

        if (!lifesteal && !blade) { return; }

        int returned = 0;

        foreach (KeyValuePair<Character, int> hit in m.Hits)
        {
            if (BotAssets.SideOf(hit.Key) != BotSide.Enemy) { continue; }

            int landed = Mathf.Min(hit.Value, Mathf.Max(0, hit.Key.Health));

            if (landed <= 0) { continue; }

            returned += (landed + 1) / 2;

            if (blade) { m.Fixed.dot += Mathf.Min(hit.Key.Health, Mathf.Clamp(model.FutureTicks, 1, 3)); }
        }

        if (lifesteal && returned > 0) { m.Ops.Add(new BotOp(BotOp.Kind.Heal, hero, returned)); }
    }

    /// Roughly what this hero hits for in a turn: its energy times its deck's damage per point of energy.
    private static float DamagePerTurn(Character hero)
    {
        DeckDamage(hero, out float damage, out float cost, out _, out _);

        return cost > 0f ? Mathf.Max(1, hero.EnergyCapacity) * damage / cost : 0f;
    }

    /// Roughly how many hits this hero lands in a turn: the cards its energy pays for, times the share of
    /// its deck that deals damage.
    private static float HitsPerTurn(Character hero)
    {
        DeckDamage(hero, out _, out float cost, out int attacks, out int cards);

        if (cards == 0 || cost <= 0f) { return 0f; }

        return Mathf.Max(1, hero.EnergyCapacity) / (cost / cards) * attacks / cards;
    }

    /// Totals over every card the hero owns: damage dealt to others, energy cost (a free card counts as
    /// half a point - it still takes a draw), cards that deal damage, and cards.
    private static void DeckDamage(Character hero, out float damage, out float cost, out int attacks, out int cards)
    {
        float d = 0f;
        float c = 0f;
        int a = 0;
        int n = 0;

        void Consider(IReadOnlyList<Card> pile)
        {
            foreach (Card card in pile)
            {
                if (card == null) { continue; }

                n++;
                c += card.cost > 0 ? card.cost : 0.5f;

                int total = 0;

                for (int e = 0; e < card.EntryCount; e++)
                {
                    CardEffectEntry entry = card.GetEntry(e);

                    if (entry.effect is not DamageEffect hit || entry.aimsAt == EffectTarget.Source) { continue; }
                    if (!card.EntryLive(entry, hero)) { continue; }

                    ActionContext ctx = new(card, hero, Array.Empty<GridTile>(), null, card.EntryDelta(entry, hero), entry.amountPercent);
                    total += ctx.Amount(hit.Damage);
                }

                if (total > 0)
                {
                    a++;
                    d += total;
                }
            }
        }

        Consider(hero.Hand);
        Consider(hero.DrawPile);
        Consider(hero.DiscardPile);

        damage = d;
        cost = c;
        attacks = a;
        cards = n;
    }

    private static void WarnUnknown(CardEffect effect)
    {
        string type = effect.GetType().Name;

        if (warnedEffects.Add(type))
        {
            Debug.LogWarning($"BotPlanner: no scoring rule for {type} - it counts for nothing until one is added.");
        }
    }
}
#endif
