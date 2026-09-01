using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the debug testbed: one 8×8 level with all four heroes, every class's totems in its own Test
/// deck, a hand of 7, and a card that refills energy on demand.
///
/// Separate from TotemContentGenerator because nothing here ships. Every asset it writes lives under a
/// Debug folder or is named *Test, and the one card it mints is Rarity.NotOffered and
/// excludeFromRewards, so a real run can never draw any of it. Keeping that in its own file is what
/// makes "does this reach the shipped game" answerable by looking at which generator wrote a thing.
///
/// Idempotent in the same two-speed way TotemContentGenerator is: assets are created if missing and
/// otherwise left alone, while the level and the run - the two things whose whole content is authored
/// here - are rewritten every run so a change to the layout below actually reaches disk.
///
/// Deck edits are append-only. A Test deck is somewhere a person also drops cards by hand mid-session,
/// and a generator that rebuilt the list would throw those away on every run.
/// </summary>
public static class DebugTestbedGenerator
{
    private const string CardRoot = "Assets/Data/CardData";
    private const string EffectRoot = "Assets/Data/EffectData";
    private const string DeckRoot = "Assets/Data/DeckData";
    private const string LevelRoot = "Assets/Data/LevelData";
    private const string RunRoot = "Assets/Data/RunData";
    private const string DebugCardFolder = CardRoot + "/Debug";
    private const string DebugPrefabFolder = "Assets/Prefabs/Enemies/Debug";

    private const string SkeletonSource = "Assets/Prefabs/Enemies/Skeleton.prefab";
    private const string TankSource = "Assets/Prefabs/Enemies/Golem.prefab";

    /// Board is 8×8 and cells are 1-based - see the OneBasedCell attribute on EnemyPlacement.cell.
    private static readonly Vector2Int BoardSize = new(8, 8);

    /// Turns before the level declares itself survived. High enough that it never ends underneath a
    /// testing session - this board is somewhere to stand and try things, not something to win.
    private const int TurnsToSurvive = 999;

    /// Wider than the normal 5 so there is plenty on screen to try, but not so wide that the fan runs
    /// off the edges - 10 did. ActiveHandViewer lays the hand out along a fixed arc, so the ceiling
    /// here is how many cards fit on screen rather than anything about the rules.
    private const int DebugHandSize = 7;

    /// <summary>
    /// Where everything stands. Row 1 is the bottom.
    ///
    ///   8 | .  T  .  .  .  .  T  .    tanky, six rows away
    ///   7 | .  .  .  .  .  .  .  .
    ///   6 | .  .  .  .  .  .  .  .
    ///   5 | .  .  .  s  .  s  .  .    1 HP skeletons
    ///   4 | .  .  .  .  .  .  .  .
    ///   3 | .  .  .  .  .  .  .  .
    ///   2 | .  H  H  H  H  .  .  .    the four heroes
    ///   1 | .  .  .  .  .  .  .  .
    ///
    /// The gap matters as much as the placements: three empty rows between the party and the skeletons
    /// is room to drop a totem and watch something walk into it, which is the only way to see an aura
    /// apply and then fall off again.
    /// </summary>
    private static readonly Vector2Int[] PartyCells =
    {
        new(2, 2), new(3, 2), new(4, 2), new(5, 2),
    };

    private static readonly Vector2Int[] SkeletonCells = { new(4, 5), new(6, 5) };

    private static readonly Vector2Int[] TankCells = { new(2, 8), new(7, 8) };

    /// The four heroes, paired with the Test deck each one plays.
    private static readonly (string prefab, string deck)[] Party =
    {
        ("Assets/Prefabs/Player/PlayerKnight.prefab", DeckRoot + "/KnightTest.asset"),
        ("Assets/Prefabs/Player/PlayerMage.prefab", DeckRoot + "/MageTest.asset"),
        ("Assets/Prefabs/Player/PlayerRogue.prefab", DeckRoot + "/RogueTest.asset"),
        ("Assets/Prefabs/Player/PlayerCleric.prefab", DeckRoot + "/ClericTest.asset"),
    };

    [MenuItem("Tools/Debug/Generate Debug Testbed")]
    public static void Generate()
    {
        CardData overflow = CreateOverflowCard();
        GameObject brittleSkeleton = CreateBrittleSkeleton();

        StockTestDecks(overflow);

        LevelData level = CreateLevel(brittleSkeleton);
        CreateRun(level);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Debug testbed: generation complete. Tick Debug Run on the main menu and press Play.");
    }

    // ------------------------------------------------------------------------------------------
    // The energy card
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Overflow: 0 cost, gain 3 energy, Innate so it is in hand every turn, Rebound so it is back in
    /// hand the instant it resolves. Together that is unlimited energy, deliberately - it is what the
    /// testbed uses instead of a debug switch on Character, so the "cheat" is a card you can see in
    /// your hand and choose not to play.
    ///
    /// NotOffered and excludeFromRewards so no real run can ever draw it. It only reaches a hand by
    /// being authored into a deck, which is exactly what StockTestDecks does and nothing else does.
    /// </summary>
    private static CardData CreateOverflowCard()
    {
        CardEffect energy = CreateEnergyEffect($"{EffectRoot}/Energy/Energy 3.asset", 3);

        string path = $"{DebugCardFolder}/Overflow.asset";

        CardData existing = AssetDatabase.LoadAssetAtPath<CardData>(path);
        if (existing != null) { return existing; }

        EnsureFolder(DebugCardFolder);

        CardData card = ScriptableObject.CreateInstance<CardData>();
        AssetDatabase.CreateAsset(card, path);

        SerializedObject so = new(card);
        so.FindProperty("<cardName>k__BackingField").stringValue = "Overflow";
        so.FindProperty("<cost>k__BackingField").intValue = 0;
        so.FindProperty("<requiredClass>k__BackingField").intValue = (int)CharacterClass.Any;
        so.FindProperty("<rarity>k__BackingField").intValue = (int)Rarity.NotOffered;
        so.FindProperty("<excludeFromRewards>k__BackingField").boolValue = true;
        so.FindProperty("<description>k__BackingField").stringValue =
            "Gain 3 energy. Returns to your hand.";

        SerializedProperty range = so.FindProperty("<range>k__BackingField");
        range.FindPropertyRelative("shape").intValue = (int)RangeShape.SelfTile;
        range.FindPropertyRelative("minDistance").intValue = 0;
        range.FindPropertyRelative("maxDistance").intValue = 0;

        SerializedProperty keywords = so.FindProperty("<keywords>k__BackingField");
        keywords.arraySize = 2;
        keywords.GetArrayElementAtIndex(0).FindPropertyRelative("type").intValue =
            (int)CardKeywordType.Innate;
        keywords.GetArrayElementAtIndex(0).FindPropertyRelative("magnitude").intValue = 0;
        keywords.GetArrayElementAtIndex(1).FindPropertyRelative("type").intValue =
            (int)CardKeywordType.Rebound;
        keywords.GetArrayElementAtIndex(1).FindPropertyRelative("magnitude").intValue = 0;

        SerializedProperty entries = so.FindProperty("<effectEntries>k__BackingField");
        entries.arraySize = 1;
        SerializedProperty entry = entries.GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("effect").objectReferenceValue = energy;
        entry.FindPropertyRelative("aimsAt").intValue = (int)EffectTarget.Source;

        SerializedProperty area = entry.FindPropertyRelative("area");
        area.FindPropertyRelative("kind").intValue = (int)AreaKind.Single;
        area.FindPropertyRelative("pattern").objectReferenceValue = null;

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(card);

        Debug.Log($"Debug testbed: created {path}");
        return card;
    }

    private static CardEffect CreateEnergyEffect(string path, int amount)
    {
        EnergyEffect existing = AssetDatabase.LoadAssetAtPath<EnergyEffect>(path);
        if (existing != null) { return existing; }

        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

        EnergyEffect effect = ScriptableObject.CreateInstance<EnergyEffect>();
        AssetDatabase.CreateAsset(effect, path);

        SerializedObject so = new(effect);
        so.FindProperty("amount").intValue = amount;
        so.ApplyModifiedProperties();

        Debug.Log($"Debug testbed: created {path}");
        return effect;
    }

    // ------------------------------------------------------------------------------------------
    // The brittle skeleton
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// A 1-health Skeleton, for watching something die to the first thing that touches it.
    ///
    /// Its own prefab rather than a health override on EnemyPlacement, because there is no such
    /// override - stats live on the prefab, deliberately, so that there is one obvious place to tune a
    /// skeleton. This is a second skeleton rather than an edit to the first for the same reason.
    /// </summary>
    private static GameObject CreateBrittleSkeleton()
    {
        string path = $"{DebugPrefabFolder}/BrittleSkeleton.prefab";

        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) { return existing; }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonSource) == null)
        {
            Debug.LogError($"Debug testbed: no skeleton to copy at {SkeletonSource}.");
            return null;
        }

        EnsureFolder(DebugPrefabFolder);

        GameObject root = PrefabUtility.LoadPrefabContents(SkeletonSource);

        SerializedObject so = new(root.GetComponent<Character>());
        so.FindProperty("displayName").stringValue = "Brittle Skeleton";
        so.FindProperty("maxHealth").intValue = 1;
        so.ApplyModifiedProperties();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);

        Debug.Log($"Debug testbed: created {path}");
        return saved;
    }

    // ------------------------------------------------------------------------------------------
    // Test decks
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Gives each class's Test deck every totem card authored for that class, plus Overflow.
    ///
    /// Class-matched rather than everything-everywhere, because Character.BuildDeck drops any card
    /// whose requiredClass does not match and only warns - so a Cleric totem in the Knight's deck would
    /// not be a bonus, it would be a card that silently is not there.
    ///
    /// Append-only, and skips anything already present, so running this twice does not duplicate and a
    /// card added by hand between runs survives.
    /// </summary>
    private static void StockTestDecks(CardData overflow)
    {
        Dictionary<CharacterClass, List<CardData>> totems = FindTotemsByClass();

        foreach ((string prefabPath, string deckPath) in Party)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"Debug testbed: no hero prefab at {prefabPath}.");
                continue;
            }

            Character character = prefab.GetComponent<Character>();
            if (character == null)
            {
                Debug.LogError($"Debug testbed: {prefabPath} has no Character.");
                continue;
            }

            CharacterClass heroClass = character.Class;

            DeckData deck = EnsureDeck(deckPath, heroClass);
            if (deck == null) { continue; }

            List<CardData> wanted = new();
            if (totems.TryGetValue(heroClass, out List<CardData> mine)) { wanted.AddRange(mine); }
            if (overflow != null) { wanted.Add(overflow); }

            AppendCards(deck, wanted);
        }
    }

    /// <summary>
    /// Every totem summon card, bucketed by the class it is authored for.
    ///
    /// Scans the four hero classes' own Summon folders rather than the whole CardData tree, which is
    /// what keeps enemy summons (Assets/Data/CardData/Enemy/...) out of a hero's deck.
    /// </summary>
    private static Dictionary<CharacterClass, List<CardData>> FindTotemsByClass()
    {
        Dictionary<CharacterClass, List<CardData>> byClass = new();

        foreach (string folder in new[] { "Knight", "Mage", "Rogue", "Cleric" })
        {
            string path = $"{CardRoot}/{folder}/Summon";
            if (!AssetDatabase.IsValidFolder(path)) { continue; }

            foreach (string guid in AssetDatabase.FindAssets("t:CardData", new[] { path }))
            {
                CardData card = AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(guid));
                if (card == null) { continue; }

                if (!byClass.TryGetValue(card.requiredClass, out List<CardData> list))
                {
                    list = new List<CardData>();
                    byClass[card.requiredClass] = list;
                }

                list.Add(card);
            }
        }

        return byClass;
    }

    private static DeckData EnsureDeck(string path, CharacterClass forClass)
    {
        DeckData existing = AssetDatabase.LoadAssetAtPath<DeckData>(path);
        if (existing != null) { return existing; }

        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

        DeckData deck = ScriptableObject.CreateInstance<DeckData>();
        AssetDatabase.CreateAsset(deck, path);

        SerializedObject so = new(deck);
        so.FindProperty("displayName").stringValue = Path.GetFileNameWithoutExtension(path);
        so.FindProperty("description").stringValue = "Debug testbed deck - every totem for this class.";
        so.FindProperty("forClass").intValue = (int)forClass;
        so.ApplyModifiedProperties();

        Debug.Log($"Debug testbed: created {path}");
        return deck;
    }

    private static void AppendCards(DeckData deck, List<CardData> cards)
    {
        SerializedObject so = new(deck);
        SerializedProperty list = so.FindProperty("cards");

        HashSet<Object> present = new();
        for (int i = 0; i < list.arraySize; i++)
        {
            present.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
        }

        int added = 0;
        foreach (CardData card in cards)
        {
            if (card == null || !present.Add(card)) { continue; }

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = card;
            added++;
        }

        if (added == 0) { return; }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(deck);

        Debug.Log($"Debug testbed: added {added} card(s) to {deck.name}");
    }

    // ------------------------------------------------------------------------------------------
    // The level and the run
    // ------------------------------------------------------------------------------------------

    /// Rewritten in full on every run, unlike the assets above: the layout in this file is the whole
    /// definition of this level, so a change here that did not reach disk would be a lie.
    private static LevelData CreateLevel(GameObject brittleSkeleton)
    {
        string path = $"{LevelRoot}/DebugTestbed.asset";

        LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(path);
        if (level == null)
        {
            EnsureFolder(LevelRoot);
            level = ScriptableObject.CreateInstance<LevelData>();
            AssetDatabase.CreateAsset(level, path);
            Debug.Log($"Debug testbed: created {path}");
        }

        GameObject tank = AssetDatabase.LoadAssetAtPath<GameObject>(TankSource);
        if (tank == null) { Debug.LogError($"Debug testbed: no tanky enemy at {TankSource}."); }

        SerializedObject so = new(level);

        so.FindProperty("boardSize").vector2IntValue = BoardSize;
        so.FindProperty("turnsToSurvive").intValue = TurnsToSurvive;
        so.FindProperty("handSize").intValue = DebugHandSize;

        SerializedProperty spawns = so.FindProperty("partySpawnCells");
        spawns.arraySize = PartyCells.Length;
        for (int i = 0; i < PartyCells.Length; i++)
        {
            spawns.GetArrayElementAtIndex(i).vector2IntValue = PartyCells[i];
        }

        SerializedProperty enemies = so.FindProperty("enemies");
        enemies.arraySize = 0;

        AddEnemies(enemies, brittleSkeleton, SkeletonCells);
        AddEnemies(enemies, tank, TankCells);

        // No reinforcements and no rolled budget: this board should hold still while you test on it.
        so.FindProperty("waves").arraySize = 0;

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(level);

        return level;
    }

    private static void AddEnemies(SerializedProperty enemies, GameObject prefab, Vector2Int[] cells)
    {
        if (prefab == null) { return; }

        foreach (Vector2Int cell in cells)
        {
            enemies.arraySize++;
            SerializedProperty placement = enemies.GetArrayElementAtIndex(enemies.arraySize - 1);

            placement.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            placement.FindPropertyRelative("cell").vector2IntValue = cell;
            placement.FindPropertyRelative("deckOverride").arraySize = 0;
        }
    }

    /// <summary>
    /// A run of exactly one level, with the four heroes on their Test decks.
    ///
    /// Its own asset rather than a rewrite of the existing DebugRun, which is a four-level parade with
    /// its own purpose - overwriting it would quietly cost somebody that.
    /// </summary>
    private static void CreateRun(LevelData level)
    {
        string path = $"{RunRoot}/DebugTestbedRun.asset";

        RunData run = AssetDatabase.LoadAssetAtPath<RunData>(path);
        if (run == null)
        {
            EnsureFolder(RunRoot);
            run = ScriptableObject.CreateInstance<RunData>();
            AssetDatabase.CreateAsset(run, path);
            Debug.Log($"Debug testbed: created {path}");
        }

        SerializedObject so = new(run);

        SerializedProperty levels = so.FindProperty("levels");
        levels.arraySize = 1;
        levels.GetArrayElementAtIndex(0).objectReferenceValue = level;

        SerializedProperty heroes = so.FindProperty("startingHeroes");
        heroes.arraySize = Party.Length;

        for (int i = 0; i < Party.Length; i++)
        {
            SerializedProperty entry = heroes.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("prefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(Party[i].prefab);
            entry.FindPropertyRelative("deck").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<DeckData>(Party[i].deck);
        }

        // StartingParty is the standalone-play fallback for opening Game.unity directly; kept in sync
        // with StartingHeroes so pressing Play in that scene lands in the same testbed.
        SerializedProperty fallback = so.FindProperty("startingParty");
        fallback.arraySize = Party.Length;

        SerializedProperty fallbackDecks = so.FindProperty("startingPartyDecks");
        fallbackDecks.arraySize = Party.Length;

        for (int i = 0; i < Party.Length; i++)
        {
            fallback.GetArrayElementAtIndex(i).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(Party[i].prefab);
            fallbackDecks.GetArrayElementAtIndex(i).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<DeckData>(Party[i].deck);
        }

        so.FindProperty("carryDamageBetweenLevels").boolValue = false;

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(run);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) { return; }

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
