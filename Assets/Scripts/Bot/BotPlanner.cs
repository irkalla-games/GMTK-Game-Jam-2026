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
        selfDamage, discard;

    public int cost;

    public float Score(BotWeights w) =>
        damage * w.damage + kills * w.kill + threatRemoved * w.threatRemoved + friendlyFire * w.friendlyFire
        + mitigation * w.mitigation + mitigationSpare * w.mitigationSpare + heal * w.heal
        + lifeSaved * w.lifeSaved + dot * w.dot + debuff * w.debuff + buff * w.buff
        + totemCover * w.totemCover + totemBase * w.totemBase + summon * w.summon + moveSetup * w.moveSetup
        + moveSafety * w.moveSafety + loot * w.loot + auraMove * w.auraMove + draw * w.draw + energy * w.energy
        + selfDamage * w.selfDamage + discard * w.discard - cost * w.costPenalty;

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

    public bool IsEndTurn => Play == null;
}

/// <summary>
/// The balance bot's in-battle brain: every legal play for every hero (hand card x tile in range x
/// facing), each scored as Σ weight × feature (BotFeatures), and the best one taken - or the turn ended
/// when nothing reaches the profile's pass threshold. One ply, greedy, re-planned after every play, so a
/// Cleric shield changes what the Knight does next.
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

    private static readonly HashSet<string> warnedEffects = new();

    public static BotDecision Decide(
        BotBoardModel model, BotProfileData profile, System.Random rng, ICollection<string> banned)
    {
        Stopwatch timer = Stopwatch.StartNew();
        BotDecision decision = new();

        List<BotCandidate> candidates = Enumerate(model, profile, banned);
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
            MoveLookahead(model, profile, candidates);
            candidates.Sort(Compare);

            decision.Play = PlayFirst(profile, candidates);

            if (decision.Play != null)
            {
                decision.Reason = $"play first: {decision.Play.Info.Primary}";
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

    private static List<BotCandidate> Enumerate(BotBoardModel model, BotProfileData profile, ICollection<string> banned)
    {
        List<BotCandidate> candidates = new();
        GridManager grid = GridManager.Instance;

        if (grid == null) { return candidates; }

        foreach (Character hero in model.Heroes)
        {
            if (!hero.CanAct) { continue; }

            int heroOrder = model.HeroOrder(hero);
            HashSet<string> seenCards = new();

            for (int handIndex = 0; handIndex < hero.Hand.Count; handIndex++)
            {
                Card card = hero.Hand[handIndex];

                if (card == null || card.PlayRefusal(hero) != null) { continue; }

                // Two copies in the same state make the same plays - evaluate the first one only.
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
                        };

                        if (banned != null && banned.Contains(candidate.Key)) { continue; }

                        Evaluate(model, profile, candidate);
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

    /// Per-candidate bookkeeping: how much of each ally's incoming damage this play has already stopped
    /// or healed, so a card with a shield and a heal on the same ally does not count the danger twice.
    private sealed class Tally
    {
        public readonly Dictionary<Character, int> Prevented = new();
        public readonly Dictionary<Character, int> Healed = new();

        public int Of(Dictionary<Character, int> map, Character c) => map.TryGetValue(c, out int v) ? v : 0;

        public void Add(Dictionary<Character, int> map, Character c, int amount) => map[c] = Of(map, c) + amount;
    }

    private static void Evaluate(BotBoardModel model, BotProfileData profile, BotCandidate candidate)
    {
        Character hero = candidate.Hero;
        Card card = candidate.Card;
        GridTile tile = candidate.Tile;
        BotFeatures f = new() { cost = card.cost };
        Tally tally = new();

        foreach (KeyValuePair<Character, int> hit in card.PreviewDamage(hero, tile))
        {
            Character victim = hit.Key;

            if (victim == null || hit.Value <= 0) { continue; }

            int lost = Mathf.Min(hit.Value, Mathf.Max(0, victim.Health));

            switch (BotAssets.SideOf(victim))
            {
                case BotSide.Enemy:
                    f.damage += lost;

                    if (hit.Value >= victim.Health)
                    {
                        f.kills += 1f;
                        f.threatRemoved += model.ThreatOf(victim);
                    }

                    break;
                case BotSide.Hero:
                case BotSide.Ally:
                    f.friendlyFire += lost;
                    break;
            }
        }

        bool moves = false;
        bool pushed = false;

        for (int e = 0; e < card.EntryCount; e++)
        {
            CardEffectEntry entry = card.GetEntry(e);
            CardEffect effect = entry.effect;

            if (effect == null || effect is DamageEffect || effect is AnimateEffect) { continue; }

            List<GridTile> landed = card.LandedTiles(hero, tile, e);
            ActionContext ctx = new(card, hero, landed, tile, entry.amountDelta, entry.amountPercent);

            switch (effect)
            {
                case ShieldEffect shield:
                    foreach (Character ally in Occupants(landed, ours: true))
                    {
                        Shield(model, f, tally, ally, ctx.Amount(shield.ShieldAmount));
                    }

                    break;
                case BlockEffect block:
                    foreach (Character ally in Occupants(landed, ours: true))
                    {
                        Charges(model, f, tally, ally, ctx.Amount(block.BlockCount), BlockStatus.AmountPerHit);
                    }

                    break;
                case ParryEffect parry:
                    foreach (Character ally in Occupants(landed, ours: true))
                    {
                        Charges(model, f, tally, ally, ctx.Amount(parry.ParryCharges), int.MaxValue);
                    }

                    break;
                case HealEffect heal:
                    foreach (Character ally in Occupants(landed, ours: true))
                    {
                        Heal(model, f, tally, ally, ctx.Amount(heal.RiderAmount));
                    }

                    break;
                case CleansePoisonEffect:
                    foreach (Character ally in Occupants(landed, ours: true))
                    {
                        int poison = ally.StatusStacks(StatusType.Poison);
                        f.heal += Mathf.Min(40, poison * (poison + 1) / 2);
                    }

                    break;
                case ApplyStatusEffect status:
                    foreach (Character target in Occupants(landed, ours: null))
                    {
                        StatusFeature(model, f, tally, target, status.Status, ctx.Amount(status.RiderAmount));
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
                    break;
                case MoveEffect:
                    moves = true;
                    break;
                case DrawEffect draw:
                    f.draw += ctx.Amount(draw.RiderAmount);
                    break;
                case EnergyEffect energy:
                    f.energy += WantsEnergy(hero, card) ? energy.Amount : energy.Amount * 0.2f;
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

        // An ally this round's intents would drop, who this play keeps standing.
        foreach (Character ally in model.OurSide)
        {
            int incoming = model.IncomingOn(ally);

            if (incoming < ally.Health) { continue; }

            int after = incoming - tally.Of(tally.Prevented, ally);

            if (after < ally.Health + tally.Of(tally.Healed, ally))
            {
                f.lifeSaved += ally.IsPlayerControlled ? 1f : 0.5f;
            }
        }

        candidate.Features = f;
        candidate.Score = f.Score(profile.weights);
        candidate.Info = BotCardInfo.Of(card);
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

    private static int Remaining(BotBoardModel model, Tally tally, Character ally) =>
        Mathf.Max(0, model.IncomingOn(ally) - tally.Of(tally.Prevented, ally));

    private static void Shield(BotBoardModel model, BotFeatures f, Tally tally, Character ally, int amount)
    {
        if (amount <= 0) { return; }

        int useful = Mathf.Min(amount, Remaining(model, tally, ally));

        f.mitigation += useful;
        f.mitigationSpare += amount - useful;
        tally.Add(tally.Prevented, ally, useful);
    }

    /// Block, Parry, Dodge: each charge meets one hit, biggest first, and stops up to `perHit` of it.
    private static void Charges(BotBoardModel model, BotFeatures f, Tally tally, Character ally, int charges, int perHit)
    {
        if (charges <= 0) { return; }

        model.Hits.TryGetValue(ally, out List<int> hits);

        int covered = hits != null ? Mathf.Min(charges, hits.Count) : 0;
        int stopped = 0;

        for (int i = 0; i < covered; i++) { stopped += Mathf.Min(perHit, hits[i]); }

        stopped = Mathf.Min(stopped, Remaining(model, tally, ally));

        f.mitigation += stopped;
        f.mitigationSpare += (charges - covered) * Mathf.Min(perHit, 5);
        tally.Add(tally.Prevented, ally, stopped);
    }

    private static void Heal(BotBoardModel model, BotFeatures f, Tally tally, Character ally, int amount)
    {
        int healed = Mathf.Min(amount, ally.MaxHealth - ally.Health);

        if (healed <= 0) { return; }

        float urgency = Mathf.Min(3f, 1f + model.IncomingOn(ally) / Mathf.Max(1f, ally.Health));

        f.heal += healed * urgency;
        tally.Add(tally.Healed, ally, healed);
    }

    private static void StatusFeature(
        BotBoardModel model, BotFeatures f, Tally tally, Character target, StatusType type, int stacks)
    {
        if (stacks <= 0) { return; }

        bool enemy = BotAssets.SideOf(target) == BotSide.Enemy;

        switch (type)
        {
            case StatusType.Poison:
                if (enemy) { f.dot += Mathf.Min(target.Health, stacks * (stacks + 1) / 2); }
                else { f.friendlyFire += stacks; }
                return;
            case StatusType.Frozen:
                if (enemy)
                {
                    f.threatRemoved += model.ThreatOf(target);
                    f.debuff += stacks;
                }

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
                if (!enemy)
                {
                    int hidden = Remaining(model, tally, target);
                    f.mitigation += hidden;
                    f.mitigationSpare += stacks;
                    tally.Add(tally.Prevented, target, hidden);
                }

                return;
            case StatusType.Dodge:
            case StatusType.Parry:
                if (!enemy) { Charges(model, f, tally, target, stacks, int.MaxValue); }
                return;
            case StatusType.Block:
                if (!enemy) { Charges(model, f, tally, target, stacks, BlockStatus.AmountPerHit); }
                return;
            case StatusType.Shield:
                if (!enemy) { Shield(model, f, tally, target, stacks); }
                return;
            case StatusType.Regeneration:
                if (!enemy) { f.heal += Mathf.Min(target.MaxHealth, stacks * (stacks + 1) / 2) * 0.5f; }
                return;
        }

        if (enemy) { f.debuff += stacks; }
        else { f.buff += stacks; }
    }

    private static void Summon(BotBoardModel model, BotFeatures f, GameObject prefab, GridTile tile)
    {
        if (prefab == null) { return; }

        if (prefab.TryGetComponent(out Totem totem))
        {
            f.totemBase += 1f;

            if (totem.Affects == AuraAudience.Enemies)
            {
                foreach (Character enemy in model.Enemies)
                {
                    if (totem.Range.Contains(tile.Coordinates, enemy.Tile.Coordinates)) { f.totemCover += 1f; }
                }
            }
            else
            {
                foreach (Character ally in model.OurSide)
                {
                    if (totem.Range.Contains(tile.Coordinates, ally.Tile.Coordinates))
                    {
                        f.totemCover += ally.IsPlayerControlled ? 1f : 0.5f;
                    }
                }
            }

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

        bool auraNow = model.AuraCells.Contains(from);
        bool auraAfter = model.AuraCells.Contains(to);

        if (auraAfter && !auraNow) { f.auraMove += 1f; }
        else if (auraNow && !auraAfter) { f.auraMove -= 1f; }

        int preferred = PreferredRange(hero);
        int nearNow = NearestEnemy(model, from);
        int nearAfter = NearestEnemy(model, to);

        if (nearNow < 999) { f.moveSetup += 0.5f * (Mathf.Abs(nearNow - preferred) - Mathf.Abs(nearAfter - preferred)); }
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
    /// For the few best-scoring moves: step the hero onto the destination, find the best attack it could
    /// then make with the rest of its hand, and step it back - the same relocate-and-restore
    /// BattleManager.DecidePlan uses to forecast an enemy's walk. Character.MoveTo only swaps occupancy
    /// (plus a sort-order refresh and GridManager's route-cache version), raises no events and rolls
    /// nothing, and the finally puts the board back exactly as it was.
    /// </summary>
    private static void MoveLookahead(BotBoardModel model, BotProfileData profile, List<BotCandidate> sorted)
    {
        int done = 0;

        foreach (BotCandidate candidate in sorted)
        {
            if (done >= MoveLookaheadCount) { break; }

            if (!candidate.Card.HasEffect<MoveEffect>() || candidate.Tile.Occupant != null) { continue; }

            candidate.Features.moveSetup += FollowUp(candidate);
            candidate.Score = candidate.Features.Score(profile.weights);
            done++;
        }
    }

    private static float FollowUp(BotCandidate move)
    {
        Character hero = move.Hero;
        GridTile origin = hero.Tile;
        GridManager grid = GridManager.Instance;
        float best = 0f;

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

                    foreach (KeyValuePair<Character, int> hit in other.PreviewDamage(hero, target))
                    {
                        if (hit.Key != null && hit.Key.IsHostileToParty)
                        {
                            damage += Mathf.Min(hit.Value, Mathf.Max(0, hit.Key.Health));
                        }
                    }

                    best = Mathf.Max(best, affordable ? damage : damage * 0.5f);
                }
            }
        }
        finally
        {
            hero.MoveTo(origin);
        }

        return best;
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
