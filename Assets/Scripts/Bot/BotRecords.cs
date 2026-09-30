#if UNITY_EDITOR || BOT_RUNNER
using System;
using System.Collections.Generic;

/// <summary>
/// Everything one bot run produced - written as runs/&lt;run id&gt;/run.json and read back by BotReport and
/// BotReplay. Plain public fields and lists only, because JsonUtility is the serializer: no
/// dictionaries, no properties, no polymorphism.
///
/// Damage conventions, everywhere below: "dealt"/"taken" is what registered - health lost plus what a
/// Shield absorbed into its pool. "prevented" is what Block, Parry and Dodge stopped outright.
/// Unblockable damage (poison, flames, self-damage) counts in dealt/taken too, credited to whoever
/// applied it where that is known.
/// </summary>
[Serializable]
public class BotRunRecord
{
    public int version = 1;
    public string runId;
    public string batchId;
    public string profile;
    public bool baseline;
    public int runIndex;
    public int profileIndex;
    public int seed;
    public string campaign;
    public int tier;
    public List<BotPartySlot> party = new();
    public string partyLabel;
    public string commit;
    public bool dirtyTree;
    public string speed;
    public float turboStep;
    public string startedUtc;

    /// Won, Died, Stalled, Error, Cancelled, Diverged - or Running while it is being written.
    public string outcome = "Running";
    public string outcomeNote;
    public int levelsCleared;

    /// 1-based level the run ended on when it did not win, else -1.
    public int endLevel = -1;
    public string endLevelName;
    public int endRound = -1;

    /// Who landed the killing blows in the final wipe - enemy types.
    public List<string> killers = new();

    public int dmgDealt;
    public int dmgTaken;
    public int healing;
    public int shieldAbsorbed;
    public int prevented;
    public int falls;
    public int heroesLost;
    public int cardsPlayed;
    public int energyUnspent;
    public int decisions;
    public int errors;
    public int warnings;
    public int plannerRefusals;
    public int playCapHits;
    public int rewardsTaken;
    public float plannerMsAvg;
    public float plannerMsP95;
    public float plannerMsMax;
    public float wallSeconds;
    public int frames;

    /// Hash over every decision fingerprint and choice - two runs with equal digests played identically.
    public string digest;

    public List<BotLevelRecord> levels = new();
    public List<BotEnemyStat> enemies = new();
    public List<BotCardStat> cards = new();
    public List<BotHeroStat> heroes = new();
    public List<BotRewardRecord> rewards = new();
    public List<BotErrorRecord> errorList = new();

    /// The most frequent warnings this run logged, by message - content noise worth knowing about.
    public List<BotWarningRecord> warningList = new();
}

[Serializable]
public class BotWarningRecord
{
    public string message;
    public int count;
}

[Serializable]
public class BotLevelRecord
{
    /// 1-based.
    public int level;
    public string name;
    public bool boss;

    /// Cleared, Won (the final level), Died, Stalled, Cancelled, Error.
    public string result;
    public int rounds;
    public int dmgDealt;
    public int dmgTaken;
    public int healing;
    public int shieldAbsorbed;
    public int prevented;
    public int falls;
    public int enemiesOpening;
    public int enemiesWaves;
    public int enemiesSummoned;
    public int enemiesKilled;
    public int hpStart;
    public int hpMaxStart;
    public int hpEnd;
    public int cardsPlayed;
    public int energyUnspent;

    /// Every enemy type that appeared this battle, once each.
    public List<string> enemyTypes = new();
    public List<BotFallRecord> fallList = new();
}

[Serializable]
public class BotFallRecord
{
    public string hero;
    public int round;
    public string killer;
}

[Serializable]
public class BotEnemyStat
{
    public string type;
    public bool boss;
    public float power;

    /// Bodies of this type that took the field, and battles it appeared in.
    public int bodies;
    public int battles;

    public int dmgToHeroes;
    public int dmgToAllies;
    public int prevented;

    /// Hero falls where this type landed the last hit.
    public int fallsCaused;

    /// Bodies of this type the party killed, the rounds they had been alive for, and damage they took.
    public int killed;
    public int roundsAlive;
    public int dmgTaken;
}

[Serializable]
public class BotCardStat
{
    public string card;
    public string category;
    public string rarity;
    public int cost;
    public int offered;
    public int picked;
    public int played;
    public float scoreSum;
    public int dmgDealt;
}

[Serializable]
public class BotHeroStat
{
    public string hero;
    public int dmgDealt;
    public int dmgTaken;
    public int healingDone;
    public int healingReceived;
    public int shieldAbsorbed;
    public int prevented;
    public int falls;
    public int cardsPlayed;
    public int energyUnspent;
}

[Serializable]
public class BotRewardRecord
{
    public int level;
    public int round;
    public string hero;

    /// "clear" (after a level) or "pickup" (loot mid-battle).
    public string occasion;

    /// card, equipment, skip, decline - and for skips, what the removal screen then did.
    public string kind;
    public string choice;
    public string offered;
}

[Serializable]
public class BotErrorRecord
{
    public int level;
    public int round;
    public string type;
    public string message;
    public string stack;
    public int count;
}

/// <summary>
/// One line of events.jsonl. A flat bag of optional fields rather than a class per kind, because
/// JsonUtility has no polymorphism and a replay has to read these back in order.
///
/// kind: run, level, decision, levelEnd, runEnd, error. A decision's action: play, endTurn, card,
/// equipment, skip, decline, remove, upgrade, cancel, discard, ack.
/// </summary>
[Serializable]
public class BotEvent
{
    public string kind;
    public int level;
    public int round;
    public int step;

    /// Decisions only: the fingerprint hash, and the canonical text it hashes - the replay diffs these.
    public string fp;
    public string fpText;

    public string action;
    public string hero;
    public int hand = -1;
    public string card;
    public int x = -1;
    public int y = -1;
    public int aim;
    public int index = -1;
    public string choice;
    public float score;
    public string note;
}
#endif
