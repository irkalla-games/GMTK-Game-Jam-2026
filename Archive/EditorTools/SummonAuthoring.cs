using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot authoring for the new totems, the summoned skeleton ally, the wall tile effects, Shambles,
/// and the Shield Totem's move from Mage to Knight - everything the card design spreadsheet's importer
/// cannot build itself, because Summon/ApplyTileEffect/Swap are not among the kinds
/// CardSheetImporter.CreateEffect knows how to mint (see its `default` case).
///
/// A menu command rather than hand-authored .asset YAML, for the same reason every other *Authoring/
/// *Wiring script in this project is one - see StarterCharacterAuthoring's header. GUIDs and fileID
/// cross-references are Unity's business.
///
/// Idempotent: every asset is only created if nothing already exists at its path, and the Shield Totem
/// move only moves the file if it is still sitting at its old Mage path. Safe to re-run.
///
/// Run this BEFORE Tools/CardSheet/Import-CardSheet.ps1: the importer resolves an "Effect 1" cell to an
/// asset by name, scanning straight off disk, and only knows how to auto-create the "&lt;Kind&gt;
/// &lt;Amount&gt;" effects (Damage 5, Poison 3, ...). The Summon/Tile/Swap effect assets this script
/// creates have to already exist on disk before that scan runs, or the importer reports them as
/// unbuildable and skips the cards that need them.
///
/// Editor-only, not covered by Tools/compile-check.ps1 - see StarterCharacterAuthoring's header for why.
/// </summary>
public static class SummonAuthoring
{
    private const string TotemPrefabPath = "Assets/Prefabs/Totem/Totem.prefab";
    private const string WeakenTotemPrefabPath = "Assets/Prefabs/Totem/WeakenTotem.prefab";
    private const string VenomTotemPrefabPath = "Assets/Prefabs/Totem/VenomTotem.prefab";

    private const string SkeletonWarriorPrefabPath = "Assets/Prefabs/Enemies/SkeletonWarrior.prefab";
    private const string SkeletonAllyPrefabPath = "Assets/Prefabs/Allies/SkeletonAlly.prefab";

    private const string SummonEffectFolder = "Assets/Data/EffectData/Summon";
    private const string TileEffectFolder = "Assets/Data/EffectData/Tile";
    private const string PatternFolder = "Assets/Data/EffectData/Patterns";

    private const string EnemySlashPath = "Assets/Data/CardData/Enemy/EnemySlash.asset";
    private const string MoveInnatePath = "Assets/Data/CardData/Enemy/MoveInnate.asset";

    private const string OldSummonTotemPath = "Assets/Data/CardData/Mage/Summon/SummonTotem.asset";
    private const string NewSummonTotemPath = "Assets/Data/CardData/Knight/Summon/SummonTotem.asset";

    private const string MageStarterPath = "Assets/Data/DeckData/MageStarter.asset";
    private const string KnightStarterPath = "Assets/Data/DeckData/KnightStarter.asset";
    private const string PlayerMagePrefabPath = "Assets/Prefabs/Player/PlayerMage.prefab";
    private const string PlayerKnightPrefabPath = "Assets/Prefabs/Player/PlayerKnight.prefab";

    // Empty placeholder, blank name, no effects - live in the reward pool at Common today for no
    // reason. The Venom Totem is what it was reaching for.
    private const string SummonPoisonShieldPath = "Assets/Data/CardData/Rogue/Summon/SummonPoisonShield.asset";

    [MenuItem("Tools/Cards/Author Summons and Walls")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Summon authoring: exit Play Mode first - asset edits made in play are not reliable.");
            return;
        }

        EnsureFolder("Assets/Prefabs/Allies");
        EnsureFolder(SummonEffectFolder);
        EnsureFolder(TileEffectFolder);
        EnsureFolder(PatternFolder);
        EnsureFolder("Assets/Data/CardData/Knight/Summon");

        GameObject weakenTotem = EnsureTotemPrefab(
            WeakenTotemPrefabPath, "Weaken Totem", AuraAudience.Enemies, StatusType.Weaken, stacks: 2,
            color: new Color(0.47f, 0.18f, 0.55f, 1f));

        GameObject venomTotem = EnsureTotemPrefab(
            VenomTotemPrefabPath, "Venom Totem", AuraAudience.Everyone, StatusType.Poison, stacks: 3,
            color: new Color(0.22f, 0.55f, 0.16f, 1f));

        GameObject skeletonAlly = EnsureSkeletonAllyPrefab();

        EnsureSummonEffect("SummonWeakenTotem", weakenTotem, lifetimeTurns: 0);
        EnsureSummonEffect("SummonVenomTotem", venomTotem, lifetimeTurns: 0);
        EnsureSummonEffect("SummonSkeletonAlly", skeletonAlly, lifetimeTurns: 3);

        EnsureTileEffect("Wall of Force", TileEffectType.WallOfForce, turns: 2, magnitude: 0);
        EnsureTileEffect("Wall of Flames", TileEffectType.WallOfFlames, turns: 2, magnitude: 4);

        EnsureSwapEffect();
        EnsureWallPattern();

        MoveAndRetagShieldTotem();

        if (DeleteBrokenPlaceholder(SummonPoisonShieldPath)) { RescanCardLibraries(); }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Summon authoring: done - Weaken Totem, Venom Totem, Skeleton Ally, Wall of Force, "
                 + "Wall of Flames and Swap are ready, and Shield Totem now belongs to the Knight. "
                 + "Pick Tools > Sync With Sheet to get these into the design workbook - it runs the "
                 + "PowerShell side itself, so there is no separate script to run first.");
    }

    /// <summary>
    /// Duplicates the Shield Totem's prefab into a new totem projecting a single aura instead of a
    /// reaction - AuraData already supports any StatusType/stacks pair, so this is authoring, not code.
    /// </summary>
    private static GameObject EnsureTotemPrefab(
        string path, string displayName, AuraAudience affects, StatusType auraType, int stacks, Color color)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) { return existing; }

        if (!AssetDatabase.CopyAsset(TotemPrefabPath, path))
        {
            Debug.LogError($"Summon authoring: failed to duplicate {TotemPrefabPath} -> {path}.");
            return null;
        }

        using (PrefabUtility.EditPrefabContentsScope editScope = new(path))
        {
            GameObject root = editScope.prefabContentsRoot;
            root.name = Path.GetFileNameWithoutExtension(path);

            Character character = root.GetComponent<Character>();

            if (character == null)
            {
                Debug.LogError($"Summon authoring: {path} has no Character component after duplicating "
                               + $"{TotemPrefabPath} - something else is wrong.");
            }
            else
            {
                SerializedObject so = new(character);
                so.FindProperty("displayName").stringValue = displayName;
                so.ApplyModifiedProperties();
            }

            Totem totem = root.GetComponent<Totem>();

            if (totem == null)
            {
                Debug.LogError($"Summon authoring: {path} has no Totem component after duplicating "
                               + $"{TotemPrefabPath} - something else is wrong.");
            }
            else
            {
                SerializedObject so = new(totem);

                SerializedProperty range = so.FindProperty("range");
                range.FindPropertyRelative("shape").intValue = (int)RangeShape.Chebyshev;
                range.FindPropertyRelative("minDistance").intValue = 0;
                range.FindPropertyRelative("maxDistance").intValue = 2;

                so.FindProperty("affects").intValue = (int)affects;

                // One aura, no reactions - unlike Shield Totem, which projects a reaction and no aura.
                SerializedProperty auras = so.FindProperty("auras");
                auras.arraySize = 1;
                SerializedProperty aura = auras.GetArrayElementAtIndex(0);
                aura.FindPropertyRelative("type").intValue = (int)auraType;
                aura.FindPropertyRelative("stacks").intValue = stacks;

                so.FindProperty("reactions").arraySize = 0;
                so.FindProperty("auraColor").colorValue = color;

                so.ApplyModifiedProperties();
            }
        }

        Debug.Log($"Summon authoring: created {path}.");

        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    /// <summary>
    /// Duplicates the enemy Skeleton Warrior into a weaker, temporary ally version. PlayableCharacter.Ally
    /// is exactly the "friendly, but AI-controlled" affiliation the enum already documents for this case
    /// - BattleManager.LivingEnemies filters on IsPlayerControlled, not affiliation, so it already picks
    /// this up and gives it a turn during EnemyResolve with no brain changes.
    /// </summary>
    private static GameObject EnsureSkeletonAllyPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonAllyPrefabPath);
        if (existing != null) { return existing; }

        if (!AssetDatabase.CopyAsset(SkeletonWarriorPrefabPath, SkeletonAllyPrefabPath))
        {
            Debug.LogError($"Summon authoring: failed to duplicate {SkeletonWarriorPrefabPath} -> "
                           + $"{SkeletonAllyPrefabPath}.");
            return null;
        }

        using (PrefabUtility.EditPrefabContentsScope editScope = new(SkeletonAllyPrefabPath))
        {
            GameObject root = editScope.prefabContentsRoot;
            root.name = "SkeletonAlly";

            Character character = root.GetComponent<Character>();

            if (character == null)
            {
                Debug.LogError($"Summon authoring: {SkeletonAllyPrefabPath} has no Character component "
                               + $"after duplicating {SkeletonWarriorPrefabPath} - something else is wrong.");
            }
            else
            {
                SerializedObject so = new(character);

                so.FindProperty("maxHealth").intValue = 15;
                so.FindProperty("playableCharacter").intValue = (int)PlayableCharacter.Ally;
                so.FindProperty("displayName").stringValue = "Skeleton";
                // A friendly summon should not hand the player a reward for its own death.
                so.FindProperty("itemDropPrefab").objectReferenceValue = null;
                so.FindProperty("lootTable").objectReferenceValue = null;
                so.FindProperty("<Health>k__BackingField").intValue = 15;

                // Reuses the enemy Skeleton Warrior's own attack and move cards rather than authoring
                // new ones - EnemySlash (4 damage, melee) and MoveInnate (1 tile) are already exactly
                // what a plain skirmisher needs, just fewer copies for a 15 HP, 1-action-point body.
                CardData enemySlash = AssetDatabase.LoadAssetAtPath<CardData>(EnemySlashPath);
                CardData moveInnate = AssetDatabase.LoadAssetAtPath<CardData>(MoveInnatePath);

                List<CardData> cards = new();
                if (enemySlash != null) { cards.Add(enemySlash); cards.Add(enemySlash); }
                else { Debug.LogWarning($"Summon authoring: {EnemySlashPath} not found - skipped."); }
                if (moveInnate != null) { cards.Add(moveInnate); cards.Add(moveInnate); }
                else { Debug.LogWarning($"Summon authoring: {MoveInnatePath} not found - skipped."); }

                SerializedProperty deck = so.FindProperty("deck");
                deck.arraySize = cards.Count;
                for (int i = 0; i < cards.Count; i++)
                {
                    deck.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
                }

                so.ApplyModifiedProperties();
            }
        }

        Debug.Log($"Summon authoring: created {SkeletonAllyPrefabPath}.");

        return AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonAllyPrefabPath);
    }

    private static void EnsureSummonEffect(string name, GameObject summonedObject, int lifetimeTurns)
    {
        string path = $"{SummonEffectFolder}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<SummonEffect>(path) != null) { return; }

        if (summonedObject == null)
        {
            Debug.LogError($"Summon authoring: cannot create {path} - the prefab to summon is missing.");
            return;
        }

        SummonEffect effect = ScriptableObject.CreateInstance<SummonEffect>();
        AssetDatabase.CreateAsset(effect, path);

        SerializedObject so = new(effect);
        so.FindProperty("summonedObject").objectReferenceValue = summonedObject;
        so.FindProperty("lifetimeTurns").intValue = lifetimeTurns;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(effect);

        Debug.Log($"Summon authoring: created {path}.");
    }

    private static void EnsureTileEffect(string name, TileEffectType type, int turns, int magnitude)
    {
        string path = $"{TileEffectFolder}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<ApplyTileEffect>(path) != null) { return; }

        ApplyTileEffect effect = ScriptableObject.CreateInstance<ApplyTileEffect>();
        AssetDatabase.CreateAsset(effect, path);

        SerializedObject so = new(effect);
        so.FindProperty("effect").intValue = (int)type;
        so.FindProperty("turns").intValue = turns;
        so.FindProperty("magnitude").intValue = magnitude;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(effect);

        Debug.Log($"Summon authoring: created {path}.");
    }

    private static void EnsureSwapEffect()
    {
        // Loose at the EffectData root rather than in a "Move" subfolder, matching how the existing
        // MoveEffect asset (Assets/Data/EffectData/Move.asset) is authored - there is no established
        // subfolder for movement effects to join.
        const string path = "Assets/Data/EffectData/Swap.asset";
        if (AssetDatabase.LoadAssetAtPath<SwapEffect>(path) != null) { return; }

        SwapEffect effect = ScriptableObject.CreateInstance<SwapEffect>();
        AssetDatabase.CreateAsset(effect, path);
        EditorUtility.SetDirty(effect);

        Debug.Log($"Summon authoring: created {path}.");
    }

    /// <summary>
    /// A 3-wide, 1-tall bar painted facing Up, anchored at its centre cell - the shape both wall cards
    /// use. Rotated to face wherever the card is aimed (see EffectPattern.Covers), a bar painted facing
    /// Up becomes a line across the caster's approach in every direction, not just north.
    /// </summary>
    private static void EnsureWallPattern()
    {
        string path = $"{PatternFolder}/Wall3.asset";
        if (AssetDatabase.LoadAssetAtPath<EffectPattern>(path) != null) { return; }

        EffectPattern pattern = ScriptableObject.CreateInstance<EffectPattern>();
        AssetDatabase.CreateAsset(pattern, path);

        // Public paint API, not SerializedObject - see EffectPattern.Resize/SetCardinalCell's own doc
        // comments: this is the intended Editor-only authoring surface, exactly like the Inspector's
        // grid painter uses.
        pattern.Resize(3, 1);
        pattern.SetAnchor(new Vector2Int(1, 0));
        pattern.SetCardinalCell(0, 0, true);
        pattern.SetCardinalCell(1, 0, true);
        pattern.SetCardinalCell(2, 0, true);

        EditorUtility.SetDirty(pattern);

        Debug.Log($"Summon authoring: created {path}.");
    }

    /// <summary>
    /// Moves the Shield Totem card from the Mage to the Knight and shortens its cast range from
    /// Chebyshev 1-3 to 1-1 - the aura's own reach (Chebyshev 0-2) is untouched. AssetDatabase.MoveAsset
    /// keeps the card's GUID, so AllCards.asset and every other reference survive the move unbroken.
    /// </summary>
    private static void MoveAndRetagShieldTotem()
    {
        CardData card = AssetDatabase.LoadAssetAtPath<CardData>(OldSummonTotemPath)
                         ?? AssetDatabase.LoadAssetAtPath<CardData>(NewSummonTotemPath);

        if (card == null)
        {
            Debug.LogError("Summon authoring: Summon Totem card not found at the Mage or Knight path - "
                           + "not retagged.");
            return;
        }

        if (AssetDatabase.GetAssetPath(card) == OldSummonTotemPath)
        {
            string moveError = AssetDatabase.MoveAsset(OldSummonTotemPath, NewSummonTotemPath);

            if (!string.IsNullOrEmpty(moveError))
            {
                Debug.LogError($"Summon authoring: failed to move Summon Totem - {moveError}");
                return;
            }
        }

        SerializedObject so = new(card);
        so.FindProperty("<requiredClass>k__BackingField").intValue = (int)CharacterClass.Knight;
        so.FindProperty("<range>k__BackingField").FindPropertyRelative("maxDistance").intValue = 1;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(card);

        RemoveFromDeck(MageStarterPath, card);
        AddToDeck(KnightStarterPath, card, count: 1);

        RemoveFromInlineDeck(PlayerMagePrefabPath, card);
        AddToInlineDeck(PlayerKnightPrefabPath, card, count: 1);

        Debug.Log("Summon authoring: Summon Totem moved to Knight (cast range Chebyshev 1-1), removed "
                 + "from the Mage's starter deck and added to the Knight's.");
    }

    private static void RemoveFromDeck(string deckPath, CardData card)
    {
        DeckData deck = AssetDatabase.LoadAssetAtPath<DeckData>(deckPath);
        if (deck == null) { return; }

        SerializedObject so = new(deck);
        SerializedProperty cards = so.FindProperty("cards");

        for (int i = cards.arraySize - 1; i >= 0; i--)
        {
            if (cards.GetArrayElementAtIndex(i).objectReferenceValue == card)
            {
                cards.DeleteArrayElementAtIndex(i);
            }
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(deck);
    }

    private static void AddToDeck(string deckPath, CardData card, int count)
    {
        DeckData deck = AssetDatabase.LoadAssetAtPath<DeckData>(deckPath);
        if (deck == null) { return; }

        SerializedObject so = new(deck);
        SerializedProperty cards = so.FindProperty("cards");

        int already = CountMatches(cards, card);

        for (int i = already; i < count; i++)
        {
            cards.InsertArrayElementAtIndex(cards.arraySize);
            cards.GetArrayElementAtIndex(cards.arraySize - 1).objectReferenceValue = card;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(deck);
    }

    private static void RemoveFromInlineDeck(string prefabPath, CardData card)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null) { return; }

        using PrefabUtility.EditPrefabContentsScope editScope = new(prefabPath);

        Character character = editScope.prefabContentsRoot.GetComponent<Character>();
        if (character == null) { return; }

        SerializedObject so = new(character);
        SerializedProperty deck = so.FindProperty("deck");

        for (int i = deck.arraySize - 1; i >= 0; i--)
        {
            if (deck.GetArrayElementAtIndex(i).objectReferenceValue == card)
            {
                deck.DeleteArrayElementAtIndex(i);
            }
        }

        so.ApplyModifiedProperties();
    }

    private static void AddToInlineDeck(string prefabPath, CardData card, int count)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null) { return; }

        using PrefabUtility.EditPrefabContentsScope editScope = new(prefabPath);

        Character character = editScope.prefabContentsRoot.GetComponent<Character>();
        if (character == null) { return; }

        SerializedObject so = new(character);
        SerializedProperty deck = so.FindProperty("deck");

        int already = CountMatches(deck, card);

        for (int i = already; i < count; i++)
        {
            deck.InsertArrayElementAtIndex(deck.arraySize);
            deck.GetArrayElementAtIndex(deck.arraySize - 1).objectReferenceValue = card;
        }

        so.ApplyModifiedProperties();
    }

    private static int CountMatches(SerializedProperty arrayProp, CardData card)
    {
        int count = 0;
        for (int i = 0; i < arrayProp.arraySize; i++)
        {
            if (arrayProp.GetArrayElementAtIndex(i).objectReferenceValue == card) { count++; }
        }
        return count;
    }

    /// <summary>
    /// Deletes an empty placeholder CardData asset, if it is still there. Returns true if anything was
    /// actually removed, which is the caller's cue to rescan every CardLibrary - a delete raises no
    /// import event, so CardLibraryEditor's AssetPostprocessor never sees it (see that file's header).
    /// </summary>
    private static bool DeleteBrokenPlaceholder(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<CardData>(path) == null) { return false; }

        if (!AssetDatabase.DeleteAsset(path))
        {
            Debug.LogWarning($"Summon authoring: failed to delete {path}.");
            return false;
        }

        Debug.Log($"Summon authoring: deleted empty placeholder card {path}.");
        return true;
    }

    /// Same scan CardLibraryEditor's own (private) Rescan runs - duplicated rather than reflected into,
    /// since it is ten lines and reflection into an Editor-only private method is worse than repeating
    /// it once here.
    private static void RescanCardLibraries()
    {
        List<CardData> found = new();

        foreach (string guid in AssetDatabase.FindAssets("t:CardData", new[] { "Assets/Data/CardData" }))
        {
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(guid));
            if (card != null) { found.Add(card); }
        }

        foreach (string guid in AssetDatabase.FindAssets("t:CardLibrary"))
        {
            CardLibrary library = AssetDatabase.LoadAssetAtPath<CardLibrary>(AssetDatabase.GUIDToAssetPath(guid));

            if (library != null)
            {
                library.SetCards(found);
                EditorUtility.SetDirty(library);
            }
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) { return; }

        string[] parts = path.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";

            if (!AssetDatabase.IsValidFolder(next)) { AssetDatabase.CreateFolder(current, parts[i]); }

            current = next;
        }
    }
}
