#if UNITY_EDITOR || BOT_RUNNER
using System.Collections.Generic;
using System.Text;

/// <summary>
/// The battle's state at one decision, written out canonically - what a replay checks before it
/// re-applies a recorded choice. Two runs whose fingerprints match at a decision are, as far as any rule
/// can tell, in the same position: same bodies on the same tiles at the same health and energy, the same
/// statuses, the same hands and the same draw-pile order, the same enemy plans, the same tile effects
/// and loot.
///
/// Sorted by board position everywhere, never by instance id or list order that could differ run to
/// run. Reads only; asks nothing that spends a charge.
/// </summary>
public static class BotFingerprint
{
    public static string Text(BattleManager battle, int level, int round, int step, string context)
    {
        StringBuilder sb = new();

        sb.Append("L").Append(level).Append("|R").Append(round).Append("|S").Append(step).Append('|').Append(context);

        if (battle == null) { return sb.ToString(); }

        sb.Append("|T").Append(battle.TurnsRemaining).Append("|P").Append((int)battle.Phase).Append('\n');

        List<Character> living = new();

        foreach (Character character in battle.Characters)
        {
            if (BotAssets.IsLiving(character)) { living.Add(character); }
        }

        living.Sort(ByPosition);

        foreach (Character character in living)
        {
            AppendCharacter(sb, character);
            sb.Append('\n');
        }

        GridManager grid = GridManager.Instance;

        if (grid != null)
        {
            List<GridTile> marked = new();

            foreach (GridTile tile in grid.AllTiles)
            {
                if (tile != null && (tile.TileEffects.Count > 0 || tile.Items.Count > 0)) { marked.Add(tile); }
            }

            marked.Sort((a, b) => Compare(a.Coordinates.y, a.Coordinates.x, b.Coordinates.y, b.Coordinates.x));

            foreach (GridTile tile in marked)
            {
                sb.Append('#').Append(tile.Coordinates.x).Append(',').Append(tile.Coordinates.y).Append(':');

                foreach (TileEffect effect in tile.TileEffects)
                {
                    sb.Append(effect.type).Append(effect.turnsRemaining).Append(' ');
                }

                sb.Append('I').Append(tile.Items.Count).Append('\n');
            }
        }

        return sb.ToString();
    }

    public static string Hash(string text) => BotSeeds.Hex(BotSeeds.Fnv1a64(text));

    private static int ByPosition(Character a, Character b)
    {
        int order = Compare(a.Tile.Coordinates.y, a.Tile.Coordinates.x, b.Tile.Coordinates.y, b.Tile.Coordinates.x);
        return order != 0 ? order : string.CompareOrdinal(a.name, b.name);
    }

    private static int Compare(int ay, int ax, int by, int bx) => ay != by ? ay.CompareTo(by) : ax.CompareTo(bx);

    private static void AppendCharacter(StringBuilder sb, Character c)
    {
        sb.Append(c.Tile.Coordinates.x).Append(',').Append(c.Tile.Coordinates.y).Append(':')
          .Append(BotAssets.TypeKey(c)).Append(':').Append((int)BotAssets.SideOf(c)).Append(':')
          .Append(c.Health).Append('/').Append(c.MaxHealth).Append(":E").Append(c.Energy).Append(":[");

        List<string> statuses = new();

        foreach (Status status in c.ActiveStatuses())
        {
            if (status != null) { statuses.Add($"{(status.IsProjected ? "~" : string.Empty)}{status.type}{status.stacks}"); }
        }

        statuses.Sort(string.CompareOrdinal);
        sb.Append(string.Join(",", statuses)).Append(']');

        if (c.IsPlayerControlled)
        {
            sb.Append("|H:");

            foreach (Card card in c.Hand) { sb.Append(card != null ? card.cardName : "?").Append(','); }

            StringBuilder draw = new();

            foreach (Card card in c.DrawPile) { draw.Append(card != null ? card.cardName : "?").Append(','); }

            sb.Append("|D").Append(c.DrawPile.Count).Append(':')
              .Append(BotSeeds.Hex(BotSeeds.Fnv1a64(draw.ToString())).Substring(0, 8))
              .Append("|X").Append(c.DiscardPile.Count);
        }
        else
        {
            sb.Append("|P:");

            foreach (Intent step in c.CommittedPlan)
            {
                sb.Append(step.kind).Append(' ').Append(step.card != null ? step.card.cardName : "-").Append('@')
                  .Append(step.target.x).Append(',').Append(step.target.y).Append(';');
            }
        }
    }
}
#endif
