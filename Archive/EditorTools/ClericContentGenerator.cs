using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot authoring for the Cleric: the player prefab, its starter deck, the Heal/Beacon/Sentinel
/// totems and every new Cleric card, moving six ally-buff totems and four Mage cards over to it, and
/// rebalancing the two decks that lose cards. Follows StarterCharacterAuthoring's character-authoring
/// conventions and TotemContentGenerator's card-batch conventions - this script leans on both rather
/// than reinventing either.
///
/// Idempotent for everything it creates fresh (checks before creating, same as every other generator
/// in this project). The two rebalanced decks (KnightStarter, MageStarter) and the moved cards'
/// requiredClass are *repaired*, not skipped, on every run - see CLAUDE.md's "a content generator
/// should repair, not skip" rule - so re-running this after hand-tuning a count elsewhere still
/// converges the class-ownership pieces to what this script says they are.
///
/// Editor-only, and not covered by Tools/compile-check.ps1 without -IncludeEditor.
/// </summary>
public static class ClericContentGenerator
{
    private const string CharactersFolder = "Assets/Data/Characters";
    private const string DecksFolder = "Assets/Data/DeckData";
    private const string CardRoot = "Assets/Data/CardData";
    private const string EffectRoot = "Assets/Data/EffectData";
    private const string GlossaryPath = "Assets/Scripts/UI/Tooltips/Glossary.asset";

    private const string PlayerMagePrefabPath = "Assets/Prefabs/Player/PlayerMage.prefab";
    private const string ClericPrefabPath = "Assets/Prefabs/Player/PlayerCleric.prefab";
    private const string ClericPortraitPath = "Assets/Extra Assets/Dark UI/New Icons/White Bishop.png";

    private const string DefaultRosterPath = "Assets/Data/Characters/DefaultRoster.asset";
    private const string KnightStarterPath = "Assets/Data/DeckData/KnightStarter.asset";
    private const string MageStarterPath = "Assets/Data/DeckData/MageStarter.asset";

    // Existing cards moving Knight -> Cleric (the six ally-buff totems; the Shield Totem/SummonTotem
    // stays with the Knight - it was never in this set).
    private static readonly string[] KnightTotemsMovingToCleric =
    {
        "Assets/Data/CardData/Knight/Summon/Warcry Totem.asset",
        "Assets/Data/CardData/Knight/Summon/Bulwark Totem.asset",
        "Assets/Data/CardData/Knight/Summon/Warlord Totem.asset",
        "Assets/Data/CardData/Knight/Summon/Aegis Totem.asset",
        "Assets/Data/CardData/Knight/Summon/Bastion Totem.asset",
        "Assets/Data/CardData/Knight/Summon/Rampart Totem.asset",
    };

    // Existing cards moving Mage -> Cleric.
    private const string SummonSkeletonOldPath = "Assets/Data/CardData/Mage/Summon/SummonSkeleton.asset";
    private const string CureWoundsOldPath = "Assets/Data/CardData/Mage/Heal/Cure Wounds.asset";
    private const string HealWordOldPath = "Assets/Data/CardData/Mage/Heal/Heal Word.asset";
    private const string BladeWardOldPath = "Assets/Data/CardData/Mage/Buff (Defensive)/Blade Ward.asset";

    // Cards staying on their own class, reused to rebuild the two rebalanced starter decks.
    private const string MovePath = "Assets/Data/CardData/Generic/Move.asset";
    private const string SlashPath = "Assets/Data/CardData/Knight/Melee Attack/Slash.asset";
    private const string ShieldPath = "Assets/Data/CardData/Knight/Buff (Defensive)/Shield.asset";
    private const string BlockPath = "Assets/Data/CardData/Knight/Buff (Defensive)/Block.asset";
    private const string TeleportPath = "Assets/Data/CardData/Mage/Movement/Teleport.asset";
    private const string FireboltPath = "Assets/Data/CardData/Mage/RangedAttack/Firebolt.asset";
    private const string RustArmorPath = "Assets/Data/CardData/Mage/Debuff/Rust Armor.asset";
    private const string DullBladePath = "Assets/Data/CardData/Mage/Debuff/Dull Blade.asset";
    private const string HexTotemPath = "Assets/Data/CardData/Mage/Summon/Hex Totem.asset";
    private const string FractureTotemPath = "Assets/Data/CardData/Mage/Summon/Fracture Totem.asset";
    private const string SapTotemPath = "Assets/Data/CardData/Mage/Summon/Sap Totem.asset";

    [MenuItem("Tools/Cards/Author Cleric")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Cleric authoring: exit Play Mode first - asset edits made in play are not reliable.");
            return;
        }

        EnsureFolder(CharactersFolder);
        EnsureFolder(DecksFolder);

        MoveAndRetagToCleric(KnightTotemsMovingToCleric);
        MoveAndRetagToCleric(new[] { SummonSkeletonOldPath, CureWoundsOldPath, HealWordOldPath, BladeWardOldPath });

        Dictionary<string, GameObject> totemPrefabs = EnsureNewTotemPrefabs();
        Dictionary<string, CardEffect> effects = BuildEffects(totemPrefabs);

        foreach (CardSpec spec in BuildCardSpecs()) { CreateCard(spec, effects); }

        DeckData clericStarter = EnsureClericStarterDeck();
        GameObject clericPrefab = EnsureClericPrefab();
        CharacterOption clericOption = EnsureCharacterOption(clericPrefab, clericStarter);

        AppendToRoster(clericOption);

        RebalanceDeck(KnightStarterPath, new (string path, int count)[]
        {
            (MovePath, 4), (SlashPath, 5), (ShieldPath, 4), (BlockPath, 2),
        });

        RebalanceDeck(MageStarterPath, new (string path, int count)[]
        {
            (TeleportPath, 3), (FireboltPath, 3), (DullBladePath, 3), (RustArmorPath, 3),
            (HexTotemPath, 1), (FractureTotemPath, 1), (SapTotemPath, 1),
        });

        AppendGlossaryRows();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Cleric authoring: done - prefab, starter deck, 11 new cards, 6 ally totems and 4 "
                 + "Mage cards moved over, KnightStarter/MageStarter rebalanced.");
    }

    // ------------------------------------------------------------------------------------------
    // Moving and retagging existing cards
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Moves each card at `oldPaths` into the matching Cleric/ subfolder (same Category/Name.asset
    /// tail its old path had) and sets requiredClass to Cleric outright - a reassignment, not a widen:
    /// unlike StarterCharacterAuthoring.RetagForRogue (which ORs Rogue onto a Knight card two classes
    /// share), these cards are leaving their old class entirely. AssetDatabase.MoveAsset keeps the
    /// GUID, so every deck and Ideas-tab reference to the card survives the move untouched.
    /// </summary>
    private static void MoveAndRetagToCleric(IReadOnlyList<string> oldPaths)
    {
        foreach (string oldPath in oldPaths)
        {
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(oldPath);

            if (card == null)
            {
                Debug.LogWarning($"Cleric authoring: {oldPath} not found - not moved.");
                continue;
            }

            string tail = oldPath[(oldPath.IndexOf('/', CardRoot.Length + 1) + 1)..];
            string newPath = $"{CardRoot}/Cleric/{tail}";

            string currentPath = AssetDatabase.GetAssetPath(card);

            if (currentPath != newPath)
            {
                EnsureFolder(Path.GetDirectoryName(newPath).Replace('\\', '/'));

                string error = AssetDatabase.MoveAsset(currentPath, newPath);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogError($"Cleric authoring: failed to move {currentPath} -> {newPath}: {error}");
                    continue;
                }
            }

            SerializedObject so = new(card);
            SerializedProperty requiredClassProp = so.FindProperty("<requiredClass>k__BackingField");

            if ((CharacterClass)requiredClassProp.intValue != CharacterClass.Cleric)
            {
                requiredClassProp.intValue = (int)CharacterClass.Cleric;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(card);
            }
        }
    }

    // ------------------------------------------------------------------------------------------
    // New totem prefabs - Heal Totem (plain Regeneration aura) and the two Taunt totems
    // ------------------------------------------------------------------------------------------

    private static Dictionary<string, GameObject> EnsureNewTotemPrefabs()
    {
        Dictionary<string, GameObject> prefabs = new();

        // Gem 2 turquoise: a maintained boon, on the Heal/Regeneration colour. Aura wash and gem colour
        // both come from gemColour now, so there is no auraColor to keep in sync by hand.
        prefabs["HealTotem"] = TotemContentGenerator.CreateTotemPrefab(new TotemContentGenerator.TotemSpec
        {
            fileName = "HealTotem",
            displayName = "Heal Totem",
            affects = AuraAudience.Allies,
            requiredClass = CharacterClass.Cleric,
            gem = 2,
            gemColour = "TURQUOISE",
            auras = { TotemContentGenerator.Plain(StatusType.Regeneration, 5) },
        });

        // stacks is left at AuraSpec's default (1): TauntStatus's counter is remaining turns, but a
        // projected aura is rebuilt fresh every query and neither ForcedQuarry nor Character.ActiveStatuses
        // consults IsExpired for an aura entry, so the value here is inert - the totem taunts for as
        // long as anything stands in its range, permanently, the same as every other plain aura here.
        //
        // Gem 9 gold: the taunt shape, on the utility colour.
        prefabs["BeaconTotem"] = TotemContentGenerator.CreateTotemPrefab(new TotemContentGenerator.TotemSpec
        {
            fileName = "BeaconTotem",
            displayName = "Beacon Totem",
            affects = AuraAudience.Enemies,
            requiredClass = CharacterClass.Cleric,
            maxHealth = 5,
            gem = 9,
            gemColour = "GOLD",
            auras = { TotemContentGenerator.Plain(StatusType.Taunt, 1) },
        });

        prefabs["SentinelTotem"] = TotemContentGenerator.CreateTotemPrefab(new TotemContentGenerator.TotemSpec
        {
            fileName = "SentinelTotem",
            displayName = "Sentinel Totem",
            affects = AuraAudience.Enemies,
            requiredClass = CharacterClass.Cleric,
            maxHealth = 12,
            gem = 9,
            gemColour = "GOLD",
            auras = { TotemContentGenerator.Plain(StatusType.Taunt, 1) },
        });

        return prefabs;
    }

    // ------------------------------------------------------------------------------------------
    // Effect assets
    // ------------------------------------------------------------------------------------------

    private static Dictionary<string, CardEffect> BuildEffects(Dictionary<string, GameObject> totemPrefabs)
    {
        Dictionary<string, CardEffect> effects = new();

        // Already authored by hand - reused rather than minting duplicates, same rule
        // TotemContentGenerator.BuildEffects follows.
        AddExisting(effects, "Damage 4", $"{EffectRoot}/Damage/Damage 4.asset");
        AddExisting(effects, "Heal 3", $"{EffectRoot}/Heal/Heal 3.asset");
        AddExisting(effects, "Heal 5", $"{EffectRoot}/Heal/Heal 5.asset");
        AddExisting(effects, "Draw 1", $"{EffectRoot}/Draw/Draw 1.asset");
        AddExisting(effects, "Strength 3", $"{EffectRoot}/Status/Strength 3.asset");
        AddExisting(effects, "Block 2", $"{EffectRoot}/Block/Block 2.asset");

        effects["Regeneration 4"] = CreateStatusEffect(
            $"{EffectRoot}/Status/Regeneration 4.asset", StatusType.Regeneration, 4, alliesOnly: true);
        effects["Lifesteal 1"] = CreateStatusEffect(
            $"{EffectRoot}/Status/Lifesteal 1.asset", StatusType.Lifesteal, 1, alliesOnly: true);

        effects["Purge Poison"] = CreateCleansePoisonEffect(
            $"{EffectRoot}/Status/Purge Poison.asset", giveToNearestEnemy: true);
        effects["Cleanse Poison Heal"] = CreateCleansePoisonEffect(
            $"{EffectRoot}/Status/Cleanse Poison Heal.asset", giveToNearestEnemy: false);

        effects["SummonHealTotem"] = CreateSummonEffect(
            $"{EffectRoot}/Summon/SummonHealTotem.asset", totemPrefabs["HealTotem"]);
        effects["SummonBeaconTotem"] = CreateSummonEffect(
            $"{EffectRoot}/Summon/SummonBeaconTotem.asset", totemPrefabs["BeaconTotem"]);
        effects["SummonSentinelTotem"] = CreateSummonEffect(
            $"{EffectRoot}/Summon/SummonSentinelTotem.asset", totemPrefabs["SentinelTotem"]);

        return effects;
    }

    private static void AddExisting(Dictionary<string, CardEffect> effects, string key, string path)
    {
        CardEffect effect = AssetDatabase.LoadAssetAtPath<CardEffect>(path);
        if (effect == null)
        {
            Debug.LogError($"Cleric authoring: expected an existing effect at {path} - none found.");
            return;
        }

        effects[key] = effect;
    }

    private static CardEffect CreateStatusEffect(string path, StatusType status, int stacks, bool alliesOnly)
    {
        ApplyStatusEffect effect = LoadOrCreate<ApplyStatusEffect>(path);
        if (effect == null) { return AssetDatabase.LoadAssetAtPath<ApplyStatusEffect>(path); }

        SerializedObject so = new(effect);
        so.FindProperty("status").intValue = (int)status;
        so.FindProperty("stacks").intValue = stacks;
        so.FindProperty("alliesOnly").boolValue = alliesOnly;
        so.ApplyModifiedProperties();
        return effect;
    }

    private static CardEffect CreateCleansePoisonEffect(string path, bool giveToNearestEnemy)
    {
        CleansePoisonEffect effect = LoadOrCreate<CleansePoisonEffect>(path);
        if (effect == null) { return AssetDatabase.LoadAssetAtPath<CleansePoisonEffect>(path); }

        SerializedObject so = new(effect);
        so.FindProperty("giveToNearestEnemy").boolValue = giveToNearestEnemy;
        so.ApplyModifiedProperties();
        return effect;
    }

    private static CardEffect CreateSummonEffect(string path, GameObject totemPrefab)
    {
        SummonEffect effect = LoadOrCreate<SummonEffect>(path);
        if (effect == null) { return AssetDatabase.LoadAssetAtPath<SummonEffect>(path); }

        SerializedObject so = new(effect);
        so.FindProperty("summonedObject").objectReferenceValue = totemPrefab;
        so.ApplyModifiedProperties();
        return effect;
    }

    /// Returns null when an asset already exists at `path`, so callers can tell "just created, fields
    /// need setting" from "already there, leave it alone" - same idempotency rule
    /// TotemContentGenerator.LoadOrCreate follows.
    private static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) { return null; }

        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

        T asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    // ------------------------------------------------------------------------------------------
    // Cards
    // ------------------------------------------------------------------------------------------

    private class EntrySpec
    {
        public string effectKey;
        public EffectTarget aimsAt;

        public EntrySpec(string effectKey, EffectTarget aimsAt)
        {
            this.effectKey = effectKey;
            this.aimsAt = aimsAt;
        }
    }

    private class CardSpec
    {
        public string folder;
        public string cardName;
        public int cost;
        public RangeShape rangeShape;
        public int rangeMin;
        public int rangeMax;
        public Rarity rarity;
        public string description;
        public List<EntrySpec> entries;
    }

    private static List<CardSpec> BuildCardSpecs()
    {
        return new List<CardSpec>
        {
            new()
            {
                folder = "Summon", cardName = "Heal Totem", cost = 2,
                rangeShape = RangeShape.Chebyshev, rangeMin = 1, rangeMax = 3, rarity = Rarity.Uncommon,
                description = "Summon a Heal Totem.",
                entries = new List<EntrySpec> { new("SummonHealTotem", EffectTarget.PlayedTile) },
            },
            new()
            {
                folder = "Summon", cardName = "Beacon Totem", cost = 1,
                rangeShape = RangeShape.Chebyshev, rangeMin = 1, rangeMax = 2, rarity = Rarity.Uncommon,
                description = "Summon a Beacon Totem.",
                entries = new List<EntrySpec> { new("SummonBeaconTotem", EffectTarget.PlayedTile) },
            },
            new()
            {
                folder = "Summon", cardName = "Sentinel Totem", cost = 3,
                rangeShape = RangeShape.Chebyshev, rangeMin = 1, rangeMax = 3, rarity = Rarity.Uncommon,
                description = "Summon a Sentinel Totem.",
                entries = new List<EntrySpec> { new("SummonSentinelTotem", EffectTarget.PlayedTile) },
            },
            new()
            {
                folder = "Debuff", cardName = "Purge", cost = 1,
                rangeShape = RangeShape.Chebyshev, rangeMin = 1, rangeMax = 4, rarity = Rarity.Common,
                description = "Remove all Poison from an ally and afflict the nearest enemy with it.",
                entries = new List<EntrySpec> { new("Purge Poison", EffectTarget.PlayedTile) },
            },
            new()
            {
                folder = "Heal", cardName = "Cleansing Light", cost = 1,
                rangeShape = RangeShape.Chebyshev, rangeMin = 1, rangeMax = 4, rarity = Rarity.Common,
                description = "Remove all Poison from an ally and heal them for the stacks removed.",
                entries = new List<EntrySpec> { new("Cleanse Poison Heal", EffectTarget.PlayedTile) },
            },
            new()
            {
                folder = "Heal", cardName = "Smite", cost = 1,
                rangeShape = RangeShape.Chebyshev, rangeMin = 1, rangeMax = 4, rarity = Rarity.Common,
                description = "Deal 4 damage. Heal 3.",
                entries = new List<EntrySpec>
                {
                    new("Damage 4", EffectTarget.PlayedTile), new("Heal 3", EffectTarget.Source),
                },
            },
            new()
            {
                folder = "Heal", cardName = "Mend and Fortify", cost = 2,
                rangeShape = RangeShape.Chebyshev, rangeMin = 1, rangeMax = 4, rarity = Rarity.Uncommon,
                description = "Heal 5 and gain 3 Strength.",
                entries = new List<EntrySpec>
                {
                    new("Heal 5", EffectTarget.PlayedTile), new("Strength 3", EffectTarget.PlayedTile),
                },
            },
            new()
            {
                folder = "Heal", cardName = "Mend and Guard", cost = 2,
                rangeShape = RangeShape.Chebyshev, rangeMin = 1, rangeMax = 4, rarity = Rarity.Uncommon,
                description = "Heal 5 and gain Block 2.",
                entries = new List<EntrySpec>
                {
                    new("Heal 5", EffectTarget.PlayedTile), new("Block 2", EffectTarget.PlayedTile),
                },
            },
            new()
            {
                folder = "Heal", cardName = "Renew", cost = 1,
                rangeShape = RangeShape.Chebyshev, rangeMin = 1, rangeMax = 4, rarity = Rarity.Common,
                description = "Heal 3 and draw a card.",
                entries = new List<EntrySpec>
                {
                    new("Heal 3", EffectTarget.PlayedTile), new("Draw 1", EffectTarget.PlayedTile),
                },
            },
            new()
            {
                folder = "Heal", cardName = "Renewing Touch", cost = 2,
                rangeShape = RangeShape.Chebyshev, rangeMin = 1, rangeMax = 4, rarity = Rarity.Uncommon,
                description = "Apply Regeneration 4.",
                entries = new List<EntrySpec> { new("Regeneration 4", EffectTarget.PlayedTile) },
            },
            new()
            {
                folder = "Buff (Offensive)", cardName = "Vengeful Vigor", cost = 1,
                rangeShape = RangeShape.Chebyshev, rangeMin = 0, rangeMax = 4, rarity = Rarity.Uncommon,
                description = "Apply Lifesteal 1.",
                entries = new List<EntrySpec> { new("Lifesteal 1", EffectTarget.PlayedTile) },
            },
        };
    }

    private static void CreateCard(CardSpec spec, Dictionary<string, CardEffect> effects)
    {
        string path = $"{CardRoot}/Cleric/{spec.folder}/{spec.cardName}.asset";

        if (AssetDatabase.LoadAssetAtPath<CardData>(path) != null) { return; }

        List<CardEffect> resolved = new();
        foreach (EntrySpec entry in spec.entries)
        {
            if (!effects.TryGetValue(entry.effectKey, out CardEffect effect) || effect == null)
            {
                Debug.LogError($"Cleric authoring: '{spec.cardName}' references effect "
                               + $"'{entry.effectKey}', which was not built. Skipping this card.");
                return;
            }
            resolved.Add(effect);
        }

        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

        CardData card = ScriptableObject.CreateInstance<CardData>();
        AssetDatabase.CreateAsset(card, path);

        SerializedObject so = new(card);
        so.FindProperty("<cardName>k__BackingField").stringValue = spec.cardName;
        so.FindProperty("<cost>k__BackingField").intValue = spec.cost;
        so.FindProperty("<requiredClass>k__BackingField").intValue = (int)CharacterClass.Cleric;
        so.FindProperty("<rarity>k__BackingField").intValue = (int)spec.rarity;
        so.FindProperty("<description>k__BackingField").stringValue = spec.description;

        SerializedProperty range = so.FindProperty("<range>k__BackingField");
        range.FindPropertyRelative("shape").intValue = (int)spec.rangeShape;
        range.FindPropertyRelative("minDistance").intValue = spec.rangeMin;
        range.FindPropertyRelative("maxDistance").intValue = spec.rangeMax;

        SerializedProperty entries = so.FindProperty("<effectEntries>k__BackingField");
        entries.arraySize = spec.entries.Count;
        for (int i = 0; i < spec.entries.Count; i++)
        {
            EntrySpec source = spec.entries[i];
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);

            entry.FindPropertyRelative("effect").objectReferenceValue = resolved[i];
            entry.FindPropertyRelative("aimsAt").intValue = (int)source.aimsAt;

            SerializedProperty area = entry.FindPropertyRelative("area");
            area.FindPropertyRelative("kind").intValue = (int)AreaKind.Single;

            SerializedProperty radius = area.FindPropertyRelative("radius");
            radius.FindPropertyRelative("shape").intValue = 0;
            radius.FindPropertyRelative("minDistance").intValue = 0;
            radius.FindPropertyRelative("maxDistance").intValue = 0;

            area.FindPropertyRelative("pattern").objectReferenceValue = null;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(card);
    }

    // ------------------------------------------------------------------------------------------
    // Prefab, deck, character option, roster
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Duplicates PlayerMage.prefab - the same CopyAsset technique
    /// StarterCharacterAuthoring.EnsureRoguePrefab used for the Rogue, so the Cleric borrows the
    /// Mage's rig and Animator as a placeholder until it has its own art. Mage over Knight because
    /// the Cleric is Backline like the Mage, not Frontline like the Knight.
    /// </summary>
    private static GameObject EnsureClericPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ClericPrefabPath) == null)
        {
            if (!AssetDatabase.CopyAsset(PlayerMagePrefabPath, ClericPrefabPath))
            {
                Debug.LogError($"Cleric authoring: failed to duplicate {PlayerMagePrefabPath} -> {ClericPrefabPath}.");
                return AssetDatabase.LoadAssetAtPath<GameObject>(PlayerMagePrefabPath);
            }
        }

        Sprite portrait = AssetDatabase.LoadAssetAtPath<Sprite>(ClericPortraitPath);

        using (PrefabUtility.EditPrefabContentsScope editScope = new(ClericPrefabPath))
        {
            GameObject root = editScope.prefabContentsRoot;
            root.name = "PlayerCleric";

            Character character = root.GetComponent<Character>();
            if (character == null)
            {
                Debug.LogError($"Cleric authoring: {ClericPrefabPath} has no Character component after "
                               + $"duplicating {PlayerMagePrefabPath}.");
            }
            else
            {
                SerializedObject so = new(character);
                so.FindProperty("characterClass").intValue = (int)CharacterClass.Cleric;
                so.FindProperty("displayName").stringValue = "Cleric";
                so.FindProperty("maxHealth").intValue = 20;
                so.FindProperty("battleRole").intValue = (int)BattleRole.Backline;
                if (portrait != null) { so.FindProperty("portrait").objectReferenceValue = portrait; }
                so.FindProperty("deck").arraySize = 0;
                so.ApplyModifiedProperties();
            }
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(ClericPrefabPath);
    }

    private static readonly (string path, int count)[] ClericDeck =
    {
        (MovePath, 3),
        ($"{CardRoot}/Cleric/Heal/Smite.asset", 3),
        ($"{CardRoot}/Cleric/Heal/Cleansing Light.asset", 2),
        ($"{CardRoot}/Cleric/Heal/Mend and Guard.asset", 2),
        ($"{CardRoot}/Cleric/Summon/Heal Totem.asset", 1),
        ($"{CardRoot}/Cleric/Debuff/Purge.asset", 1),
        ($"{CardRoot}/Cleric/Heal/Renew.asset", 2),
        ($"{CardRoot}/Cleric/Heal/Renewing Touch.asset", 1),
        ($"{CardRoot}/Cleric/Buff (Offensive)/Vengeful Vigor.asset", 1),
    };

    private static DeckData EnsureClericStarterDeck()
    {
        string path = $"{DecksFolder}/ClericStarter.asset";
        DeckData existing = AssetDatabase.LoadAssetAtPath<DeckData>(path);

        if (existing != null) { return existing; }

        DeckData deck = ScriptableObject.CreateInstance<DeckData>();

        SerializedObject so = new(deck);
        so.FindProperty("displayName").stringValue = "Cleric Starter";
        so.FindProperty("forClass").intValue = (int)CharacterClass.Cleric;

        List<CardData> cards = BuildCardList(ClericDeck);
        SerializedProperty cardsProp = so.FindProperty("cards");
        cardsProp.arraySize = cards.Count;
        for (int i = 0; i < cards.Count; i++) { cardsProp.GetArrayElementAtIndex(i).objectReferenceValue = cards[i]; }

        so.ApplyModifiedProperties();

        AssetDatabase.CreateAsset(deck, path);
        return deck;
    }

    private static CharacterOption EnsureCharacterOption(GameObject prefab, DeckData starterDeck)
    {
        string path = $"{CharactersFolder}/Cleric.asset";
        CharacterOption existing = AssetDatabase.LoadAssetAtPath<CharacterOption>(path);

        if (existing != null) { return existing; }

        if (prefab == null)
        {
            Debug.LogWarning("Cleric authoring: no Cleric prefab - CharacterOption not created.");
            return null;
        }

        CharacterOption option = ScriptableObject.CreateInstance<CharacterOption>();

        SerializedObject so = new(option);
        so.FindProperty("displayName").stringValue = "Cleric";
        so.FindProperty("prefab").objectReferenceValue = prefab;

        if (starterDeck != null)
        {
            SerializedProperty decks = so.FindProperty("decks");
            decks.arraySize = 1;
            decks.GetArrayElementAtIndex(0).objectReferenceValue = starterDeck;
        }

        so.ApplyModifiedProperties();

        AssetDatabase.CreateAsset(option, path);
        return option;
    }

    private static void AppendToRoster(CharacterOption clericOption)
    {
        if (clericOption == null) { return; }

        CharacterRoster roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(DefaultRosterPath);
        if (roster == null)
        {
            Debug.LogWarning($"Cleric authoring: {DefaultRosterPath} not found - Cleric not added to the roster.");
            return;
        }

        SerializedObject so = new(roster);
        SerializedProperty list = so.FindProperty("characters");

        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == clericOption) { return; }
        }

        list.arraySize++;
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = clericOption;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(roster);
    }

    /// <summary>
    /// Rewrites `deckPath`'s card list wholesale from `entries` - repair, not skip, per CLAUDE.md: a
    /// re-run always converges KnightStarter/MageStarter to exactly these counts rather than only
    /// fixing them the first time.
    /// </summary>
    private static void RebalanceDeck(string deckPath, (string path, int count)[] entries)
    {
        DeckData deck = AssetDatabase.LoadAssetAtPath<DeckData>(deckPath);
        if (deck == null)
        {
            Debug.LogWarning($"Cleric authoring: {deckPath} not found - not rebalanced.");
            return;
        }

        List<CardData> cards = BuildCardList(entries);

        SerializedObject so = new(deck);
        SerializedProperty cardsProp = so.FindProperty("cards");
        cardsProp.arraySize = cards.Count;
        for (int i = 0; i < cards.Count; i++) { cardsProp.GetArrayElementAtIndex(i).objectReferenceValue = cards[i]; }
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(deck);
    }

    private static List<CardData> BuildCardList((string path, int count)[] entries)
    {
        List<CardData> cards = new();

        foreach ((string path, int count) in entries)
        {
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(path);

            if (card == null)
            {
                Debug.LogWarning($"Cleric authoring: {path} not found - skipped.");
                continue;
            }

            for (int i = 0; i < count; i++) { cards.Add(card); }
        }

        return cards;
    }

    // ------------------------------------------------------------------------------------------
    // Glossary
    // ------------------------------------------------------------------------------------------

    private static void AppendGlossaryRows()
    {
        Glossary glossary = AssetDatabase.LoadAssetAtPath<Glossary>(GlossaryPath);
        if (glossary == null)
        {
            Debug.LogWarning($"Cleric authoring: no Glossary asset at {GlossaryPath} - skipping tooltip rows.");
            return;
        }

        (StatusType type, string title, string body)[] rows =
        {
            (StatusType.Regeneration, "REGENERATION",
                "Heals {stacks} at the end of each of the carrier's turns, then decays by 1."),
            (StatusType.Lifesteal, "LIFESTEAL",
                "For {stacks} more of the carrier's turns, every hit it lands also heals it for half "
                + "the damage dealt."),
        };

        SerializedObject so = new(glossary);
        SerializedProperty statuses = so.FindProperty("statuses");

        foreach ((StatusType type, string title, string body) in rows)
        {
            bool exists = false;
            for (int i = 0; i < statuses.arraySize; i++)
            {
                if (statuses.GetArrayElementAtIndex(i).FindPropertyRelative("type").intValue == (int)type)
                {
                    exists = true;
                    break;
                }
            }
            if (exists) { continue; }

            int index = statuses.arraySize;
            statuses.arraySize++;

            SerializedProperty entry = statuses.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("title").stringValue = title;
            entry.FindPropertyRelative("body").stringValue = body;
            entry.FindPropertyRelative("terms").arraySize = 0;
            entry.FindPropertyRelative("defaultStacks").intValue = 1;
            entry.FindPropertyRelative("defaultAmount").intValue = 1;
            entry.FindPropertyRelative("type").intValue = (int)type;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(glossary);
    }

    // ------------------------------------------------------------------------------------------

    private static void EnsureFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) { return; }

        string parent = path[..path.LastIndexOf('/')];
        string leaf = path[(path.LastIndexOf('/') + 1)..];

        if (!AssetDatabase.IsValidFolder(parent)) { EnsureFolder(parent); }

        AssetDatabase.CreateFolder(parent, leaf);
    }
}
