#if UNITY_EDITOR || BOT_RUNNER
using System.Collections.Generic;

/// <summary>
/// The reference play styles. The assets in Assets/Data/Bots were created from these once and are
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
        Balanced(), Aggressive(), Defensive(), TotemFirst(), Tuned(), Adaptive(), Foresight(), Veteran(), RandomBaseline(),
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

    /// <summary>
    /// The best measured play style for data collection (see Docs/BotStrategy.md for the batches):
    /// Defensive's weights with no play-first override, a stronger pull out of reach (Move Safety) and
    /// into covered positions (Exposure), whole-turn planning, and reward picks steered by measured
    /// ratings. The ratings themselves are data, not code - they live on the Tuned asset and are
    /// re-measured with Tools/BotRunner/Rate-Rewards.py; this preset carries only their weight.
    ///
    /// Why defensive: battles are won by surviving TurnsToSurvive, and from level 3 on the party kills
    /// only 5-18% of what spawns - the board cannot be thinned, so every point stopped counts and a point
    /// dealt mostly does not.
    /// </summary>
    public static BotProfileData Tuned()
    {
        BotProfileData p = Defensive();
        p.name = "Tuned";
        p.notes = "Measured best for balance data: defensive weights, steps out of reach and into covered "
                  + "spots, plans whole turns, drafts by measured reward ratings.";

        p.playFirst.Clear();
        p.weights.moveSafety = 1.5f;
        p.weights.exposure = 2f;
        p.searchDepth = 6;
        p.rewards.ratingWeight = 20f;

        return p;
    }

    /// <summary>
    /// Tuned, but kill-hungry while the battle is young and settling into Tuned's defence as it closes:
    /// the "aggressive early, defensive late" idea - a kill early saves many rounds of damage, a kill on
    /// the last turn saves one. Early aggression only adds kill pressure (Kill, Future Threat); it never
    /// lowers the defensive rows, because lowering them measured worse still.
    ///
    /// Measured (Docs/BotStrategy.md): slightly behind Tuned (-0.03 to -0.11 levels cleared), where the
    /// board cannot be thinned and energy spent chasing kills is energy not spent blocking. It led for
    /// Rogue parties only while Veil Totem could hide the whole party (fixed 2026-10-09); after the fix
    /// it ties Tuned there. Kept so the comparison can be re-run as content changes.
    /// </summary>
    public static BotProfileData Adaptive()
    {
        BotProfileData p = Tuned();
        p.name = "Adaptive";
        p.notes = "Tuned, plus kill pressure early in each battle that fades into Tuned's defence by the "
                  + "closing rounds or when the party is low.";

        p.lateWeights = p.weights.Clone();
        p.weights.kill = 8f;
        p.weights.futureThreat = 0.35f;

        p.adaptive = true;
        p.aggressiveUntil = 0.4f;
        p.defensiveFrom = 0.8f;
        p.lowHpGuard = 0.5f;
        p.futureRoundsCap = 6;

        return p;
    }

    /// <summary>
    /// Tuned, reading the board the way a careful player does (Docs/BotStrategy.md, Oct 2026 pass):
    ///   - Triage (Mitigation Need): a point blocked is worth what the ally needs it - this round's incoming
    ///     as if it kept coming every round left, against their health. Health resets between levels, so
    ///     damage a hero can soak until the battle ends costs nothing, and the energy goes into kills.
    ///   - Forecast: before a move, step the hero there and ask every enemy's own brain where it would now
    ///     hit, instead of guessing from reach and open sides (Move Safety and Exposure are off).
    ///   - Count Poison: what a hero carries bites before enemies act and nothing blocks it, and an
    ///     enemy's poison is part of its threat - without this a Light Bandit reads as 1 damage.
    /// The three only pay together: each alone measured within noise of Tuned.
    /// </summary>
    public static BotProfileData Foresight()
    {
        BotProfileData p = Tuned();
        p.name = "Foresight";
        p.notes = "Tuned, plus triage (blocks only what a hero needs blocked), an exact forecast of where "
                  + "enemies will hit after a move, and Poison counted as the damage it is.";

        p.weights.mitigation = 0.9f;
        p.weights.mitigationNeed = 1.0f;
        p.weights.forecast = 1.5f;
        p.weights.moveSafety = 0f;
        p.weights.exposure = 0f;
        p.countPoison = true;

        return p;
    }

    /// <summary>
    /// Foresight, drafting like a player who knows the deck is the weapon (Docs/BotStrategy.md, pass 3):
    ///   - Lean drafting: a card has to be worth 5 to be taken (Foresight takes at 4); below that an upgrade,
    ///     a removal once the deck is over 11, or the pickup's heal is better. +0.31 levels over Foresight.
    ///   - Exact Weaken: Weaken is 3 off each hit this round, so a Dull Blade that keeps an ally standing
    ///     counts as a saved life - the Mage and Rogue starters carry five Weaken cards between them.
    ///     +0.18 on top of lean drafting (+0.3 for parties with a Mage or Rogue, 0 for Knight + Cleric).
    ///   - On-hit buffs counted: Leeching Blade is worth the healing it will return, not a generic buff.
    ///     Measured within noise of not counting them; kept because it is the correct reading.
    /// Focus (damage weighted by the target's threat per health) and Hunt (moving towards it) were tested
    /// on top and did not pay: the dangerous enemies stand in the back line and waves keep coming.
    /// </summary>
    public static BotProfileData Veteran()
    {
        BotProfileData p = Foresight();
        p.name = "Veteran";
        p.notes = "Foresight, plus lean drafting (take a card only at value 5, else upgrade, remove or heal), "
                  + "Weaken measured as the cut it is, and on-hit buffs counted as what they return.";

        p.rewards.takeCardsMinValue = 5f;
        p.rewards.lateTakeCardsMinValue = 5f;
        p.rewards.removeWhenDeckAbove = 11;
        p.rewards.lateRemoveWhenDeckAbove = 11;
        p.exactWeaken = true;
        p.countOnHit = true;

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
