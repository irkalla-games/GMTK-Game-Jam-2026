#if UNITY_EDITOR || BOT_RUNNER
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What kind of play a card is, for the balance bot's "play these first" lists and for the report's
/// play-mix tables. A card can be several at once - Steely Attack is Attack and Defense - and reads as
/// its first in BotCardInfo.PriorityOrder when only one is wanted.
///
/// Serialized into BotProfile assets by value, so append only - never reorder.
/// </summary>
public enum BotPlayCategory
{
    Attack = 0,
    Defense = 1,
    Heal = 2,
    Totem = 3,
    Summon = 4,
    Debuff = 5,
    Buff = 6,
    Move = 7,
    Utility = 8,
}

/// <summary>
/// How much the planner values each thing a play can achieve. A candidate play's score is the sum of
/// weight × feature over every row here, where each feature is measured with the game's own previews
/// (Card.PreviewDamage, Card.LandedTiles) - see BotPlanner. Negative-meaning rows (friendly fire,
/// self-damage, discards) are written as negative weights on positive features.
/// </summary>
[Serializable]
public class BotWeights
{
    [Tooltip("Per point of health an enemy would actually lose (capped at what it has).")]
    public float damage = 1f;

    [Tooltip("Per enemy the play would kill outright.")]
    public float kill = 6f;

    [Tooltip("Per point of this round's committed enemy damage the play cancels - by killing, freezing, "
             + "weakening, or pushing an attacker out of reach.")]
    public float threatRemoved = 1f;

    [Tooltip("Per point of damage the play would do to our own side. Negative.")]
    public float friendlyFire = -2f;

    [Tooltip("Per point of incoming damage a shield, block, parry, dodge, stealth or taunt would stop, "
             + "counted only against damage actually aimed at that ally this round.")]
    public float mitigation = 1f;

    [Tooltip("Per point of shield or block beyond what is aimed at that ally - banked for later, but "
             + "Shield wipes at the next turn start, so keep this low.")]
    public float mitigationSpare = 0.1f;

    [Tooltip("Per point of missing health restored, scaled up when the ally is under threat.")]
    public float heal = 0.8f;

    [Tooltip("Per ally who would fall to this round's intents without the play and survives with it.")]
    public float lifeSaved = 25f;

    [Tooltip("Per point of damage over time: poison ticks, Wall of Flames.")]
    public float dot = 0.6f;

    [Tooltip("Per stack of a curse not modelled more precisely above.")]
    public float debuff = 1.5f;

    [Tooltip("Per stack of a buff on our side not modelled more precisely above.")]
    public float buff = 1.5f;

    [Tooltip("Per ally a placed totem's aura would cover (a hero counts 1, a summon or totem 0.5).")]
    public float totemCover = 4f;

    [Tooltip("Flat value of placing a totem at all.")]
    public float totemBase = 2f;

    [Tooltip("Per point of max health a non-totem summon brings.")]
    public float summon = 0.3f;

    [Tooltip("Per point of damage the hero could deal after moving - the best follow-up from the new "
             + "tile - plus closing on its preferred range.")]
    public float moveSetup = 0.6f;

    [Tooltip("Per point of this round's intent damage a move steps out of reach of.")]
    public float moveSafety = 0.6f;

    [Tooltip("Per loot item on the destination tile - a pickup is a reward offer.")]
    public float loot = 8f;

    [Tooltip("For stepping into a friendly totem's aura (and against stepping out of one).")]
    public float auraMove = 2f;

    [Tooltip("Per card drawn.")]
    public float draw = 2.5f;

    [Tooltip("Per point of energy gained, when the hand holds something that energy would pay for.")]
    public float energy = 2.5f;

    [Tooltip("Per point of health the play costs its own hero. Negative.")]
    public float selfDamage = -1f;

    [Tooltip("Per card the play makes you discard. Negative.")]
    public float discard = -2f;

    [Tooltip("Subtracted per point of energy cost, so a free play beats an equal paid one.")]
    public float costPenalty = 0.25f;

    // Appended after the first presets shipped - a profile or job written before these existed reads
    // them as their initialisers below, which are all "off", so it plays exactly as it did.

    [Tooltip("Per point of damage a killed enemy would have dealt over the rest of the battle (its per-"
             + "round damage x rounds left after this one, capped by Future Rounds Cap). What makes an "
             + "early kill worth more than a late one.")]
    public float futureThreat;

    [Tooltip("Per tile a move brings the hero closer to the nearest loot on the board.")]
    public float lootApproach;

    [Tooltip("Per open side a move closes: neighbouring tiles an enemy could stand on to hit the hero, "
             + "before minus after. A corner beside an ally has two; the middle of the board has eight.")]
    public float exposure;

    [Tooltip("Per point of damage stopped, scaled by how much the ally needs it: what this round's incoming "
             + "would add up to over the rounds left, against the health they have, capped at Need Cap. A hero "
             + "who can soak the rest of the battle standing needs none of it - spend the energy killing. "
             + "Counts on top of Mitigation, so lower that row when raising this one.")]
    public float mitigationNeed;

    [Tooltip("Per point of incoming a move takes off our side, need-weighted like Mitigation Need - measured "
             + "by stepping the hero there and asking every enemy's own brain where it would now hit. Falls "
             + "a move causes or prevents this round count through Life Saved. The exact version of Move "
             + "Safety and Exposure; zero those when using it.")]
    public float forecast;

    private static System.Reflection.FieldInfo[] fields;

    /// Every float row, a + (b - a) x t - how an adaptive profile slides from its aggressive weights to
    /// its defensive ones. Reflected once, so a row added later blends with no edit here.
    public static BotWeights Lerp(BotWeights a, BotWeights b, float t)
    {
        fields ??= Array.FindAll(typeof(BotWeights).GetFields(), f => f.FieldType == typeof(float));

        BotWeights result = new();

        foreach (System.Reflection.FieldInfo field in fields)
        {
            float from = (float)field.GetValue(a);
            float to = (float)field.GetValue(b);
            field.SetValue(result, from + (to - from) * t);
        }

        return result;
    }

    public BotWeights Clone() => Lerp(this, this, 0f);
}

/// A measured pick bonus for one card, by card name - see BotRewardPrefs.cardRatings.
[Serializable]
public class BotCardRating
{
    public string card;

    [Tooltip("Added to the card's pick value (times Rating Weight). Positive means runs that took it did "
             + "better than runs that passed on it.")]
    public float bonus;
}

/// <summary>
/// How the bot answers the reward screens between and during battles - see BotRewardPolicy.
/// </summary>
[Serializable]
public class BotRewardPrefs
{
    [Tooltip("Multipliers on a card's value by its first category - how a profile's taste shows up in "
             + "what it drafts. 1 is neutral.")]
    public float attackPick = 1f;
    public float defensePick = 1f;
    public float healPick = 1f;
    public float totemPick = 1f;
    public float summonPick = 1f;
    public float debuffPick = 1f;
    public float buffPick = 1f;
    public float movePick = 0.6f;
    public float utilityPick = 1f;

    [Tooltip("Added per rarity step: Common 0, Uncommon 1, Rare 2, Legendary 3.")]
    public float rarityBonus = 1f;

    [Tooltip("A card is taken when its value reaches this; below it the bot upgrades, removes, heals or "
             + "skips instead. Value is roughly 'weighted effect per point of energy'.")]
    public float takeCardsMinValue = 4f;

    [Tooltip("Choose 'Heal' on a pickup only below this fraction of max health.")]
    [Range(0f, 1f)] public float healSkipBelowHp = 0.5f;

    [Tooltip("Multiplier on the value gained by upgrading a card; 0 never upgrades.")]
    public float upgradeWeight = 1f;

    [Tooltip("'Remove a card' is chosen only while the deck is bigger than this.")]
    public int removeWhenDeckAbove = 14;

    [Tooltip("Take offered equipment at all.")]
    public bool takeEquipment = true;

    // Appended - a profile or job written before these existed reads them as off.

    [Tooltip("Levels (1-based, inclusive) that count as early: the deck is still being built, so cards "
             + "are taken at Take Cards Min Value. 0 turns the early/late split off.")]
    public int earlyLevels;

    [Tooltip("After the early levels: a card has to be worth this much to be taken.")]
    public float lateTakeCardsMinValue = 4f;

    [Tooltip("After the early levels: 'Remove a card' once the deck is bigger than this.")]
    public int lateRemoveWhenDeckAbove = 14;

    [Tooltip("Multiplier on the Loot weight after the early levels - how much a detour for a pickup is "
             + "still worth once the deck is built.")]
    public float lateLootScale = 1f;

    [Tooltip("Scales every Card Ratings bonus. 0 ignores them.")]
    public float ratingWeight;

    [Tooltip("Pick bonuses measured from batch data, by card or equipment name - "
             + "Tools/BotRunner/Rate-Rewards.py writes these.")]
    public List<BotCardRating> cardRatings = new();

    [Tooltip("Chance per reward screen of a uniformly random choice instead of the policy's - for "
             + "measuring what each card and item is worth (Rate-Rewards.py), never for real play. The "
             + "battles themselves are still played by the profile.")]
    [Range(0f, 1f)] public float exploreRewards;

    public bool IsEarly(int level) => earlyLevels <= 0 || level <= earlyLevels;

    public float TakeCardsMinValue(int level) => IsEarly(level) ? takeCardsMinValue : lateTakeCardsMinValue;

    public int RemoveWhenDeckAbove(int level) => IsEarly(level) ? removeWhenDeckAbove : lateRemoveWhenDeckAbove;

    public float LootScale(int level) => IsEarly(level) ? 1f : lateLootScale;

    public float Rating(CardData card) => card != null ? Rating(card.cardName) : 0f;

    public float Rating(EquipmentData item) => item != null ? Rating(item.equipmentName) : 0f;

    private float Rating(string name)
    {
        if (ratingWeight == 0f || cardRatings == null) { return 0f; }

        foreach (BotCardRating rating in cardRatings)
        {
            if (rating != null && rating.card == name) { return rating.bonus * ratingWeight; }
        }

        return 0f;
    }
}

/// <summary>
/// One complete play style. Plain data, so a batch can carry it by value in job.json - a headless
/// player cannot load the asset, and a report should say exactly which numbers produced it.
/// </summary>
[Serializable]
public class BotProfileData
{
    [HideInInspector]
    [Tooltip("Filled from the asset's name when a job is built - see BotProfile.Snapshot.")]
    public string name = "Balanced";

    [TextArea(2, 5)]
    public string notes;

    public BotWeights weights = new();

    [Tooltip("Categories to play first whenever a legal play of that kind scores at least Play First Min "
             + "Score - in list order. Empty plays purely by score.")]
    public List<BotPlayCategory> playFirst = new();

    [Tooltip("A play-first candidate still has to score this much. Very negative means 'always'.")]
    public float playFirstMinScore;

    [Tooltip("End the turn when the best play scores below this.")]
    public float passThreshold = 0.5f;

    [Tooltip("Chance per decision of a uniformly random legal play instead of the best one. 1 is the "
             + "Random baseline.")]
    [Range(0f, 1f)] public float randomness;

    [Tooltip("When playing randomly, the chance of ending the turn instead.")]
    [Range(0f, 1f)] public float endTurnChance = 0.15f;

    [Tooltip("A baseline profile (Random) is left out of the report's enemy and level tables, so it "
             + "cannot drag the balance numbers around.")]
    public bool baseline;

    public BotRewardPrefs rewards = new();

    // Appended - a profile or job written before these existed reads them as off: greedy, one weight set.

    [Header("Adaptive")]
    [Tooltip("Slide from Weights (aggressive) to Late Weights (defensive) as the battle goes on - see "
             + "BotAggression.")]
    public bool adaptive;

    [Tooltip("The defensive end of an adaptive profile. Ignored unless Adaptive is on.")]
    public BotWeights lateWeights = new();

    [Tooltip("Fraction of the battle's turns played fully aggressive.")]
    [Range(0f, 1f)] public float aggressiveUntil = 0.4f;

    [Tooltip("Fraction of the battle's turns from which play is fully defensive.")]
    [Range(0f, 1f)] public float defensiveFrom = 0.8f;

    [Tooltip("Below this fraction of the party's total health, aggression is scaled down in proportion.")]
    [Range(0f, 1f)] public float lowHpGuard = 0.5f;

    [Tooltip("More living enemies than this per hero scales aggression down - a crowd is survived, "
             + "not raced. 0 turns it off.")]
    public float crowdGuard;

    [Tooltip("Aggression never drops below this, even on the last turn - a kill is still a kill.")]
    [Range(0f, 1f)] public float minAggression;

    [Tooltip("Rounds of future damage a kill is credited with at most (Future Threat).")]
    public int futureRoundsCap = 6;

    [Header("Search")]
    [Tooltip("Plays deep to plan the turn as a sequence - 0 is the original one-play greedy choice.")]
    public int searchDepth;

    [Tooltip("Partial turns kept at each depth.")]
    public int beamWidth = 6;

    [Tooltip("Best next plays expanded from each kept partial turn.")]
    public int branching = 8;

    [Header("Triage and forecast")]
    [Tooltip("Most a point of damage stopped can be worth under Mitigation Need, as a multiple: an ally "
             + "whose incoming x rounds left comes to twice their health is no more urgent than this.")]
    public float needCap = 1.5f;

    [Tooltip("Floor on the need multiplier - how much a point stopped on an ally who will easily survive "
             + "the battle is still worth.")]
    [Range(0f, 1f)] public float needFloor = 0.1f;

    [Tooltip("Best-scoring moves per decision measured with the forecast (when Forecast is weighted). The "
             + "rest keep their cheap score. Each one re-plans every enemy on the board.")]
    public int forecastMoves = 12;

    [Tooltip("Count Poison as the damage it is: what an ally carries now (it bites before enemies act, and "
             + "nothing blocks it) against their health, and what an enemy's attack would add as part of that "
             + "enemy's threat. Off, a Light Bandit reads as the 1 damage its Venom Cut previews.")]
    public bool countPoison;

    /// A deep copy through JsonUtility - the same serializer the job file uses, so a copy can never
    /// carry anything a job would not.
    public BotProfileData Clone() => JsonUtility.FromJson<BotProfileData>(JsonUtility.ToJson(this));
}

/// <summary>
/// A tunable play style for the balance bot, edited in the Inspector. Create one from
/// Assets > Create > Bot > Bot Profile, or duplicate a preset in Assets/Data/Bots.
///
/// Only a container: a batch snapshots Data into its job.json when it starts, so editing an asset
/// mid-batch changes nothing that is running, and a replay never needs the asset at all.
/// </summary>
[CreateAssetMenu(menuName = "Bot/Bot Profile")]
public class BotProfile : ScriptableObject
{
    [SerializeField] private BotProfileData data = new();

    public BotProfileData Data => data;

    /// A copy named after this asset, so renaming the asset renames the profile in every report.
    public BotProfileData Snapshot()
    {
        BotProfileData copy = data.Clone();
        copy.name = name;
        return copy;
    }

    /// Replaces the authored data - for creating the preset assets from BotPresets in the Editor, outside
    /// Play Mode. Never call this at runtime: a ScriptableObject written during Play Mode keeps the write
    /// on disk.
    public void SetData(BotProfileData source)
    {
        data = source != null ? source.Clone() : new BotProfileData();
    }
}
#endif
