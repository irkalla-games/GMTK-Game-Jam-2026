using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One hero's state for the current run: who they are, what they are carrying, and how hurt they are.
///
/// The deck is a copy, never the prefab's own list. Handing out the prefab's list would mean a card
/// added as a reward gets written into the prefab asset and persists to disk after Play Mode exits -
/// the same trap as mutating a ScriptableObject at runtime, and just as silent.
/// </summary>
[System.Serializable]
public class PartyMember
{
    public Character prefab;

    public List<CardData> deck;

    public int currentHealth;

    /// Equipment picked up this run, permanent for the rest of it - the equipment counterpart to deck.
    /// Never null after Begin seeds it, same reason deck never is: BattleManager.SpawnParty hands it
    /// straight to Character.SetEquipment with no null check of its own.
    public List<EquipmentData> equipment = new();

    /// Max health this hero has permanently lost to falls, in total across every fall this run.
    /// Subtracted from prefab.MaxHealth at spawn - see RunManager.MaxHealthFor and
    /// BattleManager.SpawnParty.
    public int maxHealthLost;

    /// Set by RecordFall, cleared by BattleManager.SpawnParty once they next take the field. Between
    /// those two points it tells AdvanceLevel's heal-up to leave this member's health alone - the
    /// whole point of a fall is the half-health they come back at, and a heal-up between StartRun and
    /// the next battle would erase it before it was ever played.
    public bool returningFromFall;
}

/// <summary>
/// Where the run has got to. A run is one attempt at the game: it starts at the Main Menu, walks the
/// levels of a RunData in order, and ends when the party dies or the last level is cleared.
///
/// The runtime half of RunData, the way Card is the runtime half of CardData - RunData says which
/// levels and who you start with, this says which level you are on and what the party has become
/// since. It survives every scene load between levels because it is a PersistantSingleton.
///
/// Holds prefabs, not live instances. Each level's BattleManager instantiates its own copies from
/// Party and places them at LevelData.PartySpawnCells - the same split SpawnEnemies already uses for
/// enemies. A character's live state does not need to survive a scene load; only the record of it
/// does, which is what PartyMember is.
///
/// Deliberately has no prefab and lives in no scene. StartRun creates it on demand and resets it if it
/// already exists, which is the whole answer to the stale-run problem: a second RunManager placed in
/// MainMenu would destroy *itself* in Singleton.Awake and leave the dead run's party and level index
/// alive, so the next Play would resume a run that had already ended.
/// </summary>
public class RunManager : PersistantSingleton<RunManager>
{
    private RunData campaign;

    private int levelIndex;

    private bool tutorialEnabled;

    /// <summary>
    /// Which rung of the campaign's DifficultyLadder this run is being played on.
    ///
    /// Snapshotted at StartRun rather than read from DifficultyProgress wherever it is needed, for the
    /// same reason tutorialEnabled is: changing the selection at the menu must not reach a run already
    /// under way.
    ///
    /// Deliberately *not* reset by Begin, unlike everything else about a run. Begin runs a second time
    /// when the tutorial hands over to the real campaign (BeginFollowOn), and a tier cleared there
    /// would silently drop back to Normal mid-handover. Only StartRun and EndRun write it.
    /// </summary>
    private int difficultyTier;

    /// <summary>
    /// The run queued behind this one, or null. Exists for the tutorial: it is a self-contained prologue
    /// run with its own fixed party and its own one level, and clearing it should drop the player
    /// straight into a real run rather than back at the menu.
    ///
    /// A field here rather than on RunData because the decision is made once, at the Main Menu, and has
    /// to survive every scene load in between - which is exactly what this PersistantSingleton already
    /// does for the party and the level index. Putting it on RunData would also make a campaign asset
    /// permanently point at another one, which is a property of a session, not of the campaign.
    /// </summary>
    private RunData followOnRun;

    private readonly List<PartyMember> party = new();

    /// The run's roster. Members removed by death do not come back - see RemoveMember.
    public IReadOnlyList<PartyMember> Party => party;

    /// The level being played right now, or null once the last one is cleared.
    public LevelData CurrentLevel =>
        campaign != null && levelIndex >= 0 && levelIndex < campaign.Levels.Count
            ? campaign.Levels[levelIndex]
            : null;

    /// One-based, for anything that wants to say "Level 2" to a player.
    public int LevelNumber => levelIndex + 1;

    /// <summary>
    /// Whether there is no level after the current one. Non-mutating, unlike AdvanceLevel, which is
    /// the only other way to find out and answers by *moving the index* - the winning turn has to
    /// know which modal to show before it decides to move on.
    ///
    /// No campaign counts as final, matching what AdvanceLevel would say: there is nothing to go on
    /// to, so this is the last thing that happens.
    /// </summary>
    public bool IsFinalLevel => campaign == null || levelIndex >= campaign.Levels.Count - 1;

    /// <summary>
    /// Whether this run asked for the tutorial. Snapshotted at StartRun rather than read from
    /// GameSettings wherever it is needed, so toggling the option cannot change a run already under
    /// way - the same reason the party is copied out of RunData rather than pointed at.
    /// </summary>
    public bool TutorialEnabled => tutorialEnabled;

    /// Which rung this run is on, for anything that wants to say "Hard II" to a player.
    public int DifficultyTier => difficultyTier;

    /// <summary>
    /// The scaling this run plays at. An all-zero tier - no campaign, no ladder authored on it, or an
    /// index the ladder does not have - is exactly Normal, because every scale on a DifficultyTier is
    /// a bonus added to 1 rather than a multiplier. That is what lets the debug and testbed campaigns
    /// carry no ladder at all and still work.
    /// </summary>
    public DifficultyTier CurrentTier =>
        campaign != null && campaign.Ladder != null ? campaign.Ladder.TierAt(difficultyTier) : default;

    /// <summary>
    /// Begins a run, from the Main Menu's Play button. Reuses the existing object when there is one
    /// rather than destroying and re-instantiating: Destroy is deferred to the end of the frame, so a
    /// fresh Instantiate in the same call would find Instance still set and destroy itself instead.
    /// </summary>
    /// <param name="followOn">Started automatically when `campaign` is cleared, instead of ending the
    /// run and returning to the menu. Null is the ordinary case - see the field's own doc comment.</param>
    /// <param name="difficultyTier">Which rung of the campaign's ladder to play. Defaults to 0 -
    /// Normal - so the debug, testbed and tutorial paths stay unscaled without passing anything.</param>
    public static void StartRun(
        RunData campaign, bool showTutorial, RunData followOn = null, int difficultyTier = 0)
    {
        RunManager manager = Instance;

        if (manager == null)
        {
            manager = new GameObject(nameof(RunManager)).AddComponent<RunManager>();
        }

        manager.Begin(campaign, showTutorial);

        // Both after Begin, which resets everything else about the previous run. The tier is set here
        // rather than inside Begin precisely so a follow-on run inherits it - see the field.
        manager.followOnRun = followOn;
        manager.difficultyTier = Mathf.Max(0, difficultyTier);
    }

    /// Whether clearing the current run leads somewhere other than the menu.
    public bool HasFollowOn => followOnRun != null;

    /// <summary>
    /// Starts the queued run and reports whether it has a level to play. Consumes the queue first, so a
    /// follow-on that is itself cleared ends the way any other run does rather than restarting.
    ///
    /// Never with the tutorial: the tutorial is the thing that queued this, and running it again on the
    /// first level of the real campaign would script a level it knows nothing about.
    /// </summary>
    public bool BeginFollowOn()
    {
        if (followOnRun == null) { return false; }

        RunData next = followOnRun;
        followOnRun = null;

        Begin(next, showTutorial: false);

        return CurrentLevel != null;
    }

    /// <summary>
    /// Starts a run only if one is not already under way. This is what lets Game.unity be opened and
    /// played on its own without going through the Main Menu first - BattleManager calls it with its
    /// debug campaign, and it does nothing at all when the player arrived here through a real run.
    ///
    /// Never with the tutorial. This path exists for iterating on a level in the Editor, and a modal
    /// to dismiss on every Play is exactly the friction it is meant to avoid.
    /// </summary>
    public static void EnsureRun(RunData fallback)
    {
        if (Instance != null && Instance.campaign != null) { return; }
        if (fallback == null) { return; }

        StartRun(fallback, showTutorial: false);
    }

    /// <summary>
    /// Moves to the next level and returns whether there is one. False means the run is complete -
    /// the caller decides what that is worth, this only knows it has run out of levels.
    /// </summary>
    public bool AdvanceLevel()
    {
        levelIndex++;

        if (CurrentLevel == null) { return false; }

        // Healing happens here rather than by skipping the SetHealth call in SpawnParty, so the record
        // is always the truth about a member's health and there is only ever one path that reads it.
        // returningFromFall is skipped on purpose: a fall's whole point is the half-health they come
        // back at, and this heal-up would erase it on the very level it was meant to matter for.
        if (!campaign.CarryDamageBetweenLevels)
        {
            foreach (PartyMember member in party)
            {
                if (member.prefab != null && !member.returningFromFall)
                {
                    member.currentHealth = MaxHealthFor(member);
                }
            }
        }

        return true;
    }

    /// A hero's max health after every fall this run has taken from their prefab's original - floored
    /// at 1 so a member is never left with nothing to heal to. BattleManager.SpawnParty applies this
    /// same amount via Character.AddMaxHealth before it sets their health for the battle.
    public static int MaxHealthFor(PartyMember member) => Mathf.Max(1, member.prefab.MaxHealth - member.maxHealthLost);

    /// <summary>
    /// A hero who fell is out of the run for good. Removed rather than flagged dead: everything that
    /// walks Party wants the living, and a member who is skipped by every reader is not a member.
    /// </summary>
    public void RemoveMember(PartyMember member)
    {
        party.Remove(member);
    }

    /// <summary>
    /// What a fall costs - BattleManager.HandleCharacterDied's replacement for the old outright
    /// RemoveMember. Either the hero is knocked down a rung of max health and stays in the run, at
    /// half of what is left, or there is nothing left to take and they are out for good exactly as
    /// before.
    ///
    /// cost &lt;= 0 covers both an unset RunData.healthLostOnFall (every debug/testbed/tutorial run) and
    /// a run with no campaign at all - both keep today's permanent death rather than reading the 0 as
    /// "lose nothing and never die".
    /// </summary>
    public void RecordFall(PartyMember member)
    {
        int cost = campaign != null && member.prefab != null
            ? campaign.FallCostFor(member.prefab.MaxHealth)
            : 0;
        int remaining = MaxHealthFor(member) - cost;

        if (cost <= 0 || remaining <= 0)
        {
            RemoveMember(member);
            return;
        }

        member.maxHealthLost += cost;
        member.currentHealth = Mathf.Max(1, remaining / 2);
        member.returningFromFall = true;
    }

    /// <summary>
    /// Pays out for clearing this campaign, and reports what was newly earned so the victory modal can
    /// name it - null when nothing was, which is what keeps a repeat clear from congratulating a
    /// player for heroes they have had for three runs.
    ///
    /// Called from BattleManager.EndLevel, not Finish: Finish loads the Main Menu synchronously, so a
    /// modal raised there would never be seen. EndLevel already checks the tutorial hand-over ahead of
    /// its final-level branch, which is what stops a cleared tutorial paying out.
    ///
    /// A campaign that does not award progression returns null without touching anything - that is the
    /// whole of what keeps the debug and testbed runs inert.
    /// </summary>
    public string AwardRunComplete()
    {
        if (campaign == null || !campaign.AwardsProgression) { return null; }

        List<string> lines = new();

        foreach (CharacterOption option in CurrentTier.unlocksOnClear ?? new List<CharacterOption>())
        {
            // `!= null` rather than a null-conditional: a deleted asset is a Unity fake-null. See
            // CLAUDE.md. Unlock itself reports whether this was news rather than already earned.
            if (option != null && CharacterUnlocks.Unlock(option))
            {
                lines.Add($"{option.DisplayName} joins your roster.");
            }
        }

        // Only when the ladder actually has a rung above this one. RecordClear would otherwise raise
        // HighestUnlocked past the end of the list, and a selector offering that index would resolve
        // to an all-zero tier - a "Hard VII" that silently played on Normal.
        bool hasNextTier =
            campaign.Ladder != null && difficultyTier + 1 < campaign.Ladder.Tiers.Count;

        if (hasNextTier && DifficultyProgress.RecordClear(difficultyTier))
        {
            lines.Add($"{campaign.Ladder.NameAt(difficultyTier + 1)} unlocked.");
        }

        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }

    /// Ends the run - on death, or after the last level. The object stays alive; Begin is what resets
    /// it, so there is no window where a half-torn-down run is the one Instance points at.
    public void EndRun()
    {
        campaign = null;
        levelIndex = 0;
        tutorialEnabled = false;

        // Unlike Begin, which leaves this alone so a follow-on inherits it - see the field. A run that
        // is over has no tier, and the next StartRun sets one before anything can read it.
        difficultyTier = 0;
        followOnRun = null;
        party.Clear();
    }

    private void Begin(RunData runData, bool showTutorial)
    {
        campaign = runData;
        levelIndex = 0;
        tutorialEnabled = showTutorial;
        followOnRun = null;
        party.Clear();

        if (runData == null)
        {
            Debug.LogError("RunManager started with no RunData - there is no level to play and no party "
                           + "to play it with. Assign a campaign on the Main Menu.");
            return;
        }

        // StartingHeroes is what a real run through the Main Menu's character select produces; empty
        // means this RunData was authored the old way (StartingParty only, e.g. DebugRun.asset), which
        // is the standalone-play path - opening Game.unity directly and pressing Play goes through
        // EnsureRun with no menu in between. Never both: a select-screen run always sets one and leaves
        // the other at its authored default.
        if (runData.StartingHeroes.Count > 0)
        {
            BeginFromHeroes(runData);
        }
        else
        {
            BeginFromStartingParty(runData);
        }
    }

    private void BeginFromHeroes(RunData runData)
    {
        foreach (PartyEntry entry in runData.StartingHeroes)
        {
            if (entry.prefab == null) { continue; }

            Character character = entry.prefab.GetComponent<Character>();

            if (character == null)
            {
                Debug.LogError($"{entry.prefab.name} is in {runData.name}'s starting heroes but has no "
                               + "Character component - skipped");
                continue;
            }

            // Null deck means no choice was made for this hero - fall back to the prefab's own
            // authored deck, same as picking no CharacterOption.Decks entry means at the select screen.
            IReadOnlyList<CardData> cards =
                entry.deck != null ? entry.deck.Cards : character.AuthoredDeck;

            party.Add(new PartyMember
            {
                prefab = character,
                deck = new List<CardData>(cards),
                currentHealth = character.MaxHealth,
            });
        }
    }

    private void BeginFromStartingParty(RunData runData)
    {
        IReadOnlyList<GameObject> startingParty = runData.StartingParty;
        IReadOnlyList<DeckData> deckOverrides = runData.StartingPartyDecks;

        for (int i = 0; i < startingParty.Count; i++)
        {
            GameObject prefab = startingParty[i];

            if (prefab == null) { continue; }

            // The one place a party prefab is a bare GameObject - see RunData.StartingParty for why it
            // has to be authored as one. Resolved here so everything downstream holds a Character.
            // GetComponent on a prefab asset reads it without instantiating anything, which is also
            // how MaxHealth and the authored deck are read below.
            Character character = prefab.GetComponent<Character>();

            if (character == null)
            {
                Debug.LogError($"{prefab.name} is in {runData.name}'s starting party but has no "
                               + "Character component - skipped");
                continue;
            }

            // Null, or the override list running shorter than StartingParty, means no choice was made
            // for this slot - fall back to the prefab's own authored deck, same fallback
            // BeginFromHeroes applies for a null PartyEntry.deck.
            DeckData deckOverride = i < deckOverrides.Count ? deckOverrides[i] : null;
            IReadOnlyList<CardData> cards = deckOverride != null ? deckOverride.Cards : character.AuthoredDeck;

            party.Add(new PartyMember
            {
                prefab = character,
                deck = new List<CardData>(cards),
                currentHealth = character.MaxHealth,
            });
        }
    }
}
