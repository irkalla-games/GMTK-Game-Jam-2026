using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Authors the meta-progression content: the difficulty ladder, which heroes ship locked, and act 2 of
/// the real campaign.
///
/// Menu commands rather than hand-edited YAML because the Editor holds these assets in memory while it
/// is open, and anything written underneath it is discarded on the next save. Going through
/// SerializedObject also sets every private [SerializeField] the same way the Inspector does, dirty
/// flags included. Same shape as CharacterSelectWiring and TutorialContentGenerator.
///
/// Repairs rather than skips: every field this command owns is re-written on every run, so a
/// half-built set is always fixable by re-running rather than permanently wrong. The one exception is
/// noted on SeedFromTemplate - fields copied only to seed a *new* level asset, so re-running never
/// stomps hand-tuning of loot tables or tilesets.
///
/// Deliberately no AssetDatabase.StartAssetEditing anywhere: it defers imports, which makes
/// LoadAssetAtPath return null for anything created inside the block and writes cross-references as
/// {fileID: 0}, silently. Assets are created in dependency order and each new one is passed down as a
/// live object instead - see CLAUDE.md and the changelog.
///
/// Editor-only, and only covered by Tools/compile-check.ps1 when it is run with -IncludeEditor.
/// </summary>
public static class ProgressionContentGenerator
{
    private const string LadderFolder = "Assets/Data/DifficultyLadder";
    private const string LadderPath = LadderFolder + "/StandardLadder.asset";

    private const string RandomLevelFolder = "Assets/Data/LevelData/Randomized";
    private const string CampaignPath = "Assets/Data/RunData/RealRun - Random.asset";

    private const string MagePath = "Assets/Data/Characters/Mage.asset";
    private const string RoguePath = "Assets/Data/Characters/Rogue.asset";
    private const string KnightPath = "Assets/Data/Characters/Knight.asset";
    private const string ClericPath = "Assets/Data/Characters/Cleric.asset";
    private const string RosterPath = "Assets/Data/Characters/DefaultRoster.asset";

    /// The act-1 levels, in play order. Named explicitly rather than globbed so appending act 2 is
    /// idempotent by construction - the campaign's level list is rebuilt from this plus NewLevels on
    /// every run, never appended to, so running twice cannot double it.
    private static readonly string[] ActOneLevels =
    {
        RandomLevelFolder + "/Level 1 - Random.asset",
        RandomLevelFolder + "/Level 2 - Random.asset",
        RandomLevelFolder + "/Level 3 - Random.asset",
        RandomLevelFolder + "/Level 4 - Random.asset",
        RandomLevelFolder + "/Level 5 - Boss.asset",
    };

    /// <summary>
    /// One act-2 level to author. anyPower and reinforcementPower extrapolate the 10/15/20/25 ramp act
    /// 1 already walks; the per-body caps rise more slowly, so a later level fields more bodies rather
    /// than the same count of bigger ones. `template` is the act-1 level whose loot tables, tilesets,
    /// pool filter and party spawn cells a newly created asset is seeded from.
    /// </summary>
    private struct LevelSpec
    {
        public string path;
        public string template;
        public float anyPower;
        public float reinforcementPower;
        public float maxPowerPerEnemy;
        public float maxPowerPerWave;
        public float bossFrontlinePower;
        public float bossBacklinePower;
        public int bossCount;
    }

    private static readonly LevelSpec[] NewLevels =
    {
        new() { path = RandomLevelFolder + "/Level 6 - Random.asset",
                template = RandomLevelFolder + "/Level 4 - Random.asset",
                anyPower = 30, reinforcementPower = 80, maxPowerPerEnemy = 4, maxPowerPerWave = 10 },
        new() { path = RandomLevelFolder + "/Level 7 - Random.asset",
                template = RandomLevelFolder + "/Level 4 - Random.asset",
                anyPower = 35, reinforcementPower = 92, maxPowerPerEnemy = 4, maxPowerPerWave = 12 },
        new() { path = RandomLevelFolder + "/Level 8 - Random.asset",
                template = RandomLevelFolder + "/Level 4 - Random.asset",
                anyPower = 40, reinforcementPower = 105, maxPowerPerEnemy = 4, maxPowerPerWave = 12 },
        new() { path = RandomLevelFolder + "/Level 9 - Random.asset",
                template = RandomLevelFolder + "/Level 4 - Random.asset",
                anyPower = 45, reinforcementPower = 117, maxPowerPerEnemy = 5, maxPowerPerWave = 14 },
        new() { path = RandomLevelFolder + "/Level 10 - Boss.asset",
                template = RandomLevelFolder + "/Level 5 - Boss.asset",
                anyPower = 45, reinforcementPower = 117, maxPowerPerEnemy = 5, maxPowerPerWave = 14,
                bossCount = 1 },
    };

    /// <summary>
    /// Everything, in dependency order: heroes are locked first so the ladder can point at them, then
    /// the ladder exists so the campaign can be pointed at it.
    /// </summary>
    [MenuItem("Tools/Progression/Set Up Progression", priority = -700)]
    public static void SetUpProgression()
    {
        AuthorLockedCharacters();

        DifficultyLadder ladder = GenerateDifficultyLadder();

        GenerateActTwoLevels(ladder);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Progression: set up ladder, locked heroes and act 2.");
    }

    // ---- The ladder ----------------------------------------------------------------------------

    [MenuItem("Tools/Progression/Generate Difficulty Ladder")]
    private static void GenerateDifficultyLadderCommand()
    {
        GenerateDifficultyLadder();
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Writes the seven rungs - Normal plus Hard I to VI - and returns the live asset so a caller can
    /// point the campaign at it without a LoadAssetAtPath that might not have imported yet.
    ///
    /// The two scales alternate: each rung raises exactly one of encounter power and enemy health, so
    /// no two consecutive tiers are the same change twice. Both are stored as a bonus added to 1 - see
    /// DifficultyTier for why a raw multiplier would be unsafe.
    /// </summary>
    public static DifficultyLadder GenerateDifficultyLadder()
    {
        EnsureFolder(LadderFolder);

        DifficultyLadder ladder = AssetDatabase.LoadAssetAtPath<DifficultyLadder>(LadderPath);

        if (ladder == null)
        {
            ladder = ScriptableObject.CreateInstance<DifficultyLadder>();
            AssetDatabase.CreateAsset(ladder, LadderPath);
        }

        CharacterOption mage = AssetDatabase.LoadAssetAtPath<CharacterOption>(MagePath);
        CharacterOption rogue = AssetDatabase.LoadAssetAtPath<CharacterOption>(RoguePath);

        // name, extra encounter power, extra enemy health, who clearing it earns
        (string name, float power, float health, CharacterOption unlock)[] rungs =
        {
            ("Normal",   0.00f, 0.00f, rogue),
            ("Hard I",   0.25f, 0.00f, mage),
            ("Hard II",  0.25f, 0.25f, null),
            ("Hard III", 0.50f, 0.25f, null),
            ("Hard IV",  0.50f, 0.50f, null),
            ("Hard V",   0.75f, 0.50f, null),
            ("Hard VI",  0.75f, 0.75f, null),
        };

        SerializedObject so = new(ladder);
        SerializedProperty tiers = so.FindProperty("tiers");

        tiers.arraySize = rungs.Length;

        for (int i = 0; i < rungs.Length; i++)
        {
            SerializedProperty rung = tiers.GetArrayElementAtIndex(i);

            rung.FindPropertyRelative("displayName").stringValue = rungs[i].name;
            rung.FindPropertyRelative("extraEncounterPower").floatValue = rungs[i].power;
            rung.FindPropertyRelative("extraEnemyHealth").floatValue = rungs[i].health;

            SerializedProperty unlocks = rung.FindPropertyRelative("unlocksOnClear");

            unlocks.arraySize = rungs[i].unlock != null ? 1 : 0;

            if (rungs[i].unlock != null)
            {
                unlocks.GetArrayElementAtIndex(0).objectReferenceValue = rungs[i].unlock;
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ladder);

        if (mage == null || rogue == null)
        {
            Debug.LogWarning("Progression: Mage or Rogue CharacterOption not found, so the ladder was "
                             + $"written but unlocks no heroes. Expected {MagePath} and {RoguePath}.");
        }

        Debug.Log($"Progression: wrote {rungs.Length} difficulty rungs to {LadderPath}.");

        return ladder;
    }

    // ---- Locked heroes -------------------------------------------------------------------------

    /// <summary>
    /// Ships Mage and Rogue locked, leaves Knight and Cleric playable, and puts the roster in the order
    /// the select screen should open in - Knight, Cleric, then the two that are earned.
    ///
    /// Explicit unlockIds rather than the asset-name fallback: an id that follows the file name orphans
    /// every player's save the first time the asset is renamed, which DeckData's own tooltip warns
    /// about. Reordering the roster is safe in a way reordering an enum is not - it is a list of object
    /// references, not ids baked into other assets.
    /// </summary>
    [MenuItem("Tools/Progression/Author Locked Characters")]
    public static void AuthorLockedCharacters()
    {
        SetLocked(MagePath, locked: true, unlockId: "mage");
        SetLocked(RoguePath, locked: true, unlockId: "rogue");
        SetLocked(KnightPath, locked: false, unlockId: "knight");
        SetLocked(ClericPath, locked: false, unlockId: "cleric");

        ReorderRoster();

        AssetDatabase.SaveAssets();
    }

    private static void SetLocked(string path, bool locked, string unlockId)
    {
        CharacterOption option = AssetDatabase.LoadAssetAtPath<CharacterOption>(path);

        if (option == null)
        {
            Debug.LogWarning($"Progression: no CharacterOption at {path} - skipped.");
            return;
        }

        SerializedObject so = new(option);

        so.FindProperty("locked").boolValue = locked;
        so.FindProperty("unlockId").stringValue = unlockId;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(option);
    }

    /// Knight, Cleric, Mage, Rogue - the starting pair first, so a fresh party's default picks land on
    /// heroes the player actually has. CharacterSelectPanel.DefaultCharacterAt skips locked entries
    /// anyway; this makes the displayed order match the unlock order too.
    private static void ReorderRoster()
    {
        CharacterRoster roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);

        if (roster == null)
        {
            Debug.LogWarning($"Progression: no CharacterRoster at {RosterPath} - order left alone.");
            return;
        }

        string[] order = { KnightPath, ClericPath, MagePath, RoguePath };

        List<CharacterOption> wanted = new();

        foreach (string path in order)
        {
            CharacterOption option = AssetDatabase.LoadAssetAtPath<CharacterOption>(path);

            if (option != null) { wanted.Add(option); }
        }

        SerializedObject so = new(roster);
        SerializedProperty characters = so.FindProperty("characters");

        // Anything on the roster this command does not know about keeps its place at the end rather
        // than being dropped - a fifth hero added later must survive a re-run.
        for (int i = 0; i < characters.arraySize; i++)
        {
            CharacterOption existing =
                characters.GetArrayElementAtIndex(i).objectReferenceValue as CharacterOption;

            if (existing != null && !wanted.Contains(existing)) { wanted.Add(existing); }
        }

        characters.arraySize = wanted.Count;

        for (int i = 0; i < wanted.Count; i++)
        {
            characters.GetArrayElementAtIndex(i).objectReferenceValue = wanted[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(roster);

        Debug.Log("Progression: roster order is now Knight, Cleric, Mage, Rogue.");
    }

    // ---- Act 2 ---------------------------------------------------------------------------------

    [MenuItem("Tools/Progression/Generate Act 2 Levels")]
    private static void GenerateActTwoLevelsCommand()
    {
        GenerateActTwoLevels(AssetDatabase.LoadAssetAtPath<DifficultyLadder>(LadderPath));
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Authors levels 6 to 10 and rebuilds the campaign's level list as act 1 followed by act 2.
    ///
    /// `ladder` is passed in as a live object rather than loaded here, because a ladder created earlier
    /// in the same run has not necessarily been imported yet and LoadAssetAtPath would hand back null.
    /// </summary>
    public static void GenerateActTwoLevels(DifficultyLadder ladder)
    {
        List<LevelData> created = new();

        foreach (LevelSpec spec in NewLevels)
        {
            LevelData level = AuthorLevel(spec);

            if (level != null) { created.Add(level); }
        }

        WireCampaign(created, ladder);
    }

    private static LevelData AuthorLevel(LevelSpec spec)
    {
        LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(spec.path);
        bool created = level == null;

        if (created)
        {
            level = SeedFromTemplate(spec.template);
            AssetDatabase.CreateAsset(level, spec.path);
        }

        SerializedObject so = new(level);
        SerializedProperty budget = so.FindProperty("encounterBudget");

        // Act 2 draws from every enemy in the registry, early-game ones included, rather than the
        // late-game-only filter the act-1 template carries. On creation only - a filter set by hand
        // afterwards is a designer's choice and must survive a re-run.
        if (created) { budget.FindPropertyRelative("poolFilter").arraySize = 0; }

        // Every number this command owns, re-written on every run - repair, not skip.
        budget.FindPropertyRelative("anyPower").floatValue = spec.anyPower;
        budget.FindPropertyRelative("reinforcementPower").floatValue = spec.reinforcementPower;
        budget.FindPropertyRelative("maxPowerPerEnemy").floatValue = spec.maxPowerPerEnemy;
        budget.FindPropertyRelative("maxPowerPerWave").floatValue = spec.maxPowerPerWave;
        budget.FindPropertyRelative("bossFrontlinePower").floatValue = spec.bossFrontlinePower;
        budget.FindPropertyRelative("bossBacklinePower").floatValue = spec.bossBacklinePower;
        budget.FindPropertyRelative("bossCount").intValue = spec.bossCount;
        budget.FindPropertyRelative("waveInterval").intValue = 1;
        budget.FindPropertyRelative("waveIntervalJitter").intValue = 2;

        // Frontline and backline stay at 0: act 1 spends everything through anyPower, which rolls each
        // pick's side independently, and splitting the budget here would make act 2 the only part of
        // the game with a fixed front-to-back ratio.
        budget.FindPropertyRelative("frontlinePower").floatValue = 0f;
        budget.FindPropertyRelative("backlinePower").floatValue = 0f;

        // A larger board than act 1's 6x6, so the back half of the run reads as somewhere else and the
        // bigger encounters have room to stand.
        so.FindProperty("boardSize").vector2IntValue = new Vector2Int(7, 7);
        so.FindProperty("turnsToSurvive").intValue = 10;
        so.FindProperty("handSize").intValue = 5;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(level);

        return level;
    }

    /// <summary>
    /// A copy of an act-1 level, so a brand-new asset starts with real loot tables, tilesets and party
    /// spawn cells rather than an empty board. The template's pool filter comes across too, but
    /// AuthorLevel clears it straight after - act 2 draws from every enemy, not act 1's late pool.
    ///
    /// Used on creation only, deliberately. These are the fields a designer is most likely to retune by
    /// hand, and re-running this command must not stomp that - unlike the budget numbers in
    /// AuthorLevel, which this command owns outright and always re-writes.
    /// </summary>
    private static LevelData SeedFromTemplate(string templatePath)
    {
        LevelData template = AssetDatabase.LoadAssetAtPath<LevelData>(templatePath);

        if (template == null)
        {
            Debug.LogWarning($"Progression: no template level at {templatePath} - the new level starts "
                             + "empty and needs a tileset and loot table by hand.");

            return ScriptableObject.CreateInstance<LevelData>();
        }

        return Object.Instantiate(template);
    }

    /// <summary>
    /// Points the real campaign at ten levels, the ladder, and awarding progression.
    ///
    /// The level list is rebuilt from ActOneLevels plus what was just created rather than appended to,
    /// which is what makes running this twice leave ten levels rather than fifteen.
    /// </summary>
    private static void WireCampaign(List<LevelData> actTwo, DifficultyLadder ladder)
    {
        RunData campaign = AssetDatabase.LoadAssetAtPath<RunData>(CampaignPath);

        if (campaign == null)
        {
            Debug.LogError($"Progression: no RunData at {CampaignPath} - act 2 was authored but "
                           + "nothing plays it.");
            return;
        }

        List<LevelData> all = new();

        foreach (string path in ActOneLevels)
        {
            LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(path);

            if (level != null) { all.Add(level); }
            else { Debug.LogWarning($"Progression: act-1 level missing at {path} - campaign is short."); }
        }

        all.AddRange(actTwo);

        SerializedObject so = new(campaign);
        SerializedProperty levels = so.FindProperty("levels");

        levels.arraySize = all.Count;

        for (int i = 0; i < all.Count; i++)
        {
            levels.GetArrayElementAtIndex(i).objectReferenceValue = all[i];
        }

        so.FindProperty("ladder").objectReferenceValue = ladder;
        so.FindProperty("awardsProgression").boolValue = true;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(campaign);

        Debug.Log($"Progression: {campaign.name} now plays {all.Count} levels"
                  + (ladder != null ? " on the standard ladder." : " with NO ladder assigned."));
    }

    /// Every level that draws from the whole EnemyRegistry rather than a hand-picked pool: Level 3 on.
    /// Levels 1 and 2 keep their early-game pools, which is what makes the opening of a run gentle.
    private static readonly string[] AllEnemyLevels =
    {
        RandomLevelFolder + "/Level 3 - Random.asset",
        RandomLevelFolder + "/Level 4 - Random.asset",
        RandomLevelFolder + "/Level 5 - Boss.asset",
        RandomLevelFolder + "/Level 6 - Random.asset",
        RandomLevelFolder + "/Level 7 - Random.asset",
        RandomLevelFolder + "/Level 8 - Random.asset",
        RandomLevelFolder + "/Level 9 - Random.asset",
        RandomLevelFolder + "/Level 10 - Boss.asset",
    };

    /// <summary>
    /// Clears the pool filter on Levels 3-10 so they draw from every enemy in the EnemyRegistry - early
    /// game and late game alike - instead of the late-game-only lists Levels 3-5 were authored with and
    /// the first version of this generator copied into act 2. An empty filter is what "everything"
    /// means; see EncounterBudget.poolFilter. The boss levels' boss slots still draw from the registry's
    /// boss prefabs, the same eight their filters already listed.
    ///
    /// Touches the pool filter and nothing else, deliberately separate from Generate Act 2 Levels:
    /// that command re-writes the budget numbers it owns, and these levels have been hand-tuned since.
    /// A level already drawing from everything is skipped, so running this twice changes nothing.
    /// Recorded with Undo, so Ctrl+Z puts the old filters back.
    /// </summary>
    [MenuItem("Tools/Progression/Levels 3-10 Draw From All Enemies")]
    public static void DrawFromAllEnemies()
    {
        int cleared = 0;

        foreach (string path in AllEnemyLevels)
        {
            LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(path);

            if (level == null)
            {
                Debug.LogWarning($"Progression: no level at {path} - skipped.");
                continue;
            }

            SerializedObject so = new(level);
            SerializedProperty filter = so.FindProperty("encounterBudget").FindPropertyRelative("poolFilter");

            if (filter.arraySize == 0) { continue; }

            int before = filter.arraySize;
            filter.arraySize = 0;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(level);
            cleared++;

            Debug.Log($"Progression: {level.name} pool filter {before} -> all enemies.");
        }

        AssetDatabase.SaveAssets();

        Debug.Log(cleared > 0
            ? $"Progression: {cleared} level(s) now draw from every enemy in the registry."
            : "Progression: Levels 3-10 already draw from every enemy - nothing to change.");
    }

    /// The two boss levels, one per act.
    private static readonly string[] BossLevels =
    {
        RandomLevelFolder + "/Level 5 - Boss.asset",
        RandomLevelFolder + "/Level 10 - Boss.asset",
    };

    /// <summary>
    /// Makes each boss level place exactly one random boss on either side: bossCount 1, and both boss
    /// power budgets back to 0. Before this, Level 5 always stood its boss in the backline
    /// (bossBacklinePower only), and Level 10 set both budgets and so fielded two bosses every battle.
    ///
    /// Touches those three fields and nothing else, so tuned budgets and caps survive. Recorded with
    /// Undo; running it twice changes nothing.
    /// </summary>
    [MenuItem("Tools/Progression/Boss Levels Draw One Random Boss")]
    public static void OneRandomBoss()
    {
        foreach (string path in BossLevels)
        {
            LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(path);

            if (level == null)
            {
                Debug.LogWarning($"Progression: no level at {path} - skipped.");
                continue;
            }

            SerializedObject so = new(level);
            SerializedProperty budget = so.FindProperty("encounterBudget");

            budget.FindPropertyRelative("bossCount").intValue = 1;
            budget.FindPropertyRelative("bossFrontlinePower").floatValue = 0f;
            budget.FindPropertyRelative("bossBacklinePower").floatValue = 0f;

            if (so.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(level);
                Debug.Log($"Progression: {level.name} now places 1 random boss on either side.");
            }
        }

        AssetDatabase.SaveAssets();
    }

    // ---- Testing commands ----------------------------------------------------------------------

    [MenuItem("Tools/Unlocks/Unlock All Characters")]
    private static void UnlockAllCharacters() =>
        ForEveryCharacter(option => CharacterUnlocks.Unlock(option), "unlocked");

    [MenuItem("Tools/Unlocks/Clear All Character Unlocks")]
    private static void ClearAllCharacterUnlocks() =>
        ForEveryCharacter(CharacterUnlocks.Lock, "cleared the unlock state of");

    [MenuItem("Tools/Unlocks/Unlock All Difficulty Tiers")]
    private static void UnlockAllTiers()
    {
        DifficultyLadder ladder = AssetDatabase.LoadAssetAtPath<DifficultyLadder>(LadderPath);

        if (ladder == null)
        {
            Debug.LogWarning($"Progression: no ladder at {LadderPath} - run Generate Difficulty Ladder.");
            return;
        }

        DifficultyProgress.UnlockAll(ladder);

        Debug.Log($"Progression: every rung up to {ladder.NameAt(ladder.Tiers.Count - 1)} is selectable.");
    }

    [MenuItem("Tools/Unlocks/Reset Difficulty Progress")]
    private static void ResetDifficultyProgress()
    {
        DifficultyProgress.Reset();

        Debug.Log("Progression: difficulty progress cleared - Normal only, as on a fresh save.");
    }

    private static void ForEveryCharacter(System.Action<CharacterOption> apply, string pastTenseVerb)
    {
        int count = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:CharacterOption"))
        {
            CharacterOption option =
                AssetDatabase.LoadAssetAtPath<CharacterOption>(AssetDatabase.GUIDToAssetPath(guid));

            if (option == null) { continue; }

            apply(option);
            count++;
        }

        Debug.Log($"Progression: {pastTenseVerb} {count} character(s).");
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) { return; }

        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(folder);

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
