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

        foreach (Character enemy in model.Enemies)
        {
            model.Reach[enemy] = EstimateReach(enemy);

            int total = 0;
            int attacks = 0;
            Dictionary<Character, int> byVictim = new();

            foreach (Intent step in enemy.CommittedPlan)
            {
                if (step.kind != IntentKind.Attack || step.card == null || grid == null) { continue; }

                GridTile aim = grid.GetTile(step.target);

                if (aim == null) { continue; }

                attacks++;

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

            model.Threat[enemy] = total;
            model.ThreatOn[enemy] = byVictim;
            model.AttackSteps[enemy] = attacks;
        }

        foreach (List<int> hits in model.Hits.Values) { hits.Sort((a, b) => b.CompareTo(a)); }

        if (grid != null)
        {
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
