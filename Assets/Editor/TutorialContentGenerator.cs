using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates every asset the tutorial level needs: the two prefab variants for the heroes, the two for
/// the enemies, the reinforcement card and its summon effect, the two tutorial decks, the loot table
/// that drops Sap Totems, the level itself and the prologue run that plays it.
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
/// Cards and prefabs are *copied* from existing ones rather than built field by field
/// (AssetDatabase.CopyAsset, PrefabUtility variants), for the reason CardChoicePanelWiring gives for
/// cloning the removal panel: the nested shape - a card's effectEntries with its area and pattern, a
/// character's whole component stack - comes along correct for free, and only the handful of values
/// that actually differ have to be written.
///
/// ## The board, and why it is 3 wide
///
/// The skeleton has to land somewhere the Knight can reach with Slash, which is melee - and EnemyBrain's
/// TryFindSummon picks the closest legal empty tile to the *Ranger*, not to the Knight, so where it
/// lands is not something the tutorial gets to state directly. The board is shaped to make every answer
/// a right one instead.
///
/// Call Reinforcements is authored as a **ring**, minDistance == maxDistance == 3, so its legal tiles
/// are exactly the cells at Chebyshev 3 from the Ranger at (2,6) - which on a 3-wide board is the whole
/// row y=3. The Knight stands at (2,2), directly below the middle of that row. Chebyshev adjacency is
/// the eight surrounding tiles, diagonals included, so (1,3), (2,3) and (3,3) are *all* distance 1 from
/// him: wherever on the ring the skeleton lands, Slash reaches it.
///
///     x:   1     2     3
///     y=6  .    RNG    .
///     y=5  .     .     .     <- Sap Totem lands on one of these
///     y=4  .     .     .
///     y=3 SKL   SKL   SKL    <- the summon ring: the whole row, and every
///     y=2 MAG   KNI    .        cell of it is adjacent to the Knight below
///     y=1  .     .     .
///
/// That is deliberately stronger than blocking the ring down to a single free cell, which is what an
/// earlier distance-4 version did: it needed two specific cells occupied to work, so moving either hero
/// silently broke it, and the survivor was chosen by however GetTilesInRange happened to order two
/// equidistant tiles. Here nothing needs to be occupied and no tie-break matters.
///
/// Every other range in the script was checked against this layout: Fireball (1-5) reaches the Ranger
/// from the Mage at (1,2) at distance 4; Slash (melee) reaches every ring cell from (2,2); Teleport is
/// Anywhere; Sap Totem (1-3) reaches every free tile beside the Ranger from any row-3 drop, the row-5
/// ones at distance 2 and the row-6 ones at 3; EnemyArrow (1-6) can shoot back from (2,6).
/// </summary>
public static class TutorialContentGenerator
{
    // ---- Where things come from -------------------------------------------------------------------

    private const string Fireball = "Assets/Data/CardData/Mage/RangedAttack/Fireball.asset";
    private const string Teleport = "Assets/Data/CardData/Mage/Movement/Teleport.asset";
    private const string SapTotem = "Assets/Data/CardData/Mage/Summon/Sap Totem.asset";
    private const string Slash = "Assets/Data/CardData/Knight/Melee Attack/Slash.asset";
    private const string Shield = "Assets/Data/CardData/Knight/Buff (Defensive)/Shield.asset";
    private const string Move = "Assets/Data/CardData/Generic/Move.asset";
    private const string EnemyArrow = "Assets/Data/CardData/Enemy/EnemyArrow.asset";

    private const string SummonCardSource = "Assets/Data/CardData/Enemy/SummonSkeletonWarrior.asset";
    private const string SummonEffectSource = "Assets/Data/EffectData/Summon/SummonSkeletonWarrior.asset";

    private const string KnightPrefab = "Assets/Prefabs/Player/PlayerKnight.prefab";
    private const string MagePrefab = "Assets/Prefabs/Player/PlayerMage.prefab";
    private const string RangerPrefab = "Assets/Prefabs/Enemies/EnemyRanger.prefab";
    private const string SkeletonPrefab = "Assets/Prefabs/Enemies/SkeletonWarrior.prefab";

    // ---- Where things go --------------------------------------------------------------------------

    private const string SummonEffectOut = "Assets/Data/EffectData/Summon/SummonTutorialSkeleton.asset";
    private const string SummonCardOut = "Assets/Data/CardData/Enemy/CallReinforcements.asset";

    private const string KnightOut = "Assets/Prefabs/Player/PlayerKnightTutorial.prefab";
    private const string MageOut = "Assets/Prefabs/Player/PlayerMageTutorial.prefab";
    private const string RangerOut = "Assets/Prefabs/Enemies/EnemyRangerTutorial.prefab";
    private const string SkeletonOut = "Assets/Prefabs/Enemies/SkeletonWarriorTutorial.prefab";

    private const string KnightDeckOut = "Assets/Data/DeckData/KnightTutorial.asset";
    private const string MageDeckOut = "Assets/Data/DeckData/MageTutorial.asset";
    private const string LootTableOut = "Assets/Data/LootTable/TutorialSapDrop.asset";
    private const string LevelOut = "Assets/Data/LevelData/Tutorial.asset";
    private const string RunOut = "Assets/Data/RunData/TutorialRun.asset";

    // ---- The numbers ------------------------------------------------------------------------------

    private static readonly Vector2Int BoardSize = new(3, 6);
    private static readonly Vector2Int RangerCell = new(2, 6);
    private static readonly Vector2Int KnightCell = new(2, 2);
    private static readonly Vector2Int MageCell = new(1, 2);

    private const int TurnsToSurvive = 2;

    /// Big enough that every tutorial hand is that character's entire deck, so no shuffle can reorder
    /// what the script points at. The determinism the whole sequence rests on.
    private const int HandSize = 3;

    /// <summary>
    /// Chebyshev, minDistance == maxDistance - a ring, not a disc. 3 puts the ring on row y=3, one row
    /// above the Knight, which is what makes every cell of it adjacent to him. See the class doc.
    /// </summary>
    private const int SummonRing = 3;

    /// Longer than the level, so the Ranger summons on turn 1 and shoots on turn 2.
    private const int SummonCooldown = 4;

    private const int SkeletonHealth = 1;

    private const int SapChoices = 3;

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

        GameObject skeleton = MakeSkeleton(loot, made);
        CardEffect summonEffect = MakeSummonEffect(skeleton, made);
        CardData summonCard = MakeSummonCard(summonEffect, made);
        GameObject ranger = MakeRanger(summonCard, made);

        GameObject knight = MakeVariant(KnightPrefab, KnightOut, made);
        GameObject mage = MakeVariant(MagePrefab, MageOut, made);

        DeckData knightDeck = MakeDeck(KnightDeckOut, "Knight (Tutorial)",
            new[] { Shield, Slash, Move }, made);
        DeckData mageDeck = MakeDeck(MageDeckOut, "Mage (Tutorial)",
            new[] { Fireball, Teleport }, made);

        LevelData level = MakeLevel(ranger, loot, made);
        MakeRun(level, knight, mage, knightDeck, mageDeck, made);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(made.Count == 0
            ? "Tutorial content: all assets already existed - references re-applied.\n"
              + "Next: Tools > Tutorial > 2 - Wire Tutorial Overlay, then 3 - Wire Main Menu."
            : $"Tutorial content: created {made.Count} asset(s):\n  {string.Join("\n  ", made)}\n"
              + "Next: Tools > Tutorial > 2 - Wire Tutorial Overlay, then 3 - Wire Main Menu.");
    }

    // ---- Enemies ----------------------------------------------------------------------------------

    /// The 1-health skeleton the Knight kills, carrying the tutorial's own loot table so its drop is the
    /// three Sap Totems rather than whatever the level would otherwise roll - Character.LootTable
    /// overrides LevelData's for that character's own drop.
    private static GameObject MakeSkeleton(LootTable loot, List<string> made)
    {
        GameObject prefab = MakeVariant(SkeletonPrefab, SkeletonOut, made);

        if (prefab == null) { return null; }

        SerializedObject so = CharacterOf(prefab);

        if (so == null) { return prefab; }

        so.FindProperty("maxHealth").intValue = SkeletonHealth;
        so.FindProperty("lootTable").objectReferenceValue = loot;
        Apply(so, prefab);

        return prefab;
    }

    private static CardEffect MakeSummonEffect(GameObject skeleton, List<string> made)
    {
        CardEffect effect = CopyAsset<CardEffect>(SummonEffectSource, SummonEffectOut, made);

        if (effect == null || skeleton == null) { return effect; }

        SerializedObject so = new(effect);
        so.FindProperty("summonedObject").objectReferenceValue = skeleton;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(effect);

        return effect;
    }

    /// <summary>
    /// Call Reinforcements: the ring-ranged summon the whole board layout is built around.
    ///
    /// Copied from SummonSkeletonWarrior rather than built from scratch so its effectEntries shape comes
    /// along intact, then the four values that differ are overwritten - notably dropping that card's
    /// Dormant 3, which would make it unplayable for the first three turns of a two-turn level.
    /// </summary>
    private static CardData MakeSummonCard(CardEffect effect, List<string> made)
    {
        CardData card = CopyAsset<CardData>(SummonCardSource, SummonCardOut, made);

        if (card == null) { return null; }

        SerializedObject so = new(card);

        so.FindProperty("<cardName>k__BackingField").stringValue = "Call Reinforcements";
        so.FindProperty("<cost>k__BackingField").intValue = 0;
        so.FindProperty("<description>k__BackingField").stringValue =
            "Summon a Skeleton Warrior four tiles away.";

        SerializedProperty range = so.FindProperty("<range>k__BackingField");
        range.FindPropertyRelative("shape").enumValueIndex = (int)RangeShape.Chebyshev;
        range.FindPropertyRelative("minDistance").intValue = SummonRing;
        range.FindPropertyRelative("maxDistance").intValue = SummonRing;

        // Cooldown only - Dormant is deliberately dropped, see the doc comment.
        SerializedProperty keywords = so.FindProperty("<keywords>k__BackingField");
        keywords.arraySize = 1;
        SerializedProperty cooldown = keywords.GetArrayElementAtIndex(0);
        cooldown.FindPropertyRelative("type").enumValueIndex = (int)CardKeywordType.Cooldown;
        cooldown.FindPropertyRelative("magnitude").intValue = SummonCooldown;

        if (effect != null)
        {
            SerializedProperty entries = so.FindProperty("<effectEntries>k__BackingField");

            if (entries.arraySize > 0)
            {
                entries.GetArrayElementAtIndex(0).FindPropertyRelative("effect").objectReferenceValue = effect;
            }

            SerializedProperty legacy = so.FindProperty("<effects>k__BackingField");

            if (legacy.arraySize > 0) { legacy.GetArrayElementAtIndex(0).objectReferenceValue = effect; }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(card);

        return card;
    }

    /// The Ranger, holding only the reinforcement card and two arrows - no Move, so RangerBrain can
    /// never walk it out of the Sap totem's aura between the totem landing and the shot that proves it
    /// works.
    private static GameObject MakeRanger(CardData summonCard, List<string> made)
    {
        GameObject prefab = MakeVariant(RangerPrefab, RangerOut, made);

        if (prefab == null) { return null; }

        SerializedObject so = CharacterOf(prefab);

        if (so == null) { return prefab; }

        SetCards(so.FindProperty("deck"), new List<Object>
        {
            summonCard,
            Load<CardData>(EnemyArrow),
            Load<CardData>(EnemyArrow),
        });

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

    /// Three guaranteed Sap Totems filling all three slots - which needs LootManager.BuildOffer to stop
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

        CardData sap = Load<CardData>(SapTotem);
        SerializedProperty guaranteed = so.FindProperty("guaranteedCards");
        guaranteed.arraySize = SapChoices;

        for (int i = 0; i < SapChoices; i++)
        {
            guaranteed.GetArrayElementAtIndex(i).objectReferenceValue = sap;
        }

        so.FindProperty("choiceCount").intValue = SapChoices;
        so.FindProperty("equipmentChance").floatValue = 0f;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(table);

        return table;
    }

    private static LevelData MakeLevel(GameObject rangerPrefab, LootTable loot, List<string> made)
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
        SerializedProperty ranger = enemies.GetArrayElementAtIndex(0);
        ranger.FindPropertyRelative("prefab").objectReferenceValue = rangerPrefab;
        SetCell(ranger.FindPropertyRelative("cell"), RangerCell);
        ranger.FindPropertyRelative("deckOverride").arraySize = 0;

        so.FindProperty("waves").arraySize = 0;

        // Index for index against the run's roster: slot 0 is the Knight, so pressing 1 selects him.
        SerializedProperty spawns = so.FindProperty("partySpawnCells");
        spawns.arraySize = 2;
        SetCell(spawns.GetArrayElementAtIndex(0), KnightCell);
        SetCell(spawns.GetArrayElementAtIndex(1), MageCell);

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

    private static void MakeRun(LevelData level, GameObject knight, GameObject mage,
        DeckData knightDeck, DeckData mageDeck, List<string> made)
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
        party.GetArrayElementAtIndex(1).objectReferenceValue = mage;

        SerializedProperty decks = so.FindProperty("startingPartyDecks");
        decks.arraySize = 2;
        decks.GetArrayElementAtIndex(0).objectReferenceValue = knightDeck;
        decks.GetArrayElementAtIndex(1).objectReferenceValue = mageDeck;

        so.FindProperty("startingHeroes").arraySize = 0;
        so.FindProperty("carryDamageBetweenLevels").boolValue = false;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(run);
    }

    // ---- Helpers ------------------------------------------------------------------------------------

    /// <summary>
    /// A real Prefab Variant, not a copy - instantiating the base and saving that instance is what makes
    /// Unity record it as a variant. Being a variant is the point: future art, animator or stat changes
    /// to PlayerKnight carry into PlayerKnightTutorial on their own, where a flat copy would silently
    /// drift from the prefab it was meant to mirror.
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

    private static T CopyAsset<T>(string sourcePath, string outPath, List<string> made) where T : Object
    {
        T existing = Load<T>(outPath);

        if (existing != null) { return existing; }

        if (Load<T>(sourcePath) == null)
        {
            Debug.LogError($"Tutorial content: no asset at {sourcePath} - skipped {outPath}.");
            return null;
        }

        if (!AssetDatabase.CopyAsset(sourcePath, outPath))
        {
            Debug.LogError($"Tutorial content: could not copy {sourcePath} to {outPath}.");
            return null;
        }

        made.Add(outPath);

        return Load<T>(outPath);
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
            Fireball, Teleport, SapTotem, Slash, Shield, Move, EnemyArrow,
            SummonCardSource, SummonEffectSource,
            KnightPrefab, MagePrefab, RangerPrefab, SkeletonPrefab,
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
