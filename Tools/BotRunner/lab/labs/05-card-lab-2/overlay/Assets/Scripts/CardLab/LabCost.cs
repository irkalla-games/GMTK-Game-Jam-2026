using UnityEngine;

/// <summary>
/// CARD LAB 2 (bot-mirror only, never in the real project). A card whose CardData carries a lab
/// condition costs `labDiscount` less while its holder meets that condition - the patched Card.cost
/// getter asks here every time, so the hand, CanAfford, SpendEnergy and the bot's planner all see the
/// same number at the moment they look.
/// </summary>
public static class LabCost
{
    public const int None = 0, NextToTotem = 1, HasShield = 2, NextToHero = 3, HasBlock = 4;

    public static int Discount(Character holder, CardData data)
    {
        if (holder == null || data == null || data.labDiscount <= 0 || holder.Tile == null) { return 0; }

        return Met(holder, data.labCondition) ? data.labDiscount : 0;
    }

    public static bool Met(Character holder, int condition)
    {
        switch (condition)
        {
            case NextToTotem:
                foreach (Totem totem in Totem.Active)
                {
                    if (totem == null) { continue; }

                    Character body = totem.GetComponent<Character>();

                    if (body != null && !body.IsHostileToParty && Adjacent(holder, body)) { return true; }
                }

                return false;
            case HasShield:
                return holder.StatusStacks(StatusType.Shield) > 0;
            case HasBlock:
                return holder.StatusStacks(StatusType.Block) > 0;
            case NextToHero:
                if (BattleManager.Instance == null) { return false; }

                foreach (Character other in BattleManager.Instance.Characters)
                {
                    if (other == null || !other.IsPlayerControlled || other.GetComponent<Totem>() != null) { continue; }
                    if (Adjacent(holder, other)) { return true; }
                }

                return false;
        }

        return false;
    }

    private static bool Adjacent(Character a, Character b)
    {
        if (b == null || b == a || b.IsDead || b.Tile == null || a.Tile == null) { return false; }

        Vector2Int d = a.Tile.Coordinates - b.Tile.Coordinates;
        return Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)) <= 1;
    }
}
