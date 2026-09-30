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
