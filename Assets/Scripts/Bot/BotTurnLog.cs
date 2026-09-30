#if UNITY_EDITOR || BOT_RUNNER
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// A run's two logs: turns.log, written for a person - the board each round, both sides' state, every
/// decision with the runner-up plays it beat, what each enemy did - and events.jsonl, the same run as
/// one JSON object per line for BotReplay and anything else that wants to read it back.
///
/// Buffered and flushed at the end of each round and on close, so a crash loses at most the round it
/// happened in.
/// </summary>
public sealed class BotTurnLog : IDisposable
{
    private readonly StreamWriter text;
    private readonly StreamWriter events;

    public BotTurnLog(string runDir, bool writeText)
    {
        Directory.CreateDirectory(runDir);

        if (writeText) { text = new StreamWriter(Path.Combine(runDir, "turns.log"), false, BotText.Utf8); }

        events = new StreamWriter(Path.Combine(runDir, "events.jsonl"), false, BotText.Utf8);
    }

    public void Line(string line = "")
    {
        if (text != null) { text.WriteLine(line); }
    }

    public void Event(BotEvent e) => events.WriteLine(JsonUtility.ToJson(e));

    public void Flush()
    {
        if (text != null) { text.Flush(); }

        events.Flush();
    }

    public void Dispose()
    {
        try
        {
            Flush();

            if (text != null) { text.Dispose(); }

            events.Dispose();
        }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// The board as text, top row first, one three-character cell per tile: H1 for heroes, E1 for
    /// enemies, T1 for totems, A1 for other allies, then * for loot, # for Wall of Force and ~ for Wall of
    /// Flames on an otherwise empty tile. Returns the legend of who each token is alongside.
    /// </summary>
    public static string Board(BattleManager battle, out Dictionary<Character, string> tokens)
    {
        tokens = new Dictionary<Character, string>();

        GridManager grid = GridManager.Instance;

        if (battle == null || grid == null) { return string.Empty; }

        List<Character> living = new();

        foreach (Character character in battle.Characters)
        {
            if (BotAssets.IsLiving(character)) { living.Add(character); }
        }

        living.Sort((a, b) =>
        {
            int side = BotAssets.SideOf(a).CompareTo(BotAssets.SideOf(b));
            if (side != 0) { return side; }

            Vector2Int ca = a.Tile.Coordinates;
            Vector2Int cb = b.Tile.Coordinates;
            return ca.y != cb.y ? cb.y.CompareTo(ca.y) : ca.x.CompareTo(cb.x);
        });

        int heroes = 0, enemies = 0, totems = 0, allies = 0;

        foreach (Character character in living)
        {
            string token = BotAssets.SideOf(character) switch
            {
                BotSide.Hero => "H" + ++heroes,
                BotSide.Enemy => "E" + ++enemies,
                _ when character.TryGetComponent(out Totem _) => "T" + ++totems,
                _ => "A" + ++allies,
            };

            tokens[character] = token;
        }

        Vector2Int size = grid.BoardSize;
        StringBuilder sb = new();

        sb.Append("    ");

        for (int x = 0; x < size.x; x++) { sb.Append(x.ToString().PadLeft(3)); }

        sb.Append('\n');

        for (int y = size.y - 1; y >= 0; y--)
        {
            sb.Append(y.ToString().PadLeft(3)).Append(' ');

            for (int x = 0; x < size.x; x++)
            {
                GridTile tile = grid.GetTile(new Vector2Int(x, y));
                string cell = " .";

                if (tile == null)
                {
                    cell = "  ";
                }
                else if (tile.Occupant != null && tokens.TryGetValue(tile.Occupant, out string token))
                {
                    cell = token.Length >= 3 ? token.Substring(0, 3) : token.PadLeft(2);
                }
                else if (tile.Items.Count > 0)
                {
                    cell = " *";
                }
                else
                {
                    foreach (TileEffect effect in tile.TileEffects)
                    {
                        if (effect.type == TileEffectType.WallOfForce) { cell = " #"; }
                        else if (effect.type == TileEffectType.WallOfFlames) { cell = " ~"; }
                    }
                }

                sb.Append(cell.PadLeft(3));
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// "Shield 5, Poison 2, ~Strength 1" - '~' marks a totem's aura rather than a carried status.
    public static string Statuses(Character character)
    {
        List<string> parts = new();

        foreach (Status status in character.ActiveStatuses())
        {
            // An equipment rule with no status type of its own reads as "None" - noise in a log.
            if (status == null || status.stacks <= 0 || status.type == StatusType.None) { continue; }

            parts.Add($"{(status.IsProjected ? "~" : string.Empty)}{status.type} {status.stacks}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "-";
    }

    public static string Hand(Character hero)
    {
        List<string> cards = new();

        foreach (Card card in hero.Hand)
        {
            if (card == null) { continue; }

            string locked = card.LockedTurns > 0 ? $" locked {card.LockedTurns}" : string.Empty;
            cards.Add($"{card.cardName}({card.cost}{locked})");
        }

        return cards.Count > 0 ? string.Join(", ", cards) : "(empty)";
    }

    /// An enemy's committed plan as the intent icons show it: "Attack Slash -> (3,4) Knight -6".
    public static string Plan(Character enemy, BotBoardModel model)
    {
        List<string> steps = new();
        GridManager grid = GridManager.Instance;

        foreach (Intent step in enemy.CommittedPlan)
        {
            if (step.IsWait) { continue; }

            string where = $"({step.target.x},{step.target.y})";

            if (step.kind == IntentKind.Attack && step.card != null && grid != null)
            {
                GridTile aim = grid.GetTile(step.target);
                List<string> victims = new();

                if (aim != null)
                {
                    foreach (KeyValuePair<Character, int> hit in step.card.PreviewDamage(enemy, aim))
                    {
                        if (hit.Key != null && hit.Value > 0) { victims.Add($"{BotAssets.TypeKey(hit.Key)} -{hit.Value}"); }
                    }
                }

                steps.Add($"Attack {step.card.cardName} -> {where}{(victims.Count > 0 ? " " + string.Join(", ", victims) : string.Empty)}");
            }
            else
            {
                steps.Add($"{step.kind} {(step.card != null ? step.card.cardName : string.Empty)} -> {where}");
            }
        }

        return steps.Count > 0 ? string.Join(" then ", steps) : "wait";
    }
}
#endif
