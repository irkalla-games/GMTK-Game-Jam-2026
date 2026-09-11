using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates every asset the tutorial level needs: the two prefab variants for the heroes, the two for
/// the enemies, the two tutorial decks, the loot table that drops Heal Totems, the level itself (with its
/// two reinforcement waves) and the prologue run that plays it.
///
/// Same shape as TotemContentGenerator and EquipmentExampleContent: idempotent and re-runnable. An
/// asset that already exists is reused rather than recreated, but **every field this generator owns is
/// re-written on every run**, references included. That is deliberate - it makes a re-run a repair, so
/// a half-built set of assets can be fixed by running the command again rather than by hand-deleting
/// files. Tune numbers in the Inspector *after* the last run, or move them to the constants below.
///
/// **No AssetDatabase.StartAssetEditing here, on purpose.** Batching defers the import of everything
/// created inside the block, so AssetDatabase.LoadAssetAtPath returns null for an asset this same run
/// just created - which silently writes null into every cross-reference and produces a complete-looking
/// set of assets that all point at nothing. Assets are created in dependency order instead, and each
/// one is passed down as a live object rather than re-loaded by path, so the reference never depends on
/// the import having caught up.
///
/// Prefabs are *copied* from existing ones rather than built field by field (PrefabUtility variants -
/// see MakeVariant), for the reason CardChoicePanelWiring gives for cloning the removal panel: the
/// nested shape - a character's whole component stack - comes along correct for free, and only the
/// handful of values that actually differ have to be written.
///
/// ## The board, and why it is 3 wide
///
/// Reinforcements arrive as directly-authored LevelData.EnemyWave entries now, not a card an enemy AI
/// casts - so unlike an AI-picked summon tile (EnemyBrain.TryFindSummon, "closest legal tile to the
/// Eye"), each wave's landing cell is just stated outright (RatCell, SecondWaveCell) and needs no
/// geometry to steer it. The board keeps its original 3x6 shape and hero/Eye cells anyway, for
/// continuity with every other authored range already tuned against them.
///
/// Coordinates below are 0-based, matching Vector2Int as GridManager actually reads it - a 3x6 board is
/// x in [0,2], y in [0,5]. (An earlier version of this map was written 1-based, which is what let
/// RangerCell (2,6) sit off-board undetected - it only worked because SpawnPlacement routes through
/// GridManager.NearestFreeSpawnTile.)
///
///     x:   0     1     2
///     y=5  .    EYE    .
///     y=4  .     .    RAT     <- SecondWaveCell (turn 3)
///     y=3  .     .    RAT     <- RatCell (turn 2), adjacent to both heroes below
///     y=2  .    CLR   KNI
///     y=1  .     .     .
///     y=0  .     .     .
///
/// Smite (1-4) reaches the Eye from the Cleric at (1,2) at distance 3; Slash (melee) reaches RatCell
/// from the Knight at (2,2); Move (1-1, routed) reaches the loot drop at RatCell from the Cleric
/// diagonally; Heal Totem (1-3) reaches every free tile beside the Knight from the drop; Withering Gaze
/// (2-4) can shoot either hero from the Eye. Resizing the board or moving EyeCell/KnightCell/ClericCell
/// means re-checking every one of these.
/// </summary>
public static class TutorialContentGenerator
{
    // ---- Where things come from -------------------------------------------------------------------

    private const string Smite = "Assets/Data/CardData/Cleric/Heal/Smite.asset";
    private const string Move = "Assets/Data/CardData/Generic/Move.asset";
    private const string HealTotem = "Assets/Data/CardData/Cleric/Summon/Heal Totem.asset";
    private const string Slash = "Assets/Data/CardData/Knight/Melee Attack/Slash.asset";
    private const string Shield = "Assets/Data/CardData/Knight/Buff (Defensive)/Shield.asset";
    private const string WitheringGaze = "Assets/Data/CardData/Enemy/FlyingEye/WitheringGaze.asset";

    private const string KnightPrefab = "Assets/Prefabs/Player/PlayerKnight.prefab";
    private const string ClericPrefab = "Assets/Prefabs/Player/PlayerCleric.prefab";
    private const string EyePrefab = "Assets/Prefabs/Enemies/FlyingEye.prefab";
    private const string RatPrefab = "Assets/Prefabs/Enemies/Rat.prefab";

    /// Already exactly what the Eye needs while it stands over a 1-HP Heal Totem - Closest only, and
    /// never hunts a totem instead. Reused rather than authored fresh: TotemContentGenerator's own
    /// ClosestIgnoreTotem.asset already is this pattern.
    private const string ClosestIgnoreTotem = "Assets/Data/TargetingData/ClosestIgnoreTotem.asset";

    // ---- Where things go --------------------------------------------------------------------------

    private const string KnightOut = "Assets/Prefabs/Player/PlayerKnightTutorial.prefab";
    private const string ClericOut = "Assets/Prefabs/Player/PlayerClericTutorial.prefab";
    private const string EyeOut = "Assets/Prefabs/Enemies/FlyingEyeTutorial.prefab";
    private const string RatOut = "Assets/Prefabs/Enemies/RatTutorial.prefab";

    private const string KnightDeckOut = "Assets/Data/DeckData/KnightTutorial.asset";
    private const string ClericDeckOut = "Assets/Data/DeckData/ClericTutorial.asset";
    private const string LootTableOut = "Assets/Data/LootTable/TutorialTotemDrop.asset";
    private const string LevelOut = "Assets/Data/LevelData/Tailored/Tutorial.asset";
    private const string RunOut = "Assets/Data/RunData/TutorialRun.asset";

    // ---- The numbers ------------------------------------------------------------------------------

    /// Resizing this or moving EyeCell/KnightCell/ClericCell means re-checking every authored range
    /// against them - see the class doc's closing paragraph.
    private static readonly Vector2Int BoardSize = new(3, 6);
    private static readonly Vector2Int EyeCell = new(1, 5);
    private static readonly Vector2Int KnightCell = new(2, 2);
    private static readonly Vector2Int ClericCell = new(1, 2);

    /// Chebyshev-adjacent to the Knight at (2,2), so Slash always reaches it - the turn-2 wave. See the
    /// class doc.
    private static readonly Vector2Int RatCell = new(2, 3);

    /// The turn-3 wave - what the spawn-preview beat shows during turn 2, and what turn 3's free play
    /// is spent surviving.
    private static readonly Vector2Int SecondWaveCell = new(2, 4);

    private const int TurnsToSurvive = 3;

    /// Big enough that every tutorial hand is that character's entire deck, so no shuffle can reorder
    /// what the script points at. The determinism the whole sequence rests on.
    private const int HandSize = 3;

    private const int RatHealth = 1;

    private const int TotemChoices = 3;

    [MenuItem("Tools/Tutorial/1 - Generate Tutorial Content")]
    public static void Generate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Tutorial content: exit Play Mode first.");
            return;
        }

        if (!RequireAll()) { return; }

        List<string> made = new();

        // Dependency order, each result handed to the next. Nothing is re-loaded by path after being
        // created - see the class doc for why that matters.
        LootTable loot = MakeLootTable(made);

        GameObject rat = MakeRat(loot, made);
        GameObject eye = MakeEye(made);

        GameObject knight = MakeVariant(KnightPrefab, KnightOut, made);
        GameObject cleric = MakeVariant(ClericPrefab, ClericOut, made);

        DeckData knightDeck = MakeDeck(KnightDeckOut, "Knight (Tutorial)",
            new[] { Shield, Slash, Move }, made);
        DeckData clericDeck = MakeDeck(ClericDeckOut, "Cleric (Tutorial)",
            new[] { Smite, Move }, made);

        LevelData level = MakeLevel(eye, rat, loot, made);
        MakeRun(level, knight, cleric, knightDeck, clericDeck, made);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(made.Count == 0
            ? "Tutorial content: all assets already existed - references re-applied.\n"
              + "Next: Tools > Tutorial > 2 - Wire Tutorial Overlay, then 3 - Wire Main Menu."
            : $"Tutorial content: created {made.Count} asset(s):\n  {string.Join("\n  ", made)}\n"
              + "Next: Tools > Tutorial > 2 - Wire Tutorial Overlay, then 3 - Wire Main Menu.");
    }

    // ---- Enemies ----------------------------------------------------------------------------------

    /// The 1-health rat the Knight kills, carrying the tutorial's own loot table so its drop is the
    /// three Heal Totems rather than whatever the level would otherwise roll - Character.LootTable
    /// overrides LevelData's for that character's own drop. displayName is overridden too, matching the
    /// Debug/BrittleSkeleton naming convention for a 1-HP variant of an existing enemy.
    private static GameObject MakeRat(LootTable loot, List<string> made)
    {
        GameObject prefab = MakeVariant(RatPrefab, RatOut, made);

        if (prefab == null) { return null; }

        SerializedObject so = CharacterOf(prefab);

        if (so == null) { return prefab; }

        so.FindProperty("maxHealth").intValue = RatHealth;
        so.FindProperty("lootTable").objectReferenceValue = loot;
        so.FindProperty("displayName").stringValue = "Brittle Rat";
        Apply(so, prefab);

        return prefab;
    }

    /// The Flying Eye, holding only two Withering Gazes - no Move card, so EnemyBrain can never walk it
    /// out of the Heal Totem's aura between the totem landing and the shot that would otherwise prove
    /// nothing - and Closest-only, never-hunt-totems targeting, so its 15%/turn totem hunt (the base
    /// prefab's Totem15Closest) can never destroy the 1-HP totem the payoff beat depends on.
    private static GameObject MakeEye(List<string> made)
    {
        GameObject prefab = MakeVariant(EyePrefab, EyeOut, made);

        if (prefab == null) { return null; }

        SerializedObject so = CharacterOf(prefab);

        if (so == null) { return prefab; }

        SetCards(so.FindProperty("deck"), new List<Object>
        {
            Load<CardData>(WitheringGaze),
            Load<CardData>(WitheringGaze),
        });

        so.FindProperty("targetingPattern").objectReferenceValue = Load<TargetingPattern>(ClosestIgnoreTotem);

        Apply(so, prefab);

        return prefab;
    }

    // ---- Decks, loot, level, run --------------------------------------------------------------------

    private static DeckData MakeDeck(string path, string displayName, string[] cardPaths, List<string> made)
    {
        DeckData deck = Load<DeckData>(path);

        if (deck == null)
        {
            deck = ScriptableObject.CreateInstance<DeckData>();
            AssetDatabase.CreateAsset(deck, path);
            made.Add(path);
        }

        SerializedObject so = new(deck);
        so.FindProperty("displayName").stringValue = displayName;
        so.FindProperty("description").stringValue = "The scripted tutorial hand. Not offered anywhere.";

        // Any rather than the real class: these are only ever fed in through RunData.StartingPartyDecks,
        // never offered at the select screen, so gating them would be a rule with no reader.
        so.FindProperty("forClass").intValue = (int)CharacterClass.Any;
        so.FindProperty("locked").boolValue = false;

        List<Object> cards = new();

        foreach (string cardPath in cardPaths) { cards.Add(Load<CardData>(cardPath)); }

        SetCards(so.FindProperty("cards"), cards);

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(deck);

        return deck;
    }

    /// Three guaranteed Heal Totems filling all three slots - which needs LootManager.BuildOffer to stop
    /// de-duplicating guaranteed entries against each other, the change this tutorial shipped with.
    private static LootTable MakeLootTable(List<string> made)
    {
        LootTable table = Load<LootTable>(LootTableOut);

        if (table == null)
        {
            table = ScriptableObject.CreateInstance<LootTable>();
            AssetDatabase.CreateAsset(table, LootTableOut);
            made.Add(LootTableOut);
        }

        SerializedObject so = new(table);

        SerializedProperty tiers = so.FindProperty("tierWeights");
        tiers.arraySize = 1;
        tiers.GetArrayElementAtIndex(0).FindPropertyRelative("rarity").enumValueIndex = (int)Rarity.Common;
        tiers.GetArrayElementAtIndex(0).FindPropertyRelative("weight").intValue = 1;

        CardData totem = Load<CardData>(HealTotem);
        SerializedProperty guaranteed = so.FindProperty("guaranteedCards");
        guaranteed.arraySize = TotemChoices;

        for (int i = 0; i < TotemChoices; i++)
        {
            guaranteed.GetArrayElementAtIndex(i).objectReferenceValue = totem;
        }

        so.FindProperty("choiceCount").intValue = TotemChoices;
        so.FindProperty("equipmentChance").floatValue = 0f;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(table);

        return table;
    }

    private static LevelData MakeLevel(GameObject eyePrefab, GameObject ratPrefab, LootTable loot,
        List<string> made)
    {
        LevelData level = Load<LevelData>(LevelOut);

        if (level == null)
        {
            level = ScriptableObject.CreateInstance<LevelData>();
            AssetDatabase.CreateAsset(level, LevelOut);
            made.Add(LevelOut);
        }

        SerializedObject so = new(level);

        SerializedProperty enemies = so.FindProperty("enemies");
        enemies.arraySize = 1;
        SerializedProperty eye = enemies.GetArrayElementAtIndex(0);
        eye.FindPropertyRelative("prefab").objectReferenceValue = eyePrefab;
        SetCell(eye.FindPropertyRelative("cell"), EyeCell);
        eye.FindPropertyRelative("deckOverride").arraySize = 0;

        // Reinforcements, not a card an enemy AI plays - see the class doc. Turn 2's wave is what the
        // Slash beat kills; turn 3's is what the spawn-preview beat shows during turn 2 and what free
        // play is spent surviving.
        SerializedProperty waves = so.FindProperty("waves");
        waves.arraySize = 2;

        SerializedProperty wave0 = waves.GetArrayElementAtIndex(0);
        wave0.FindPropertyRelative("turn").intValue = 2;
        SerializedProperty wave0Enemies = wave0.FindPropertyRelative("enemies");
        wave0Enemies.arraySize = 1;
        SerializedProperty wave0Enemy = wave0Enemies.GetArrayElementAtIndex(0);
        wave0Enemy.FindPropertyRelative("prefab").objectReferenceValue = ratPrefab;
        SetCell(wave0Enemy.FindPropertyRelative("cell"), RatCell);
        wave0Enemy.FindPropertyRelative("deckOverride").arraySize = 0;

        SerializedProperty wave1 = waves.GetArrayElementAtIndex(1);
        wave1.FindPropertyRelative("turn").intValue = 3;
        SerializedProperty wave1Enemies = wave1.FindPropertyRelative("enemies");
        wave1Enemies.arraySize = 1;
        SerializedProperty wave1Enemy = wave1Enemies.GetArrayElementAtIndex(0);
        wave1Enemy.FindPropertyRelative("prefab").objectReferenceValue = ratPrefab;
        SetCell(wave1Enemy.FindPropertyRelative("cell"), SecondWaveCell);
        wave1Enemy.FindPropertyRelative("deckOverride").arraySize = 0;

        // Index for index against the run's roster: slot 0 is the Knight, so pressing 1 selects him.
        SerializedProperty spawns = so.FindProperty("partySpawnCells");
        spawns.arraySize = 2;
        SetCell(spawns.GetArrayElementAtIndex(0), KnightCell);
        SetCell(spawns.GetArrayElementAtIndex(1), ClericCell);

        SetCell(so.FindProperty("boardSize"), BoardSize);

        so.FindProperty("turnsToSurvive").intValue = TurnsToSurvive;
        so.FindProperty("handSize").intValue = HandSize;
        so.FindProperty("lootTable").objectReferenceValue = loot;

        // No clear reward: the tutorial hands straight over to the real run, and an offer between the
        // two would be a choice the player has no context for yet.
        so.FindProperty("clearRewardTable").objectReferenceValue = null;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(level);

        return level;
    }

    private static void MakeRun(LevelData level, GameObject knight, GameObject cleric,
        DeckData knightDeck, DeckData clericDeck, List<string> made)
    {
        RunData run = Load<RunData>(RunOut);

        if (run == null)
        {
            run = ScriptableObject.CreateInstance<RunData>();
            AssetDatabase.CreateAsset(run, RunOut);
            made.Add(RunOut);
        }

        SerializedObject so = new(run);

        SerializedProperty levels = so.FindProperty("levels");
        levels.arraySize = 1;
        levels.GetArrayElementAtIndex(0).objectReferenceValue = level;

        // StartingParty, not StartingHeroes: this run never goes through character select, which is the
        // exact case StartingParty and its deck override exist for. Slot 0 is the Knight, so pressing 1
        // selects him - the order the script's copy assumes.
        SerializedProperty party = so.FindProperty("startingParty");
        party.arraySize = 2;
        party.GetArrayElementAtIndex(0).objectReferenceValue = knight;
        party.GetArrayElementAtIndex(1).objectReferenceValue = cleric;

        SerializedProperty decks = so.FindProperty("startingPartyDecks");
        decks.arraySize = 2;
        decks.GetArrayElementAtIndex(0).objectReferenceValue = knightDeck;
        decks.GetArrayElementAtIndex(1).objectReferenceValue = clericDeck;

        so.FindProperty("startingHeroes").arraySize = 0;
        so.FindProperty("carryDamageBetweenLevels").boolValue = false;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(run);
    }

    // ---- Helpers ------------------------------------------------------------------------------------

    /// <summary>
    /// A real Prefab Variant, not a copy - instantiating the base and saving that instance is what makes
    /// Unity record it as a variant. Being a variant is the point: future art, animator or stat changes
    /// to the source prefab carry into the tutorial variant on their own, where a flat copy would
    /// silently drift from the prefab it was meant to mirror.
    /// </summary>
    private static GameObject MakeVariant(string sourcePath, string outPath, List<string> made)
    {
        GameObject existing = Load<GameObject>(outPath);

        if (existing != null) { return existing; }

        GameObject source = Load<GameObject>(sourcePath);

        if (source == null)
        {
            Debug.LogError($"Tutorial content: no prefab at {sourcePath} - skipped {outPath}.");
            return null;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        GameObject variant = PrefabUtility.SaveAsPrefabAsset(instance, outPath);
        Object.DestroyImmediate(instance);

        made.Add(outPath);

        return variant;
    }

    private static SerializedObject CharacterOf(GameObject prefab)
    {
        Character character = prefab != null ? prefab.GetComponent<Character>() : null;

        if (character == null)
        {
            // Not prefab?.name - `?.` tests reference null and so sails straight past a fake-null Unity
            // object, throwing exactly where an error was being reported. See TutorialWiring.Ensure.
            Debug.LogError($"Tutorial content: {(prefab != null ? prefab.name : "null")} has no "
                           + "Character component.");
            return null;
        }

        return new SerializedObject(character);
    }

    private static void Apply(SerializedObject so, GameObject prefab)
    {
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(prefab);
        PrefabUtility.SavePrefabAsset(prefab);
    }

    private static void SetCards(SerializedProperty list, List<Object> cards)
    {
        list.arraySize = cards.Count;

        for (int i = 0; i < cards.Count; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
        }
    }

    private static void SetCell(SerializedProperty property, Vector2Int cell)
    {
        property.vector2IntValue = cell;
    }

    private static bool Exists(string path) => Load<Object>(path) != null;

    private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);

    /// Fails loudly and early rather than producing half the content - a partially generated tutorial is
    /// harder to diagnose than none at all, because the missing piece only shows up mid-script.
    private static bool RequireAll()
    {
        string[] required =
        {
            Smite, Move, HealTotem, Slash, Shield, WitheringGaze,
            KnightPrefab, ClericPrefab, EyePrefab, RatPrefab, ClosestIgnoreTotem,
        };

        bool ok = true;

        foreach (string path in required)
        {
            if (Exists(path)) { continue; }

            Debug.LogError($"Tutorial content: required source asset missing - {path}");
            ok = false;
        }

        return ok;
    }
}
