#if UNITY_EDITOR || BOT_RUNNER
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Watches one bot run and writes down what happened: who hit whom and for how much, who fell and to
/// what, which enemies turned up, what was drafted, what the bot decided and why. Builds the
/// BotRunRecord that becomes run.json, and writes turns.log and events.jsonl as it goes (BotTurnLog).
///
/// Listens rather than asks. Damage comes from Character.HitResolved (an attack, with its attacker) and
/// DamagedUnblockable (poison, flames, self-damage, which carry no attacker - see ClassifyUnblockable),
/// bodies from BattleManager.CharacterJoined, deaths from Character.Died, everything an action did from
/// ActionManager.ActionResolved. Hooked in the Game scene's sceneLoaded, before BattleManager.Start, so
/// the opening spawn is seen too.
///
/// Every handler is wrapped: they run inside TakeDamage and Died, and an exception thrown back into the
/// game from a statistics hook would change the battle it is meant to be measuring.
/// </summary>
public sealed class BotRecorder
{
    private readonly BotJob job;
    private readonly BotProfileData profile;
    private readonly BotTurnLog log;
    private readonly string runDir;
    private readonly StringBuilder digest = new();
    private readonly List<double> plannerMs = new();

    public BotRunRecord Record { get; }

    /// Anything happened - the watchdog's progress signal.
    public int EventCount { get; private set; }

    /// The last action that resolved, as "DamageAction Heavy Swing by HeavyBandit 1,4" - what a loop
    /// detector names when the game will not stop resolving.
    public string LastAction { get; private set; }

    public string RunDir => runDir;

    // Level state.
    private BotLevelRecord level;
    private BattleManager battle;
    private ActionManager actions;
    private bool levelOpen;
    private int currentRound;

    private sealed class Body
    {
        public string type;
        public BotSide side;
        public int joinedRound;
    }

    private readonly Dictionary<Character, Body> bodies = new();
    private readonly Dictionary<Character, string> lastHitBy = new();
    private readonly Dictionary<Character, string> lastPoisoner = new();
    private readonly Dictionary<Vector2Int, string> flamesBy = new();
    private readonly HashSet<string> typesThisLevel = new();
    private readonly HashSet<string> heroNames = new();

    // Run-wide tallies, emitted sorted into the record at the end.
    private readonly Dictionary<string, BotEnemyStat> enemyStats = new();
    private readonly Dictionary<string, BotCardStat> cardStats = new();
    private readonly Dictionary<string, BotHeroStat> heroStats = new();
    private readonly Dictionary<string, BotErrorRecord> errorsByMessage = new();

    // The hero play whose consequences are landing right now - hits and heals are credited to it.
    private Character playHero;
    private string playCard;
    private readonly List<string> pendingLines = new();

    // Enemy-phase hits land during an action's Execute, before ActionResolved names the action - held
    // here so the log can print the action first and what it did underneath it.
    private readonly List<string> actionHitLines = new();

    // Warnings by message, for the report's "what kept complaining" table.
    private readonly Dictionary<string, int> warningCounts = new();

    // BotJob.traceRng: the game's dice state at every event, to find where two runs part ways.
    private StreamWriter rngTrace;
    private string lastTracedState;

    /// Appends the dice state with what just happened, when tracing and the state moved since last time.
    public void TraceRng(string what)
    {
        if (!job.traceRng) { return; }

        string state = GameDice.StateText;

        if (state == lastTracedState) { return; }

        lastTracedState = state;

        rngTrace ??= new StreamWriter(Path.Combine(runDir, "rng-trace.txt"), false, BotText.Utf8);

        BattlePhase phase = battle != null ? battle.Phase : BattlePhase.NotStarted;
        rngTrace.WriteLine($"L{CurrentLevelNumber} R{currentRound} {phase} ev{EventCount} {what} {state}");
    }

    public BotRecorder(BotJob job, BotRunKey key, BotProfileData profile, int seed, string runDir, string partyLabel)
    {
        this.job = job;
        this.profile = profile;
        this.runDir = runDir;

        Record = new BotRunRecord
        {
            runId = key.RunId,
            batchId = job.batchId,
            profile = profile.name,
            baseline = profile.baseline,
            runIndex = key.runIndex,
            profileIndex = key.profileIndex,
            seed = seed,
            campaign = job.campaign,
            tier = job.tier,
            party = new List<BotPartySlot>(job.party),
            partyLabel = partyLabel,
            commit = job.commit,
            dirtyTree = job.dirtyTree,
            speed = job.speed.ToString(),
            turboStep = job.turboStep,
            startedUtc = DateTime.UtcNow.ToString("o"),
        };

        log = new BotTurnLog(runDir, job.writeTurnLog);

        log.Line($"=== Run {key.RunId} | profile {profile.name} | seed {seed} | {partyLabel} | {job.campaign} | tier {job.tier} ===");
        log.Line($"    batch {job.batchId}, commit {job.commit}{(job.dirtyTree ? " (dirty)" : string.Empty)}, {job.speed}");

        log.Event(new BotEvent { kind = "run", note = $"{key.RunId} seed {seed} profile {profile.name}" });
    }

    // ---------------------------------------------------------------------------------------------
    // Level lifecycle

    public void BeginLevel(int levelIndex, LevelData data, BattleManager battleManager, ActionManager actionManager)
    {
        EndLevel("Abandoned");

        level = new BotLevelRecord
        {
            level = levelIndex + 1,
            name = data != null ? data.name : "?",
            result = "Running",
        };

        battle = battleManager;
        actions = actionManager;
        levelOpen = true;
        currentRound = 0;
        bodies.Clear();
        lastHitBy.Clear();
        lastPoisoner.Clear();
        flamesBy.Clear();
        typesThisLevel.Clear();
        pendingLines.Clear();
        playHero = null;
        playCard = null;

        if (battle != null)
        {
            battle.CharacterJoined += OnJoined;
            battle.CharacterLeft += OnLeft;
        }

        if (actions != null) { actions.ActionResolved += OnActionResolved; }

        string board = data != null ? $"{data.BoardSize.x}x{data.BoardSize.y}, {data.TurnsToSurvive} turns" : string.Empty;

        log.Line();
        log.Line($"--- Level {level.level}: {level.name} ({board}) ---");
        log.Event(new BotEvent { kind = "level", level = level.level, note = level.name });
    }

    /// Closes the level record. `result` is Cleared, Won, Died, Stalled, Cancelled or Error.
    public void EndLevel(string result)
    {
        if (!levelOpen) { return; }

        levelOpen = false;
        FlushPending();

        level.result = result;
        level.rounds = Math.Max(level.rounds, currentRound);
        level.hpEnd = PartyHealth(out _);

        if (battle != null)
        {
            battle.CharacterJoined -= OnJoined;
            battle.CharacterLeft -= OnLeft;
        }

        if (actions != null) { actions.ActionResolved -= OnActionResolved; }

        foreach (Character character in bodies.Keys) { Unhook(character); }

        Record.levels.Add(level);

        if (result == "Cleared" || result == "Won") { Record.levelsCleared++; }

        log.Line($"=== Level {level.level} {result} after {level.rounds} rounds: dealt {level.dmgDealt}, taken {level.dmgTaken}, "
                 + $"healed {level.healing}, shield absorbed {level.shieldAbsorbed}, prevented {level.prevented}, falls {level.falls}, "
                 + $"enemies killed {level.enemiesKilled}/{level.enemiesOpening + level.enemiesWaves + level.enemiesSummoned} ===");
        log.Event(new BotEvent { kind = "levelEnd", level = level.level, round = level.rounds, note = result });
        log.Flush();

        battle = null;
        actions = null;
    }

    public int CurrentLevelNumber => level != null ? level.level : 0;

    // ---------------------------------------------------------------------------------------------
    // Rounds and decisions

    /// Called at the first decision of each player phase, with the board as it stands.
    public void BeginRound(int round, BotBoardModel model)
    {
        FlushPending();

        currentRound = round;

        if (level != null) { level.rounds = Math.Max(level.rounds, round); }

        if (round == 1 && level != null) { level.hpStart = PartyHealth(out level.hpMaxStart); }

        BattleManager bm = model.Battle;
        string board = BotTurnLog.Board(bm, out Dictionary<Character, string> tokens);

        log.Line();
        log.Line($"Round {round}  (turns left {(bm != null ? bm.TurnsRemaining : 0)})");
        log.Line(board.TrimEnd('\n'));

        foreach (Character hero in model.Heroes)
        {
            log.Line($"  {Token(tokens, hero)} {hero.DisplayName,-10} HP {hero.Health}/{hero.MaxHealth}  E {hero.Energy}/{hero.EnergyCapacity}"
                     + $"  [{BotTurnLog.Statuses(hero)}]  incoming {model.IncomingOn(hero)}");
            log.Line($"       hand: {BotTurnLog.Hand(hero)}");
        }

        foreach (Character ally in model.OurSide)
        {
            if (ally.IsPlayerControlled) { continue; }

            log.Line($"  {Token(tokens, ally)} {BotAssets.TypeKey(ally),-10} HP {ally.Health}/{ally.MaxHealth}  [{BotTurnLog.Statuses(ally)}]");
        }

        foreach (Character enemy in model.Enemies)
        {
            log.Line($"  {Token(tokens, enemy)} {BotAssets.TypeKey(enemy),-10} HP {enemy.Health}/{enemy.MaxHealth}"
                     + $"  [{BotTurnLog.Statuses(enemy)}]  {BotTurnLog.Plan(enemy, model)}");
        }

        log.Line("  Decisions:");
    }

    private static string Token(Dictionary<Character, string> tokens, Character c) =>
        tokens.TryGetValue(c, out string token) ? token.PadRight(3) : "?? ";

    /// Logs a battle decision - play or end turn - with the plays it beat.
    public void Decision(BotDecision decision, BotEvent e, BotWeights weights, int handLeftEnergy)
    {
        FlushPending();

        if (decision.Play != null)
        {
            BotCandidate p = decision.Play;

            log.Line($"   #{e.step} {p.Describe()}  [{BotText.F(p.Score, 1)}: {p.Features.Breakdown(weights)}]  ({decision.Reason})");

            if (decision.Alternatives.Count > 0)
            {
                List<string> alts = new();

                foreach (BotCandidate alt in decision.Alternatives) { alts.Add($"{alt.Describe()} {BotText.F(alt.Score, 1)}"); }

                log.Line($"       over: {string.Join("; ", alts)}");
            }
        }
        else
        {
            log.Line($"   #{e.step} End turn ({decision.Reason}; {decision.Evaluated} plays considered, {handLeftEnergy} energy unspent)");
        }

        RecordEvent(e);

        if (decision.PlannerMs > 0) { plannerMs.Add(decision.PlannerMs); }
    }

    /// Logs a popup answer - reward, removal, discard, acknowledgement.
    public void Choice(BotEvent e, string line)
    {
        FlushPending();
        log.Line($"   #{e.step} {line}");
        RecordEvent(e);
    }

    private void RecordEvent(BotEvent e)
    {
        Record.decisions++;
        digest.Append(e.fp).Append(e.action).Append(e.hand).Append(e.x).Append(e.y).Append(e.aim).Append(e.index).Append('|');
        log.Event(e);
    }

    public void Note(string line)
    {
        FlushPending();
        log.Line("   " + line);
    }

    /// A hero play is about to resolve: until the next decision, its hits and heals are credited to it.
    /// Opened before CardPlayManager.TryPlay, because the first action resolves inside that call.
    public void BeginPlay(Character hero, Card card)
    {
        playHero = hero;
        playCard = card != null ? card.cardName : null;
    }

    /// The play went through - count it.
    public void PlayCommitted(Character hero, Card card, BotCandidate candidate)
    {
        BotCardStat stat = CardStat(card != null ? card.Data : null);

        if (stat != null)
        {
            stat.played++;
            stat.scoreSum += candidate != null ? candidate.Score : 0f;
        }

        Record.cardsPlayed++;

        if (level != null) { level.cardsPlayed++; }

        HeroStat(hero).cardsPlayed++;
    }

    public void EndPlay()
    {
        playHero = null;
        playCard = null;
    }

    /// End of the heroes' turn: energy left on the table.
    public void EndTurn(BotBoardModel model)
    {
        EndPlay();

        foreach (Character hero in model.Heroes)
        {
            int unspent = Mathf.Max(0, hero.Energy);

            Record.energyUnspent += unspent;

            if (level != null) { level.energyUnspent += unspent; }

            HeroStat(hero).energyUnspent += unspent;
        }

        pendingLines.Add("  Enemy phase:");
    }

    public void PlayRefused(string what, string reason)
    {
        Record.plannerRefusals++;
        Note($"! refused: {what} - {reason}");
    }

    public void PlayCapHit()
    {
        Record.playCapHits++;
        Note("! play cap reached - ending the turn");
    }

    public void Reward(BotRewardRecord reward, IReadOnlyList<CardData> offeredCards, CardData picked)
    {
        reward.level = CurrentLevelNumber;
        reward.round = currentRound;
        Record.rewards.Add(reward);

        foreach (CardData card in offeredCards)
        {
            BotCardStat stat = CardStat(card);

            if (stat != null) { stat.offered++; }
        }

        if (picked != null)
        {
            BotCardStat stat = CardStat(picked);

            if (stat != null) { stat.picked++; }
        }

        if (reward.kind == "card" || reward.kind == "equipment") { Record.rewardsTaken++; }
    }

    /// The Victory/Defeat/Cleared notification is up: close the level, and for a wipe name who did it.
    public void LevelOutcome(string result)
    {
        // Read now, while the run still exists - a defeat's Finish calls RunManager.EndRun, which clears
        // the party before anything could count it.
        RunManager run = RunManager.Instance;

        if (run != null) { Record.heroesLost = Math.Max(0, Record.party.Count - run.Party.Count); }

        if (result == "Died" && level != null)
        {
            Record.endLevel = level.level;
            Record.endLevelName = level.name;
            Record.endRound = currentRound;

            foreach (BotFallRecord fall in level.fallList)
            {
                if (fall.round >= currentRound - 1 && !Record.killers.Contains(fall.killer)) { Record.killers.Add(fall.killer); }
            }
        }

        EndLevel(result);
    }

    // ---------------------------------------------------------------------------------------------
    // Errors

    public void Error(string type, string message, string stack)
    {
        string key = type + "|" + message;

        if (errorsByMessage.TryGetValue(key, out BotErrorRecord existing))
        {
            existing.count++;
        }
        else
        {
            BotErrorRecord record = new()
            {
                level = CurrentLevelNumber,
                round = currentRound,
                type = type,
                message = message,
                stack = stack,
                count = 1,
            };

            errorsByMessage[key] = record;
            Record.errorList.Add(record);

            Note($"! {type}: {message}");
            log.Event(new BotEvent { kind = "error", level = CurrentLevelNumber, round = currentRound, note = $"{type}: {message}" });
        }

        Record.errors++;
    }

    public void Warning(string message)
    {
        Record.warnings++;

        string key = (message ?? string.Empty).Split('\n')[0].Trim();

        if (key.Length > 160) { key = key.Substring(0, 160); }

        warningCounts[key] = warningCounts.TryGetValue(key, out int count) ? count + 1 : 1;
    }

    // ---------------------------------------------------------------------------------------------
    // Finishing

    public void FinishRun(string outcome, string note, float wallSeconds, int frames)
    {
        if (levelOpen) { EndLevel(outcome == "Won" ? "Won" : outcome); }

        Record.outcome = outcome;
        Record.outcomeNote = note;
        Record.wallSeconds = wallSeconds;
        Record.frames = frames;

        if (outcome != "Won" && Record.endLevel < 0 && Record.levels.Count > 0)
        {
            BotLevelRecord last = Record.levels[^1];
            Record.endLevel = last.level;
            Record.endLevelName = last.name;
            Record.endRound = last.rounds;
        }

        foreach (BotLevelRecord l in Record.levels)
        {
            Record.dmgDealt += l.dmgDealt;
            Record.dmgTaken += l.dmgTaken;
            Record.healing += l.healing;
            Record.shieldAbsorbed += l.shieldAbsorbed;
            Record.prevented += l.prevented;
            Record.falls += l.falls;
        }

        plannerMs.Sort();

        if (plannerMs.Count > 0)
        {
            double sum = 0;

            foreach (double ms in plannerMs) { sum += ms; }

            Record.plannerMsAvg = (float)(sum / plannerMs.Count);
            Record.plannerMsP95 = (float)plannerMs[Math.Min(plannerMs.Count - 1, (int)(plannerMs.Count * 0.95))];
            Record.plannerMsMax = (float)plannerMs[^1];
        }

        Record.enemies = SortedValues(enemyStats);
        Record.cards = SortedValues(cardStats);
        Record.heroes = SortedValues(heroStats);

        List<string> warnings = new(warningCounts.Keys);
        warnings.Sort((a, b) => warningCounts[b] != warningCounts[a]
            ? warningCounts[b].CompareTo(warningCounts[a])
            : string.CompareOrdinal(a, b));

        for (int i = 0; i < warnings.Count && i < 15; i++)
        {
            Record.warningList.Add(new BotWarningRecord { message = warnings[i], count = warningCounts[warnings[i]] });
        }
        Record.digest = BotFingerprint.Hash(digest.ToString());

        log.Line();
        log.Line($"=== Run {Record.runId} {outcome}{(string.IsNullOrEmpty(note) ? string.Empty : " - " + note)}: levels cleared "
                 + $"{Record.levelsCleared}, dealt {Record.dmgDealt}, taken {Record.dmgTaken}, falls {Record.falls}, "
                 + $"{Record.decisions} decisions, {BotText.F(wallSeconds, 1)} s ===");
        log.Event(new BotEvent { kind = "runEnd", note = outcome });
        log.Dispose();

        if (rngTrace != null) { rngTrace.Dispose(); }

        BotText.WriteAtomic(Path.Combine(runDir, "run.json"), JsonUtility.ToJson(Record, prettyPrint: true));
    }

    /// Closes the log files without writing run.json - a replay's side-record, which never becomes a run.
    public void CloseWithoutRecord() => log.Dispose();

    private static List<T> SortedValues<T>(Dictionary<string, T> map)
    {
        List<string> keys = new(map.Keys);
        keys.Sort(string.CompareOrdinal);

        List<T> values = new();

        foreach (string key in keys) { values.Add(map[key]); }

        return values;
    }

    // ---------------------------------------------------------------------------------------------
    // Event handlers

    private void OnJoined(Character character)
    {
        try
        {
            TraceRng($"join {(character != null ? character.name : "?")}");

            if (character == null || bodies.ContainsKey(character)) { return; }

            BotSide side = BotAssets.SideOf(character);
            string type = BotAssets.TypeKey(character);
            int round = battle != null ? battle.TurnsElapsed : 0;

            bodies[character] = new Body { type = type, side = side, joinedRound = round };

            character.HitResolved += OnHit;
            character.DamagedUnblockable += OnUnblockable;
            character.Healed += OnHealed;
            character.Died += OnDied;

            EventCount++;

            if (side == BotSide.Hero)
            {
                heroNames.Add(type);
                HeroStat(character);
                return;
            }

            if (side != BotSide.Enemy || level == null) { return; }

            BotEnemyStat stat = EnemyStat(type, character);
            stat.bodies++;

            if (typesThisLevel.Add(type))
            {
                stat.battles++;
                level.enemyTypes.Add(type);
            }

            if (character.IsBoss) { level.boss = true; }

            BattlePhase phase = battle != null ? battle.Phase : BattlePhase.NotStarted;

            if (phase == BattlePhase.NotStarted) { level.enemiesOpening++; }
            else if (phase == BattlePhase.TurnStart) { level.enemiesWaves++; }
            else { level.enemiesSummoned++; }

            if (phase != BattlePhase.NotStarted)
            {
                // `!= null`, never `?.` - Tile is a UnityEngine.Object. See CLAUDE.md.
                Vector2Int cell = character.Tile != null ? character.Tile.Coordinates : default;
                pendingLines.Add($"    + {type} joins at ({cell.x},{cell.y})");
            }
        }
        catch (Exception ex) { Swallow(ex); }
    }

    private void OnLeft(Character character)
    {
        try
        {
            if (character != null) { Unhook(character); }
        }
        catch (Exception ex) { Swallow(ex); }
    }

    private void Unhook(Character character)
    {
        if (character == null) { return; }

        character.HitResolved -= OnHit;
        character.DamagedUnblockable -= OnUnblockable;
        character.Healed -= OnHealed;
        character.Died -= OnDied;
    }

    private void OnHit(DamageInfo info, int arriving)
    {
        try
        {
            EventCount++;

            Character target = info.target;
            Character attacker = info.attacker;

            TraceRng($"hit {(attacker != null ? attacker.name : "-")}>{(target != null ? target.name : "-")} {info.amount}");

            if (target == null || level == null) { return; }

            int landed = Mathf.Max(0, info.amount);
            int absorbed = Mathf.Max(0, info.shieldAbsorbed);
            int registered = landed + absorbed;
            int prevented = Mathf.Max(0, arriving - landed - absorbed);

            BotSide targetSide = BotAssets.SideOf(target);
            BotSide attackerSide = BotAssets.SideOf(attacker);
            string attackerType = attacker != null ? BotAssets.TypeKey(attacker) : "environment";
            string targetType = BotAssets.TypeKey(target);

            if (targetSide == BotSide.Hero || targetSide == BotSide.Ally)
            {
                if (targetSide == BotSide.Hero)
                {
                    level.dmgTaken += registered;
                    level.shieldAbsorbed += absorbed;
                    level.prevented += prevented;

                    BotHeroStat hero = HeroStat(target);
                    hero.dmgTaken += registered;
                    hero.shieldAbsorbed += absorbed;
                    hero.prevented += prevented;
                }

                if (attackerSide == BotSide.Enemy)
                {
                    BotEnemyStat stat = EnemyStat(attackerType, attacker);

                    if (targetSide == BotSide.Hero) { stat.dmgToHeroes += registered; }
                    else { stat.dmgToAllies += registered; }

                    stat.prevented += prevented;
                }
            }
            else if (targetSide == BotSide.Enemy)
            {
                EnemyStat(targetType, target).dmgTaken += registered;

                if (attackerSide == BotSide.Hero || attackerSide == BotSide.Ally)
                {
                    level.dmgDealt += registered;

                    if (attackerSide == BotSide.Hero) { HeroStat(attacker).dmgDealt += registered; }

                    if (playCard != null && attacker == playHero)
                    {
                        BotCardStat card = CardStatByName(playCard);

                        if (card != null) { card.dmgDealt += registered; }
                    }
                }
            }

            if (registered > 0 || info.negated) { lastHitBy[target] = attackerType; }

            if (attacker != null && attacker.StatusStacks(StatusType.PoisonBlade) > 0) { lastPoisoner[target] = attackerType; }

            if (battle != null && battle.Phase == BattlePhase.EnemyResolve && (registered > 0 || prevented > 0))
            {
                string extra = absorbed > 0 || prevented > 0 ? $" (shield {absorbed}, stopped {prevented})" : string.Empty;
                actionHitLines.Add($"        {targetType} -{landed}{extra}{(attacker != null && attackerSide != BotSide.Enemy ? $" (from {attackerType})" : string.Empty)}");
            }
        }
        catch (Exception ex) { Swallow(ex); }
    }

    private void OnUnblockable(Character victim, int amount)
    {
        try
        {
            EventCount++;

            TraceRng($"unblockable {(victim != null ? victim.name : "-")} {amount}");

            if (victim == null || level == null || amount <= 0) { return; }

            string source = ClassifyUnblockable(victim);

            if (source == null) { return; }

            BotSide side = BotAssets.SideOf(victim);

            if (side == BotSide.Hero)
            {
                level.dmgTaken += amount;
                HeroStat(victim).dmgTaken += amount;

                if (enemyStats.TryGetValue(source, out BotEnemyStat stat)) { stat.dmgToHeroes += amount; }

                lastHitBy[victim] = source;
            }
            else if (side == BotSide.Enemy)
            {
                EnemyStat(BotAssets.TypeKey(victim), victim).dmgTaken += amount;

                if (heroNames.Contains(source))
                {
                    level.dmgDealt += amount;
                    HeroStat(source).dmgDealt += amount;
                }

                lastHitBy[victim] = source;
            }

            pendingLines.Add($"    {BotAssets.TypeKey(victim)} takes {amount} ({source})");
        }
        catch (Exception ex) { Swallow(ex); }
    }

    /// <summary>
    /// Who is behind damage that carries no attacker, told apart by when it lands.
    ///
    /// Inside an action (the queue is busy): the play's own self-damage cost, or Thorns biting back at
    /// an attacker. Outside one: the round-end ticks - poison, credited to whoever last applied it (a card
    /// that poisons, or a hit from a PoisonBlade carrier), then Wall of Flames, credited to whoever laid it.
    /// A summon's own expiry counts for nobody (null - not recorded).
    /// </summary>
    private string ClassifyUnblockable(Character victim)
    {
        bool inAction = ActionManager.Instance != null && !ActionManager.Instance.IsIdle;

        if (inAction) { return playHero != null && victim == playHero ? "self" : "thorns"; }

        if (victim.CarriedStatusStacks(StatusType.Summoned) > 0 && victim.Health <= 0) { return null; }

        if (victim.CarriedStatusStacks(StatusType.Poison) > 0)
        {
            return lastPoisoner.TryGetValue(victim, out string poisoner) ? poisoner : "poison";
        }

        if (victim.Tile != null)
        {
            foreach (TileEffect effect in victim.Tile.TileEffects)
            {
                if (effect.type != TileEffectType.WallOfFlames) { continue; }

                return flamesBy.TryGetValue(victim.Tile.Coordinates, out string burner) ? burner : "Wall of Flames";
            }
        }

        return lastPoisoner.TryGetValue(victim, out string last) ? last : "environment";
    }

    private void OnHealed(Character character, int amount)
    {
        try
        {
            EventCount++;

            TraceRng($"healed {(character != null ? character.name : "-")} {amount}");

            if (character == null || level == null || !BotAssets.IsOurSide(character)) { return; }

            if (character.IsPlayerControlled)
            {
                level.healing += amount;
                HeroStat(character).healingReceived += amount;
            }

            if (playHero != null) { HeroStat(playHero).healingDone += amount; }
        }
        catch (Exception ex) { Swallow(ex); }
    }

    private void OnDied(Character character)
    {
        try
        {
            EventCount++;

            TraceRng($"died {(character != null ? character.name : "-")}");

            if (character == null || level == null) { return; }

            bodies.TryGetValue(character, out Body body);
            BotSide side = body != null ? body.side : BotAssets.SideOf(character);
            string type = body != null ? body.type : BotAssets.TypeKey(character);

            if (side == BotSide.Hero)
            {
                string killer = lastHitBy.TryGetValue(character, out string hit) ? hit : "unknown";

                level.falls++;
                level.fallList.Add(new BotFallRecord { hero = type, round = currentRound, killer = killer });
                HeroStat(character).falls++;

                if (enemyStats.TryGetValue(killer, out BotEnemyStat stat)) { stat.fallsCaused++; }

                pendingLines.Add($"    ** {type} falls (last hit: {killer})");
            }
            else if (side == BotSide.Enemy)
            {
                level.enemiesKilled++;

                BotEnemyStat stat = EnemyStat(type, character);
                stat.killed++;
                stat.roundsAlive += Mathf.Max(0, currentRound - (body != null ? body.joinedRound : currentRound));

                pendingLines.Add($"    x {type} is down");
            }
        }
        catch (Exception ex) { Swallow(ex); }
    }

    private void OnActionResolved(GameAction action, ActionContext ctx)
    {
        try
        {
            EventCount++;

            LastAction = $"{action.GetType().Name} {(ctx != null && ctx.card != null ? ctx.card.cardName : "(no card - a reaction?)")} "
                         + $"by {(ctx != null && ctx.source != null ? ctx.source.name : "-")}";

            TraceRng("action " + LastAction);

            if (ctx != null && ctx.source != null && ctx.card != null) { Attribute(action, ctx); }

            // Whatever this action hit goes under it - or, for an action with no card to name (a totem's
            // reaction), straight after whatever came before it.
            pendingLines.AddRange(actionHitLines);
            actionHitLines.Clear();
        }
        catch (Exception ex) { Swallow(ex); }
    }

    /// Who poisoned whom and who lit which tile, for ClassifyUnblockable - plus the enemy-phase line naming
    /// the action.
    private void Attribute(GameAction action, ActionContext ctx)
    {
        string source = BotAssets.TypeKey(ctx.source);

        foreach (CardEffectEntry entry in ctx.card.EffectEntries)
        {
            if (entry.effect is ApplyStatusEffect status && status.Status == StatusType.Poison)
            {
                foreach (GridTile tile in ctx.targets)
                {
                    Character occupant = tile != null ? tile.Occupant : null;

                    if (occupant != null && occupant.CarriedStatusStacks(StatusType.Poison) > 0) { lastPoisoner[occupant] = source; }
                }
            }
            else if (entry.effect is ApplyTileEffect tileEffect && tileEffect.Effect == TileEffectType.WallOfFlames)
            {
                foreach (GridTile tile in ctx.targets)
                {
                    if (tile != null) { flamesBy[tile.Coordinates] = source; }
                }
            }
        }

        if (battle != null && battle.Phase == BattlePhase.EnemyResolve && BotAssets.SideOf(ctx.source) == BotSide.Enemy)
        {
            string where = ctx.epicenter != null ? $"({ctx.epicenter.Coordinates.x},{ctx.epicenter.Coordinates.y})" : "self";
            pendingLines.Add($"    {source}: {ctx.card.cardName} -> {where} [{action.GetType().Name.Replace("Action", string.Empty)}]");
        }
    }

    private void FlushPending()
    {
        pendingLines.AddRange(actionHitLines);
        actionHitLines.Clear();

        foreach (string line in pendingLines) { log.Line(line); }

        pendingLines.Clear();
    }

    // ---------------------------------------------------------------------------------------------
    // Tallies

    private BotEnemyStat EnemyStat(string type, Character sample)
    {
        if (!enemyStats.TryGetValue(type, out BotEnemyStat stat))
        {
            stat = new BotEnemyStat
            {
                type = type,
                boss = sample != null && sample.IsBoss,
                power = sample != null ? sample.PowerLevel : 0f,
            };

            enemyStats[type] = stat;
        }

        return stat;
    }

    private BotHeroStat HeroStat(Character hero) => HeroStat(hero != null ? hero.DisplayName : "?");

    private BotHeroStat HeroStat(string name)
    {
        if (!heroStats.TryGetValue(name, out BotHeroStat stat))
        {
            stat = new BotHeroStat { hero = name };
            heroStats[name] = stat;
        }

        return stat;
    }

    private BotCardStat CardStat(CardData data)
    {
        if (data == null) { return null; }

        if (!cardStats.TryGetValue(data.cardName, out BotCardStat stat))
        {
            stat = new BotCardStat
            {
                card = data.cardName,
                category = BotCardInfo.Of(data).Primary.ToString(),
                rarity = data.rarity.ToString(),
                cost = data.cost,
            };

            cardStats[data.cardName] = stat;
        }

        return stat;
    }

    private BotCardStat CardStatByName(string cardName) =>
        cardName != null && cardStats.TryGetValue(cardName, out BotCardStat stat) ? stat : null;

    private int PartyHealth(out int max)
    {
        int health = 0;
        max = 0;

        if (battle == null) { return 0; }

        foreach (Character character in battle.Characters)
        {
            if (character == null || !character.IsPlayerControlled || character.IsDead) { continue; }

            health += character.Health;
            max += character.MaxHealth;
        }

        return health;
    }

    private void Swallow(Exception ex)
    {
        // Logged, not rethrown - see the class comment. The pilot's log hook records it as an error.
        Debug.LogException(ex);
    }
}
#endif
