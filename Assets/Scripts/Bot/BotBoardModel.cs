#if UNITY_EDITOR || BOT_RUNNER
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The board as the balance bot sees it at one decision: who is where, and - the part a player reads off
/// the intent icons - how much damage every enemy's committed plan is about to do to whom.
///
/// Built once per decision and thrown away. Nothing here changes the game or rolls a die: threat comes
/// from Card.PreviewDamage on each enemy's committed attack, the same call the damage preview uses, so it
/// already accounts for the victim's Shield, Block, Parry and Dodge without spending any of them.
///
/// Enemies re-decide their aim when they actually act (see BattleManager.EnemyResolve), so this is a
/// forecast, exactly as good as the one on screen.
/// </summary>
public sealed class BotBoardModel
{
    public BattleManager Battle { get; private set; }

    /// Living heroes, in party order.
    public readonly List<Character> Heroes = new();

    /// Everyone on our side: heroes, friendly summons and totems.
    public readonly List<Character> OurSide = new();

    public readonly List<Character> Enemies = new();

    /// Total health each of ours is set to lose to this round's committed attacks.
    public readonly Dictionary<Character, int> Incoming = new();

    /// The same, hit by hit, largest first - what a Block or Parry charge is worth against.
    public readonly Dictionary<Character, List<int>> Hits = new();

    /// Enemy -> what its committed plan does to our side, in total and per victim.
    public readonly Dictionary<Character, int> Threat = new();

    public readonly Dictionary<Character, Dictionary<Character, int>> ThreatOn = new();

    /// Enemy -> how many attack steps its plan holds, for Weaken.
    public readonly Dictionary<Character, int> AttackSteps = new();

    /// Enemy -> roughly how far it can strike this round (move range + attack range), Chebyshev.
    public readonly Dictionary<Character, int> Reach = new();

    /// Cells a friendly totem's aura covers.
    public readonly HashSet<Vector2Int> AuraCells = new();

    /// Cells with loot lying on them - what a move can detour towards.
    public readonly List<Vector2Int> LootCells = new();

    /// Enemy -> the most damage it can do in a round on a normal turn: this round's committed threat, or
    /// its hardest-hitting card when it is not attacking right now. What a kill saves per round to come.
    public readonly Dictionary<Character, int> PerRound = new();

    /// Ally -> Poison stacks they carry now. Poison bites at the end of the carrier's own phase, so a
    /// hero's ticks before this round's enemies act - and no Shield, Block or Parry stops it.
    public readonly Dictionary<Character, int> PoisonNow = new();

    /// Ally -> Poison stacks this round's committed attacks will add (Venom Cut, Plague Bite...).
    public readonly Dictionary<Character, int> PoisonAdded = new();

    /// Enemy -> what the Poison its committed plan applies is worth in damage over the rounds left -
    /// what PreviewDamage, counting hits only, leaves out. A Light Bandit's Venom Cut previews as 1.
    public readonly Dictionary<Character, float> PoisonThreat = new();

    /// Enemy -> the same for its nastiest poison card, for an enemy not poisoning anyone right now.
    public readonly Dictionary<Character, float> PoisonPerRound = new();

    /// Rounds played and rounds still to survive, and which level of the run this is (1-based).
    public int TurnsElapsed { get; private set; }

    public int TurnsRemaining { get; private set; }

    public int Level { get; private set; } = 1;

    /// How far through the battle this is, 0 at the start, 1 on the last turn.
    public float Progress
    {
        get
        {
            int total = TurnsElapsed + TurnsRemaining;
            return total <= 1 ? 1f : Mathf.Clamp01((TurnsElapsed - 1f) / (total - 1f));
        }
    }

    /// Living heroes' health over their max health, together.
    public float PartyHealth
    {
        get
        {
            int health = 0;
            int max = 0;

            foreach (Character hero in Heroes)
            {
                health += Mathf.Max(0, hero.Health);
                max += Mathf.Max(1, hero.MaxHealth);
            }

            return max > 0 ? (float)health / max : 0f;
        }
    }

    public int PerRoundOf(Character enemy) => enemy != null && PerRound.TryGetValue(enemy, out int amount) ? amount : 0;

    public int PoisonNowOn(Character ally) => ally != null && PoisonNow.TryGetValue(ally, out int n) ? n : 0;

    public int PoisonAddedOn(Character ally) => ally != null && PoisonAdded.TryGetValue(ally, out int n) ? n : 0;

    public float PoisonThreatOf(Character enemy) => enemy != null && PoisonThreat.TryGetValue(enemy, out float v) ? v : 0f;

    public float PoisonPerRoundOf(Character enemy) => enemy != null && PoisonPerRound.TryGetValue(enemy, out float v) ? v : 0f;

    /// Rounds whose end-of-phase Poison tick still lands before the battle is won: poison an enemy applies
    /// this round first bites at the end of our next phase, and there are TurnsRemaining - 1 of those.
    public int FutureTicks => Mathf.Max(0, TurnsRemaining - 1);

    /// Damage `stacks` of Poison deals over `ticks` ticks: stacks, stacks - 1, ... while both last.
    public static int PoisonTotal(int stacks, int ticks)
    {
        int total = 0;

        for (int i = 0; i < ticks && stacks - i > 0; i++) { total += stacks - i; }

        return total;
    }

    /// What `added` more stacks on `ally` cost them over the rounds left, on top of what they carry.
    public float PoisonValue(Character ally, int added)
    {
        if (added <= 0) { return 0f; }

        int carried = Mathf.Max(0, PoisonNowOn(ally) - 1);

        return PoisonTotal(carried + added, FutureTicks) - PoisonTotal(carried, FutureTicks);
    }

    /// Everything Poison will still cost `ally` this battle: this round's tick, then what they carry plus
    /// what this round's attacks add, decaying over the rounds left.
    public int PoisonAhead(Character ally) =>
        PoisonNowOn(ally) + PoisonTotal(Mathf.Max(0, PoisonNowOn(ally) - 1) + PoisonAddedOn(ally), FutureTicks);

    /// <summary>
    /// Every one of ours an enemy's `card` aimed at `aim` would poison, and how many stacks - the
    /// ApplyStatusEffect entries PreviewDamage does not report. Landed tiles come from Card.LandedTiles,
    /// so an area's audience rules are the game's.
    /// </summary>
    public static void PoisonFrom(Character enemy, Card card, GridTile aim, Dictionary<Character, int> into)
    {
        if (card == null || aim == null) { return; }

        for (int e = 0; e < card.EntryCount; e++)
        {
            CardEffectEntry entry = card.GetEntry(e);

            if (entry.effect is not ApplyStatusEffect status || status.Status != StatusType.Poison) { continue; }
            if (!card.EntryLive(entry, enemy)) { continue; }

            List<GridTile> landed = card.LandedTiles(enemy, aim, e);
            ActionContext ctx = new(card, enemy, landed, aim, card.EntryDelta(entry, enemy), entry.amountPercent);
            int stacks = ctx.Amount(status.RiderAmount);

            if (stacks <= 0) { continue; }

            foreach (GridTile tile in landed)
            {
                Character victim = tile != null ? tile.Occupant : null;

                if (!BotAssets.IsLiving(victim) || !BotAssets.IsOurSide(victim)) { continue; }

                into[victim] = (into.TryGetValue(victim, out int so) ? so : 0) + stacks;
            }
        }
    }

    public int HeroOrder(Character hero)
    {
        int index = Heroes.IndexOf(hero);
        return index >= 0 ? index : 99;
    }

    public int IncomingOn(Character ally) => ally != null && Incoming.TryGetValue(ally, out int total) ? total : 0;

    public int ThreatOf(Character enemy) => enemy != null && Threat.TryGetValue(enemy, out int total) ? total : 0;

    public int ThreatOf(Character enemy, Character victim) =>
        enemy != null && victim != null && ThreatOn.TryGetValue(enemy, out Dictionary<Character, int> byVictim)
        && byVictim.TryGetValue(victim, out int amount)
            ? amount
            : 0;

    public int ReachOf(Character enemy) => enemy != null && Reach.TryGetValue(enemy, out int reach) ? reach : 1;

    public static int Distance(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

    public static BotBoardModel Build(BattleManager battle)
    {
        BotBoardModel model = new() { Battle = battle };

        if (battle == null) { return model; }

        model.TurnsElapsed = battle.TurnsElapsed;
        model.TurnsRemaining = battle.TurnsRemaining;
        model.Level = RunManager.Instance != null ? RunManager.Instance.LevelNumber : 1;

        foreach (Character character in battle.Characters)
        {
            if (!BotAssets.IsLiving(character)) { continue; }

            switch (BotAssets.SideOf(character))
            {
                case BotSide.Hero:
                    model.Heroes.Add(character);
                    model.OurSide.Add(character);
                    break;
                case BotSide.Ally:
                    model.OurSide.Add(character);
                    break;
                case BotSide.Enemy:
                    model.Enemies.Add(character);
                    break;
            }
        }

        GridManager grid = GridManager.Instance;

        foreach (Character ally in model.OurSide)
        {
            int poison = ally.StatusStacks(StatusType.Poison);

            if (poison > 0) { model.PoisonNow[ally] = poison; }
        }

        foreach (Character enemy in model.Enemies)
        {
            model.Reach[enemy] = EstimateReach(enemy);
            Dictionary<Character, int> poisoned = new();

            int total = 0;
            int attacks = 0;
            Dictionary<Character, int> byVictim = new();

            foreach (Intent step in enemy.CommittedPlan)
            {
                if (step.kind != IntentKind.Attack || step.card == null || grid == null) { continue; }

                GridTile aim = grid.GetTile(step.target);

                if (aim == null) { continue; }

                attacks++;

                PoisonFrom(enemy, step.card, aim, poisoned);

                foreach (KeyValuePair<Character, int> hit in step.card.PreviewDamage(enemy, aim))
                {
                    Character victim = hit.Key;

                    if (victim == null || hit.Value <= 0 || !BotAssets.IsOurSide(victim)) { continue; }

                    total += hit.Value;
                    byVictim[victim] = byVictim.TryGetValue(victim, out int so) ? so + hit.Value : hit.Value;
                    model.Incoming[victim] = model.IncomingOn(victim) + hit.Value;

                    if (!model.Hits.TryGetValue(victim, out List<int> hits))
                    {
                        hits = new List<int>();
                        model.Hits[victim] = hits;
                    }

                    hits.Add(hit.Value);
                }
            }

            float poisonThreat = 0f;

            foreach (KeyValuePair<Character, int> pair in poisoned)
            {
                model.PoisonAdded[pair.Key] = model.PoisonAddedOn(pair.Key) + pair.Value;
                poisonThreat += model.PoisonValue(pair.Key, pair.Value);
            }

            model.PoisonThreat[enemy] = poisonThreat;
            model.PoisonPerRound[enemy] = Mathf.Max(poisonThreat, PoisonTotal(HardestPoison(enemy), model.FutureTicks));
            model.Threat[enemy] = total;
            model.PerRound[enemy] = Mathf.Max(total, HardestHit(enemy));
            model.ThreatOn[enemy] = byVictim;
            model.AttackSteps[enemy] = attacks;
        }

        foreach (List<int> hits in model.Hits.Values) { hits.Sort((a, b) => b.CompareTo(a)); }

        if (grid != null)
        {
            foreach (GridTile tile in grid.AllTiles)
            {
                if (tile != null && tile.Items.Count > 0) { model.LootCells.Add(tile.Coordinates); }
            }

            foreach (Totem totem in Totem.Active)
            {
                if (totem == null || !totem.IsProjecting || totem.Affects == AuraAudience.Enemies) { continue; }

                if (!totem.TryGetComponent(out Character owner) || !BotAssets.IsOurSide(owner)) { continue; }

                GridTile origin = totem.OriginTile;

                foreach (GridTile tile in grid.AllTiles)
                {
                    if (tile != null && totem.Range.Contains(origin, tile)) { model.AuraCells.Add(tile.Coordinates); }
                }
            }
        }

        return model;
    }

    /// The most damage one of the enemy's cards deals as authored, across its whole deck - a stand-in for
    /// what it does on a round it attacks, since the intent on screen only covers this one.
    private static int HardestHit(Character enemy)
    {
        int best = 0;

        void Consider(IReadOnlyList<Card> pile)
        {
            foreach (Card card in pile)
            {
                if (card == null) { continue; }

                int total = 0;

                for (int e = 0; e < card.EntryCount; e++)
                {
                    CardEffectEntry entry = card.GetEntry(e);

                    if (entry.effect is not DamageEffect damage || entry.aimsAt == EffectTarget.Source) { continue; }
                    if (!card.EntryLive(entry, enemy)) { continue; }

                    ActionContext ctx = new(card, enemy, System.Array.Empty<GridTile>(), null, card.EntryDelta(entry, enemy), entry.amountPercent);
                    total += ctx.Amount(damage.Damage);
                }

                best = Mathf.Max(best, total);
            }
        }

        Consider(enemy.Hand);
        Consider(enemy.DrawPile);
        Consider(enemy.DiscardPile);

        return best;
    }

    /// The most Poison stacks one of the enemy's cards applies per target, across its whole deck.
    private static int HardestPoison(Character enemy)
    {
        int best = 0;

        void Consider(IReadOnlyList<Card> pile)
        {
            foreach (Card card in pile)
            {
                if (card == null) { continue; }

                for (int e = 0; e < card.EntryCount; e++)
                {
                    CardEffectEntry entry = card.GetEntry(e);

                    if (entry.effect is not ApplyStatusEffect status || status.Status != StatusType.Poison) { continue; }
                    if (!card.EntryLive(entry, enemy)) { continue; }

                    ActionContext ctx = new(card, enemy, System.Array.Empty<GridTile>(), null, card.EntryDelta(entry, enemy), entry.amountPercent);
                    best = Mathf.Max(best, ctx.Amount(status.RiderAmount));
                }
            }
        }

        Consider(enemy.Hand);
        Consider(enemy.DrawPile);
        Consider(enemy.DiscardPile);

        return best;
    }

    /// <summary>
    /// How far an enemy can hit from where it stands this round: its longest attack (click range plus the
    /// widest splash, the same sum EnemyBrain.LongestReach takes) plus, when it has a second action point
    /// to spend walking, its longest move. An approximation in Chebyshev tiles - good enough to tell
    /// "stepping back two tiles gets me out of that archer's shot" from "it doesn't".
    /// </summary>
    private static int EstimateReach(Character enemy)
    {
        int attack = 0;
        int move = 0;

        foreach (Card card in enemy.Hand)
        {
            if (card == null) { continue; }

            bool anywhere = card.range.Shape == RangeShape.Anywhere;

            if (card.HasEffect<DamageEffect>())
            {
                attack = Mathf.Max(attack, anywhere ? 99 : card.range.MaxDistance + card.WidestAreaReach());
            }

            if (card.HasEffect<MoveEffect>())
            {
                move = Mathf.Max(move, anywhere ? 99 : card.range.MaxDistance);
            }
        }

        if (attack == 0) { return 0; }

        return enemy.ActionPoints > 1 ? attack + move : attack;
    }
}
#endif
