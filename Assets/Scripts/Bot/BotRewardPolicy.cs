#if UNITY_EDITOR || BOT_RUNNER
using System.Collections.Generic;

/// Which skip button a SkipReward is - the pilot remembers the last one it pressed so the removal
/// screen that follows knows whether it is removing or upgrading.
public enum BotSkipKind
{
    None = 0,
    Skip = 1,
    Heal = 2,
    Remove = 3,
    Upgrade = 4,
    Other = 5,
}

/// The bot's answer to a reward offer.
public sealed class BotRewardChoice
{
    /// "card", "equipment", "skip" or "decline" (ChooseSkip(null) - nothing at all).
    public string Kind = "decline";

    /// Index into the matching Offered* list, or -1 for decline.
    public int Index = -1;

    public string Name;
    public BotSkipKind Skip;
    public float Value;
    public string Reason;
}

/// <summary>
/// How the balance bot answers every choice outside a play: card and equipment offers (level clear and
/// mid-battle pickups), which card to remove or upgrade, and which card to discard.
///
/// Values come from BotCardInfo - each card's authored effects weighed by the profile's own weights and
/// its taste for categories - so a Totem-first profile drafts totems and a Defensive one drafts shields
/// without either needing rules of its own. The Random profile picks uniformly, from the per-decision
/// System.Random the pilot hands in.
/// </summary>
public static class BotRewardPolicy
{
    public static BotSkipKind KindOf(SkipReward skip) => skip switch
    {
        null => BotSkipKind.None,
        RemoveCardSkipReward => BotSkipKind.Remove,
        UpgradeCardSkipReward => BotSkipKind.Upgrade,
        HealSkipReward => BotSkipKind.Heal,
        NoneSkipReward => BotSkipKind.Skip,
        _ => BotSkipKind.Other,
    };

    /// <summary>
    /// Card offer: take the best card if it is worth at least takeCardsMinValue. Otherwise upgrade (when
    /// something upgradable would gain), remove (when the deck is past removeWhenDeckAbove), heal (when
    /// below healSkipBelowHp), plain skip - and only then fall back to the best card after all.
    /// Equipment offer: take the best item unless the profile refuses equipment.
    /// </summary>
    public static BotRewardChoice ChooseOffer(
        RewardPanel panel, IReadOnlyList<CardData> deck, BotProfileData profile, System.Random rng)
    {
        IReadOnlyList<CardData> cards = panel.OfferedCards;
        IReadOnlyList<EquipmentData> items = panel.OfferedEquipment;
        IReadOnlyList<SkipReward> skips = panel.OfferedSkips;
        Character receiver = panel.Receiver;

        if (profile.randomness >= 1f) { return Uniform(cards, items, skips, rng); }

        if (items.Count > 0)
        {
            int best = -1;
            float bestValue = float.MinValue;

            for (int i = 0; i < items.Count; i++)
            {
                float value = EquipmentValue(items[i], receiver);

                if (items[i] != null && value > bestValue)
                {
                    bestValue = value;
                    best = i;
                }
            }

            if (best >= 0 && profile.rewards.takeEquipment)
            {
                return new BotRewardChoice
                {
                    Kind = "equipment", Index = best, Name = items[best].equipmentName, Value = bestValue,
                    Reason = "best equipment",
                };
            }
        }

        int bestCard = -1;
        float bestCardValue = float.MinValue;

        for (int i = 0; i < cards.Count; i++)
        {
            float value = BotCardInfo.PickValue(cards[i], profile);

            if (cards[i] != null && value > bestCardValue)
            {
                bestCardValue = value;
                bestCard = i;
            }
        }

        if (bestCard >= 0 && bestCardValue >= profile.rewards.takeCardsMinValue)
        {
            return CardChoice(cards, bestCard, bestCardValue, "worth taking");
        }

        BotRewardChoice skip = ChooseSkip(skips, deck, receiver, profile);

        if (skip != null) { return skip; }

        if (bestCard >= 0) { return CardChoice(cards, bestCard, bestCardValue, "no better skip on offer"); }

        if (items.Count > 0) { return new BotRewardChoice { Kind = "equipment", Index = 0, Name = items[0].equipmentName }; }

        return new BotRewardChoice { Kind = "decline", Reason = "nothing to choose" };
    }

    private static BotRewardChoice CardChoice(IReadOnlyList<CardData> cards, int index, float value, string reason) =>
        new() { Kind = "card", Index = index, Name = cards[index].cardName, Value = value, Reason = reason };

    private static BotRewardChoice ChooseSkip(
        IReadOnlyList<SkipReward> skips, IReadOnlyList<CardData> deck, Character receiver, BotProfileData profile)
    {
        int Find(BotSkipKind kind)
        {
            for (int i = 0; i < skips.Count; i++)
            {
                if (KindOf(skips[i]) == kind) { return i; }
            }

            return -1;
        }

        BotRewardChoice Pick(int index, string reason) => new()
        {
            Kind = "skip", Index = index, Name = skips[index].Label, Skip = KindOf(skips[index]), Reason = reason,
        };

        int upgrade = Find(BotSkipKind.Upgrade);

        if (upgrade >= 0 && deck != null && profile.rewards.upgradeWeight > 0f && BestUpgrade(deck, null, profile, out _) > 0f)
        {
            return Pick(upgrade, "upgrade beats every offered card");
        }

        int remove = Find(BotSkipKind.Remove);

        if (remove >= 0 && deck != null && deck.Count > profile.rewards.removeWhenDeckAbove)
        {
            return Pick(remove, $"deck over {profile.rewards.removeWhenDeckAbove} cards");
        }

        int heal = Find(BotSkipKind.Heal);

        if (heal >= 0 && receiver != null && receiver.Health < receiver.MaxHealth * profile.rewards.healSkipBelowHp)
        {
            return Pick(heal, "low on health");
        }

        int plain = Find(BotSkipKind.Skip);

        if (plain >= 0) { return Pick(plain, "nothing worth taking"); }

        return heal >= 0 ? Pick(heal, "nothing worth taking") : null;
    }

    private static BotRewardChoice Uniform(
        IReadOnlyList<CardData> cards, IReadOnlyList<EquipmentData> items, IReadOnlyList<SkipReward> skips,
        System.Random rng)
    {
        int total = cards.Count + items.Count + skips.Count;

        if (total == 0) { return new BotRewardChoice { Kind = "decline", Reason = "random: nothing offered" }; }

        int pick = rng.Next(total);

        if (pick < cards.Count) { return CardChoice(cards, pick, 0f, "random"); }

        pick -= cards.Count;

        if (pick < items.Count)
        {
            return new BotRewardChoice { Kind = "equipment", Index = pick, Name = items[pick].equipmentName, Reason = "random" };
        }

        pick -= items.Count;

        return new BotRewardChoice
        {
            Kind = "skip", Index = pick, Name = skips[pick].Label, Skip = KindOf(skips[pick]), Reason = "random",
        };
    }

    /// Rarity, plus a bonus for filling an empty slot, minus the rarity of whatever it would replace.
    public static float EquipmentValue(EquipmentData item, Character receiver)
    {
        if (item == null) { return float.MinValue; }

        float value = 3f * Rank(item.rarity);
        EquipmentData replaced = receiver != null && !EquipmentSlots.IsUnlimited(item.slot)
            ? receiver.EquippedIn(item.slot)
            : null;

        return replaced != null ? value - 3f * Rank(replaced.rarity) : value + 2f;
    }

    private static int Rank(Rarity rarity) => rarity switch
    {
        Rarity.Uncommon => 1,
        Rarity.Rare => 2,
        Rarity.Legendary => 3,
        _ => 0,
    };

    /// <summary>
    /// The deck index whose upgrade gains the most value, and that gain (0 or less when nothing gains).
    /// `eligible` narrows it to what the panel will actually accept; null means "has an upgradedForm".
    /// </summary>
    public static float BestUpgrade(
        IReadOnlyList<CardData> deck, System.Predicate<int> eligible, BotProfileData profile, out int index)
    {
        index = -1;
        float best = float.MinValue;

        for (int i = 0; i < deck.Count; i++)
        {
            CardData card = deck[i];

            if (card == null || card.upgradedForm == null) { continue; }
            if (eligible != null && !eligible(i)) { continue; }

            float gain = (BotCardInfo.PickValue(card.upgradedForm, profile) - BotCardInfo.PickValue(card, profile))
                         * profile.rewards.upgradeWeight;

            if (gain > best)
            {
                best = gain;
                index = i;
            }
        }

        return index >= 0 ? best : 0f;
    }

    /// <summary>
    /// The removal screen: upgrade the best-gaining card, remove the least valuable one, or -1 to cancel
    /// (which re-offers the whole reward). `mode` is the skip the pilot pressed to get here.
    /// </summary>
    public static int ChooseFromDeck(CardRemovalPanel panel, BotSkipKind mode, BotProfileData profile, System.Random rng)
    {
        IReadOnlyList<CardData> deck = panel.ShownDeck;
        List<int> eligible = new();

        for (int i = 0; i < deck.Count; i++)
        {
            if (deck[i] != null && panel.IsEligible(i)) { eligible.Add(i); }
        }

        if (eligible.Count == 0) { return -1; }

        if (profile.randomness >= 1f) { return eligible[rng.Next(eligible.Count)]; }

        if (mode == BotSkipKind.Upgrade)
        {
            BestUpgrade(deck, panel.IsEligible, profile, out int index);
            return index;
        }

        int worst = -1;
        float worstValue = float.MaxValue;

        foreach (int i in eligible)
        {
            float value = BotCardInfo.PickValue(deck[i], profile);

            if (value < worstValue)
            {
                worstValue = value;
                worst = i;
            }
        }

        return worst;
    }

    /// The discard screen: the least valuable card in hand.
    public static int ChooseDiscard(IReadOnlyList<Card> hand, BotProfileData profile, System.Random rng)
    {
        if (hand == null || hand.Count == 0) { return -1; }

        if (profile.randomness >= 1f) { return rng.Next(hand.Count); }

        int worst = 0;
        float worstValue = float.MaxValue;

        for (int i = 0; i < hand.Count; i++)
        {
            if (hand[i] == null) { continue; }

            float value = BotCardInfo.PickValue(hand[i].Data, profile);

            if (value < worstValue)
            {
                worstValue = value;
                worst = i;
            }
        }

        return worst;
    }
}
#endif
