#if UNITY_EDITOR || BOT_RUNNER
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a card is, as far as the balance bot is concerned: its play categories (for "play these first"
/// and the report's play mix), and a board-free value for drafting and discarding.
///
/// Read from the card's effect entries, never from CardTag - most cards carry no tags at all. Cached per
/// CardData, since a card's entries only change through CardModifiers, which rewrite the runtime Card and
/// never the asset this keys on - the value is a drafting heuristic, not a promise.
/// </summary>
public sealed class BotCardInfo
{
    /// Which category a card reads as when only one is wanted - the report's "played by category" and
    /// reward-taste multipliers. A totem card that also buffs is a Totem card; an attack that also
    /// shields is an Attack card.
    public static readonly BotPlayCategory[] PriorityOrder =
    {
        BotPlayCategory.Totem, BotPlayCategory.Summon, BotPlayCategory.Attack, BotPlayCategory.Heal,
        BotPlayCategory.Defense, BotPlayCategory.Debuff, BotPlayCategory.Buff, BotPlayCategory.Move,
        BotPlayCategory.Utility,
    };

    private static readonly Dictionary<CardData, BotCardInfo> cache = new();

    private readonly bool[] has = new bool[Enum.GetValues(typeof(BotPlayCategory)).Length];

    public BotPlayCategory Primary { get; private set; } = BotPlayCategory.Utility;

    public bool Has(BotPlayCategory category) => has[(int)category];

    public static BotCardInfo Of(CardData data)
    {
        if (data == null) { return new BotCardInfo(); }

        if (!cache.TryGetValue(data, out BotCardInfo info))
        {
            info = Build(data);
            cache[data] = info;
        }

        return info;
    }

    public static BotCardInfo Of(Card card) => Of(card != null ? card.Data : null);

    public static bool IsTotemPrefab(GameObject prefab) => prefab != null && prefab.GetComponent<Totem>() != null;

    /// The statuses that stop damage on the carrier - a card applying one of these to an ally is Defense.
    public static bool IsDefensiveStatus(StatusType type) =>
        type == StatusType.Shield || type == StatusType.Block || type == StatusType.Parry
        || type == StatusType.Dodge || type == StatusType.Stealth || type == StatusType.DoubleShield;

    private static BotCardInfo Build(CardData data)
    {
        BotCardInfo info = new();

        foreach (CardEffectEntry entry in data.effectEntries)
        {
            switch (entry.effect)
            {
                case null:
                    break;
                case DamageEffect:
                    if (entry.aimsAt != EffectTarget.Source) { info.Mark(BotPlayCategory.Attack); }
                    break;
                case ShieldEffect:
                case BlockEffect:
                case ParryEffect:
                case TauntEffect:
                    info.Mark(BotPlayCategory.Defense);
                    break;
                case HealEffect:
                case CleansePoisonEffect:
                    info.Mark(BotPlayCategory.Heal);
                    break;
                case SummonEffect summon:
                    info.Mark(IsTotemPrefab(summon.SummonedObject) ? BotPlayCategory.Totem : BotPlayCategory.Summon);
                    break;
                case MoveEffect:
                    info.Mark(BotPlayCategory.Move);
                    break;
                case ApplyStatusEffect status:
                    if (IsDefensiveStatus(status.Status)) { info.Mark(BotPlayCategory.Defense); }
                    else if (status.Status == StatusType.Regeneration) { info.Mark(BotPlayCategory.Heal); }
                    else if (status.Audience == TargetAudience.Enemy) { info.Mark(BotPlayCategory.Debuff); }
                    else { info.Mark(BotPlayCategory.Buff); }
                    break;
                case ApplyTileEffect tile:
                    info.Mark(tile.Effect == TileEffectType.WallOfFlames ? BotPlayCategory.Attack : BotPlayCategory.Defense);
                    break;
                default:
                    info.Mark(BotPlayCategory.Utility);
                    break;
            }
        }

        foreach (BotPlayCategory category in PriorityOrder)
        {
            if (!info.Has(category)) { continue; }

            info.Primary = category;
            break;
        }

        return info;
    }

    private void Mark(BotPlayCategory category) => has[(int)category] = true;

    private static int RarityRank(Rarity rarity) => rarity switch
    {
        Rarity.Uncommon => 1,
        Rarity.Rare => 2,
        Rarity.Legendary => 3,
        _ => 0,
    };

    /// The profile's taste for a card's first category - BotRewardPrefs' pick multipliers.
    public static float PickMultiplier(BotPlayCategory category, BotRewardPrefs prefs) => category switch
    {
        BotPlayCategory.Attack => prefs.attackPick,
        BotPlayCategory.Defense => prefs.defensePick,
        BotPlayCategory.Heal => prefs.healPick,
        BotPlayCategory.Totem => prefs.totemPick,
        BotPlayCategory.Summon => prefs.summonPick,
        BotPlayCategory.Debuff => prefs.debuffPick,
        BotPlayCategory.Buff => prefs.buffPick,
        BotPlayCategory.Move => prefs.movePick,
        _ => prefs.utilityPick,
    };

    /// <summary>
    /// How much a card is worth to this profile away from any board: each entry's authored magnitude
    /// (after the entry's own adjustment, through the same ActionContext.Amount the real play uses)
    /// times the matching planner weight, per point of energy, nudged by keywords. Used to draft, to
    /// pick what to remove or upgrade, and to choose a discard. A heuristic on purpose - what a card is
    /// worth in a fight is BotPlanner's job, with the board in front of it.
    /// </summary>
    public static float Value(CardData data, BotProfileData profile)
    {
        if (data == null || profile == null) { return 0f; }

        BotWeights w = profile.weights;
        Card card = new(data);
        float raw = 0f;

        foreach (CardEffectEntry entry in card.EffectEntries)
        {
            if (entry.effect == null) { continue; }

            float area = entry.area.IsSingle ? 1f : 1.5f;
            ActionContext ctx = new(card, null, Array.Empty<GridTile>(), null, entry.amountDelta, entry.amountPercent);

            switch (entry.effect)
            {
                case DamageEffect damage:
                    raw += entry.aimsAt == EffectTarget.Source
                        ? ctx.Amount(damage.Damage) * w.selfDamage
                        : ctx.Amount(damage.Damage) * w.damage * area;
                    break;
                case ShieldEffect shield:
                    raw += ctx.Amount(shield.ShieldAmount) * w.mitigation * 0.8f * area;
                    break;
                case BlockEffect block:
                    raw += ctx.Amount(block.BlockCount) * BlockStatus.AmountPerHit * w.mitigation * 0.8f * area;
                    break;
                case ParryEffect parry:
                    raw += ctx.Amount(parry.ParryCharges) * 5f * w.mitigation * area;
                    break;
                case HealEffect heal:
                    raw += ctx.Amount(heal.RiderAmount) * w.heal * area;
                    break;
                case CleansePoisonEffect:
                    raw += 3f * w.heal;
                    break;
                case DrawEffect draw:
                    raw += ctx.Amount(draw.RiderAmount) * w.draw;
                    break;
                case EnergyEffect energy:
                    raw += energy.Amount * w.energy;
                    break;
                case ApplyStatusEffect status:
                    raw += StatusValue(status.Status, ctx.Amount(status.RiderAmount), status.Audience, w) * area;
                    break;
                case SummonEffect summon:
                    raw += SummonValue(summon.SummonedObject, w);
                    break;
                case MoveEffect:
                    raw += 3f * Mathf.Max(w.moveSetup, w.moveSafety);
                    break;
                case TauntEffect:
                    raw += 4f * w.mitigation;
                    break;
                case ApplyTileEffect tile:
                    raw += tile.Effect == TileEffectType.WallOfFlames
                        ? ctx.Amount(tile.Magnitude) * tile.Turns * w.dot * area
                        : 3f;
                    break;
                case SelfDamageEffect self:
                    raw += self.Amount * w.selfDamage;
                    break;
                case DiscardEffect:
                    raw += w.discard;
                    break;
                case PushEffect:
                case SwapEffect:
                    raw += 2f;
                    break;
            }
        }

        float value = raw / Mathf.Max(1, card.cost);

        foreach (CardKeyword keyword in card.Keywords)
        {
            value += keyword.type switch
            {
                CardKeywordType.Innate => 1f,
                CardKeywordType.Rebound => 2f,
                CardKeywordType.Cooldown => -0.5f * keyword.magnitude,
                CardKeywordType.Dormant => -1f,
                CardKeywordType.Interruptible => -0.5f,
                _ => 0f,
            };
        }

        return value;
    }

    /// Value × the profile's taste for the card's category, plus the rarity bonus - what drafting ranks by.
    public static float PickValue(CardData data, BotProfileData profile)
    {
        if (data == null || profile == null) { return float.MinValue; }

        return Value(data, profile) * PickMultiplier(Of(data).Primary, profile.rewards)
               + profile.rewards.rarityBonus * RarityRank(data.rarity);
    }

    public static float StatusValue(StatusType type, int stacks, TargetAudience audience, BotWeights w)
    {
        switch (type)
        {
            case StatusType.Poison: return Mathf.Min(40, stacks * (stacks + 1) / 2) * w.dot;
            case StatusType.Frozen: return 8f * w.threatRemoved;
            case StatusType.Weaken: return stacks * 2f * w.threatRemoved;
            case StatusType.Stealth:
            case StatusType.Dodge: return 6f * stacks * w.mitigation;
            case StatusType.Shield: return stacks * w.mitigation * 0.8f;
            case StatusType.Block: return stacks * BlockStatus.AmountPerHit * w.mitigation * 0.8f;
            case StatusType.Parry: return stacks * 5f * w.mitigation;
            case StatusType.Regeneration: return stacks * (stacks + 1) / 2f * w.heal;
            case StatusType.Strength: return stacks * StrengthStatus.AmountPerHit * w.buff * 0.5f;
            case StatusType.DoubleNextAttack: return stacks * 4f * w.buff;
        }

        return stacks * (audience == TargetAudience.Enemy ? w.debuff : w.buff);
    }

    public static float SummonValue(GameObject prefab, BotWeights w)
    {
        if (prefab == null) { return 0f; }

        if (IsTotemPrefab(prefab)) { return w.totemBase + 2f * w.totemCover; }

        return prefab.TryGetComponent(out Character body) ? body.MaxHealth * w.summon : 2f;
    }
}
#endif
