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
    /// Begins a run, from the Main Menu's Play button. Reuses the existing object when there is one
    /// rather than destroying and re-instantiating: Destroy is deferred to the end of the frame, so a
    /// fresh Instantiate in the same call would find Instance still set and destroy itself instead.
    /// </summary>
    public static void StartRun(RunData campaign)
    {
        RunManager manager = Instance;

        if (manager == null)
        {
            manager = new GameObject(nameof(RunManager)).AddComponent<RunManager>();
        }

        manager.Begin(campaign);
    }

    /// <summary>
    /// Starts a run only if one is not already under way. This is what lets Game.unity be opened and
    /// played on its own without going through the Main Menu first - BattleManager calls it with its
    /// debug campaign, and it does nothing at all when the player arrived here through a real run.
    /// </summary>
    public static void EnsureRun(RunData fallback)
    {
        if (Instance != null && Instance.campaign != null) { return; }
        if (fallback == null) { return; }

        StartRun(fallback);
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
        if (!campaign.CarryDamageBetweenLevels)
        {
            foreach (PartyMember member in party)
            {
                if (member.prefab != null) { member.currentHealth = member.prefab.MaxHealth; }
            }
        }

        return true;
    }

    /// <summary>
    /// A hero who fell is out of the run for good. Removed rather than flagged dead: everything that
    /// walks Party wants the living, and a member who is skipped by every reader is not a member.
    /// </summary>
    public void RemoveMember(PartyMember member)
    {
        party.Remove(member);
    }

    /// Ends the run - on death, or after the last level. The object stays alive; Begin is what resets
    /// it, so there is no window where a half-torn-down run is the one Instance points at.
    public void EndRun()
    {
        campaign = null;
        levelIndex = 0;
        party.Clear();
    }

    private void Begin(RunData runData)
    {
        campaign = runData;
        levelIndex = 0;
        party.Clear();

        if (runData == null)
        {
            Debug.LogError("RunManager started with no RunData - there is no level to play and no party "
                           + "to play it with. Assign a campaign on the Main Menu.");
            return;
        }

        foreach (GameObject prefab in runData.StartingParty)
        {
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

            party.Add(new PartyMember
            {
                prefab = character,
                deck = new List<CardData>(character.AuthoredDeck),
                currentHealth = character.MaxHealth,
            });
        }
    }
}
