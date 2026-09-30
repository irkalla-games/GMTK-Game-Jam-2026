#if UNITY_EDITOR || BOT_RUNNER
using System.Collections.Generic;

/// <summary>
/// The reference play styles. The five assets in Assets/Data/Bots were created from these once and are
/// yours to tune from then on - these stay as the defaults a job falls back to when it names no asset,
/// and as the record of what each preset started as.
///
/// BotWeights' field initialisers are Balanced; every other preset is Balanced with a handful of rows
/// moved, so the differences below are the whole personality.
/// </summary>
public static class BotPresets
{
    public static List<BotProfileData> All() => new()
    {
        Balanced(), Aggressive(), Defensive(), TotemFirst(), RandomBaseline(),
    };

    public static BotProfileData Balanced() => new()
    {
        name = "Balanced",
        notes = "Plays whatever scores best on damage, kills, blocking what is actually incoming, healing "
                + "and setting up. The reference every other profile is compared against.",
    };

    public static BotProfileData Aggressive()
    {
        BotProfileData p = Balanced();
        p.name = "Aggressive";
        p.notes = "Hits first and hardest; defends only when it is cheap. Takes damage and debuff cards.";

        BotWeights w = p.weights;
        w.damage = 1.5f;
        w.kill = 10f;
        w.threatRemoved = 0.8f;
        w.friendlyFire = -1f;
        w.mitigation = 0.5f;
        w.mitigationSpare = 0.05f;
        w.heal = 0.4f;
        w.lifeSaved = 15f;
        w.dot = 0.8f;
        w.debuff = 1f;
        w.buff = 2f;
        w.totemCover = 2f;
        w.totemBase = 1f;
        w.moveSetup = 0.9f;
        w.moveSafety = 0.3f;
        w.loot = 6f;
        w.auraMove = 1f;
        w.selfDamage = -0.6f;

        p.playFirst.Add(BotPlayCategory.Attack);

        p.rewards.attackPick = 1.5f;
        p.rewards.debuffPick = 1.2f;
        p.rewards.takeCardsMinValue = 3f;

        return p;
    }

    public static BotProfileData Defensive()
    {
        BotProfileData p = Balanced();
        p.name = "Defensive";
        p.notes = "Shields, blocks and heals before anything else; attacks with what is left over.";

        BotWeights w = p.weights;
        w.damage = 0.7f;
        w.kill = 5f;
        w.threatRemoved = 1.4f;
        w.friendlyFire = -3f;
        w.mitigation = 1.6f;
        w.mitigationSpare = 0.3f;
        w.heal = 1.3f;
        w.lifeSaved = 40f;
        w.dot = 0.5f;
        w.debuff = 2f;
        w.buff = 1f;
        w.summon = 0.4f;
        w.moveSetup = 0.4f;
        w.moveSafety = 1f;
        w.auraMove = 3f;
        w.selfDamage = -1.5f;

        p.playFirst.Add(BotPlayCategory.Defense);
        p.playFirst.Add(BotPlayCategory.Heal);

        p.rewards.defensePick = 1.5f;
        p.rewards.healPick = 1.4f;

        return p;
    }

    public static BotProfileData TotemFirst()
    {
        BotProfileData p = Balanced();
        p.name = "TotemFirst";
        p.notes = "Always places a totem when it legally can, wherever it covers the most allies; plays "
                  + "Balanced otherwise. Drafts totems over everything.";

        BotWeights w = p.weights;
        w.totemCover = 8f;
        w.totemBase = 6f;
        w.auraMove = 3f;

        p.playFirst.Add(BotPlayCategory.Totem);
        p.playFirstMinScore = -1000f;

        p.rewards.totemPick = 3f;
        p.rewards.takeCardsMinValue = 3f;

        return p;
    }

    public static BotProfileData RandomBaseline()
    {
        BotProfileData p = Balanced();
        p.name = "Random";
        p.notes = "Uniformly random legal plays and reward picks, ending the turn 15% of the time. The floor "
                  + "every real profile has to beat - a profile that does not is broken, not unlucky.";
        p.randomness = 1f;
        p.endTurnChance = 0.15f;
        p.baseline = true;

        return p;
    }
}
#endif
