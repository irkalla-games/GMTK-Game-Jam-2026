using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot content generator for the Knight/Mage card batch: 12 totem prefabs, the effect assets they
/// and six standalone cards need, the 18 CardData assets that tie it together, and four Glossary rows
/// so the new statuses read as sentences in a tooltip.
///
/// Unity has to do these writes, not a hand-authored .asset/.prefab file: a totem prefab is a copy of
/// Totem.prefab with several MonoBehaviour fields changed, and [field: SerializeField] auto-properties
/// on CardData can only be set through SerializedObject - see CardSheetImporter's header comment, which
/// this script follows for every field-name convention (`<name>k__BackingField` for CardData's
/// properties, the bare field name for everything else).
///
/// Idempotent: every Create step checks whether its target path already has an asset before building
/// anything, so running this twice - or after hand-editing one of its outputs - never clobbers work.
/// Totem prefabs are the one exception: CreateTotemPrefab repairs rather than skips, rewriting every
/// field a spec owns on every run, so a spec change (Sap/Fracture/Rampart's TurnTick -> plain-aura
/// conversion, say) reaches the prefab on disk instead of being silently ignored - see its own doc
/// comment. Everything else does not re-sync an existing asset's fields to match this script if they
/// have drifted; delete the asset and re-run to rebuild it from scratch.
///
/// Editor-only, and not covered by Tools/compile-check.ps1 without -IncludeEditor - see
/// CardSheetImporter's identical note.
/// </summary>
public static class TotemContentGenerator
{
    private const string TotemPrefabSource = "Assets/Prefabs/Totem/Totem.prefab";
    private const string TotemFolder = "Assets/Prefabs/Totem";
    private const string EffectRoot = "Assets/Data/EffectData";
    private const string CardRoot = "Assets/Data/CardData";

    /// Internal, not private: ClericContentGenerator constructs these directly for the Heal/Beacon/
    /// Sentinel totems rather than going through BuildTotemSpecs/BuildCardSpecs, which would also
    /// auto-generate a Knight- or Mage-class summon card for them (BuildCardSpecs assigns Knight for
    /// AuraAudience.Allies and Mage for Enemies, with no way to say Cleric) - see CreateTotemPrefab's
    /// own accessibility note.
    internal class TotemSpec
    {
        public string fileName;
        public string displayName;
        public AuraAudience affects;
        public StatusType auraType;

        /// The base counter written onto the AuraData entry. Inert for the three parameterised statuses
        /// (GainMultiplier, Potency, TurnTick), which read magnitude instead - see AuraData.CreateEffect
        /// - so those specs leave this at its default of 1. A plain aura (Weaken, Vulnerable, Block, ...)
        /// reads this as the real magnitude, same as Status.stacks everywhere else.
        public int stacks = 1;

        public StatusType subject;
        public int magnitude;
        public TurnTiming timing;
        public Color color;

        /// Written onto the totem's own Character component alongside displayName. Every totem here
        /// leaves this at the source prefab's default of 1 except the Taunt totems ClericContentGenerator
        /// builds - see Character.maxHealth and Totem's own "totems have 1 HP" precedent.
        public int maxHealth = 1;
    }

    private class EntrySpec
    {
        public string effectKey;
        public EffectTarget aimsAt;
        public AreaKind areaKind = AreaKind.Single;
        public RangeShape areaShape;
        public int areaMin;
        public int areaMax;

        public EntrySpec(string effectKey, EffectTarget aimsAt) : this(effectKey, aimsAt, AreaKind.Single) { }

        public EntrySpec(string effectKey, EffectTarget aimsAt, AreaKind areaKind,
            RangeShape areaShape = RangeShape.Anywhere, int areaMin = 0, int areaMax = 0)
        {
            this.effectKey = effectKey;
            this.aimsAt = aimsAt;
            this.areaKind = areaKind;
            this.areaShape = areaShape;
            this.areaMin = areaMin;
            this.areaMax = areaMax;
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
        public CharacterClass requiredClass;
        public Rarity rarity;
        public string description;
        public List<EntrySpec> entries;
    }

    [MenuItem("Tools/Cards/Generate Card Batch")]
    public static void Generate()
    {
        Dictionary<string, GameObject> totemPrefabs = new();
        foreach (TotemSpec spec in BuildTotemSpecs())
        {
            totemPrefabs[spec.fileName] = CreateTotemPrefab(spec);
        }

        Dictionary<string, CardEffect> effects = BuildEffects(totemPrefabs);

        foreach (CardSpec spec in BuildCardSpecs())
        {
            CreateCard(spec, effects);
        }

        AppendGlossaryRows();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Card sheet: batch generation complete.");
    }

    // ------------------------------------------------------------------------------------------
    // Totem prefabs
    // ------------------------------------------------------------------------------------------

    private static List<TotemSpec> BuildTotemSpecs()
    {
        return new List<TotemSpec>
        {
            new() { fileName = "WarcryTotem", displayName = "Warcry Totem", affects = AuraAudience.Allies,
                auraType = StatusType.GainMultiplier, subject = StatusType.Strength, magnitude = 2,
                color = new Color(0.85f, 0.55f, 0.15f, 1f) },
            new() { fileName = "BulwarkTotem", displayName = "Bulwark Totem", affects = AuraAudience.Allies,
                auraType = StatusType.GainMultiplier, subject = StatusType.Block, magnitude = 2,
                color = new Color(0.35f, 0.5f, 0.85f, 1f) },
            new() { fileName = "WarlordTotem", displayName = "Warlord Totem", affects = AuraAudience.Allies,
                auraType = StatusType.Potency, subject = StatusType.Strength, magnitude = 3,
                color = new Color(0.8f, 0.35f, 0.2f, 1f) },
            new() { fileName = "AegisTotem", displayName = "Aegis Totem", affects = AuraAudience.Allies,
                auraType = StatusType.Potency, subject = StatusType.Block, magnitude = 3,
                color = new Color(0.3f, 0.6f, 0.8f, 1f) },
            new() { fileName = "BastionTotem", displayName = "Bastion Totem", affects = AuraAudience.Allies,
                auraType = StatusType.TurnTick, subject = StatusType.Shield, magnitude = 5,
                timing = TurnTiming.TurnEnd, color = new Color(0.4f, 0.65f, 0.95f, 1f) },
            // A plain Block aura, not TurnTick - see the class doc on Sap/Fracture/Rampart below for why.
            new() { fileName = "RampartTotem", displayName = "Rampart Totem", affects = AuraAudience.Allies,
                auraType = StatusType.Block, stacks = 1, color = new Color(0.55f, 0.55f, 0.6f, 1f) },

            new() { fileName = "HexTotem", displayName = "Hex Totem", affects = AuraAudience.Enemies,
                auraType = StatusType.GainMultiplier, subject = StatusType.Weaken, magnitude = 2,
                color = new Color(0.55f, 0.2f, 0.6f, 1f) },
            new() { fileName = "CurseTotem", displayName = "Curse Totem", affects = AuraAudience.Enemies,
                auraType = StatusType.GainMultiplier, subject = StatusType.Vulnerable, magnitude = 2,
                color = new Color(0.75f, 0.15f, 0.25f, 1f) },
            new() { fileName = "EnfeebleTotem", displayName = "Enfeeble Totem", affects = AuraAudience.Enemies,
                auraType = StatusType.Potency, subject = StatusType.Weaken, magnitude = 3,
                color = new Color(0.45f, 0.25f, 0.55f, 1f) },
            new() { fileName = "RuinTotem", displayName = "Ruin Totem", affects = AuraAudience.Enemies,
                auraType = StatusType.Potency, subject = StatusType.Vulnerable, magnitude = 3,
                color = new Color(0.65f, 0.2f, 0.15f, 1f) },
            // Plain Weaken/Vulnerable auras, not TurnTick: a TurnTick totem only grants at OnTurnStart/
            // OnTurnEnd, so one summoned mid-PlayerActing would do nothing until the round after next -
            // BattleManager.TurnStart has already run by the time it hits the board. A maintained aura is
            // live the instant Totem.OnEnable registers it, which happens inside SummonAction itself, and
            // it is gone the instant its carrier steps out of range rather than lingering as a carried
            // status - see Totem's own "auras are pulled, not pushed" doc.
            new() { fileName = "SapTotem", displayName = "Sap Totem", affects = AuraAudience.Enemies,
                auraType = StatusType.Weaken, stacks = 2, color = new Color(0.4f, 0.3f, 0.5f, 1f) },
            new() { fileName = "FractureTotem", displayName = "Fracture Totem", affects = AuraAudience.Enemies,
                auraType = StatusType.Vulnerable, stacks = 2, color = new Color(0.6f, 0.25f, 0.2f, 1f) },
        };
    }

    /// <summary>
    /// Repairs the prefab at `path` if it already exists, rather than skipping it - the "a content
    /// generator should repair, not skip" rule (CLAUDE.md). Loading from the existing prefab rather
    /// than always from TotemPrefabSource means a re-run reaches whatever this totem's fields have
    /// drifted to, not just a fresh one; every field below is then rewritten unconditionally, including
    /// subject/magnitude/timing even when the spec does not use them, so converting a spec away from
    /// TurnTick actually clears what a previous run left in those three rather than leaving them stale.
    /// </summary>
    /// Internal, not private - ClericContentGenerator calls this directly for the Heal/Beacon/Sentinel
    /// totems, which are built one at a time rather than through BuildTotemSpecs/Generate (see
    /// TotemSpec's own accessibility note for why those three stay out of that shared list).
    internal static GameObject CreateTotemPrefab(TotemSpec spec)
    {
        string path = $"{TotemFolder}/{spec.fileName}.prefab";

        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        GameObject root = PrefabUtility.LoadPrefabContents(exists ? path : TotemPrefabSource);

        SerializedObject characterSO = new(root.GetComponent<Character>());
        characterSO.FindProperty("displayName").stringValue = spec.displayName;
        characterSO.FindProperty("maxHealth").intValue = spec.maxHealth;
        characterSO.ApplyModifiedProperties();

        SerializedObject totemSO = new(root.GetComponent<Totem>());

        SerializedProperty range = totemSO.FindProperty("range");
        range.FindPropertyRelative("shape").intValue = (int)RangeShape.Chebyshev;
        range.FindPropertyRelative("minDistance").intValue = 0;
        range.FindPropertyRelative("maxDistance").intValue = 2;

        totemSO.FindProperty("affects").intValue = (int)spec.affects;

        // These 12 totems are pure auras, no one-shot grant on a resolved action - clear whatever the
        // source prefab carries (Totem.prefab's own Shield-on-attack reaction).
        totemSO.FindProperty("reactions").arraySize = 0;

        SerializedProperty auras = totemSO.FindProperty("auras");
        auras.arraySize = 1;
        SerializedProperty aura = auras.GetArrayElementAtIndex(0);
        aura.FindPropertyRelative("type").intValue = (int)spec.auraType;
        aura.FindPropertyRelative("stacks").intValue = spec.stacks;
        aura.FindPropertyRelative("subject").intValue = (int)spec.subject;
        aura.FindPropertyRelative("magnitude").intValue = spec.magnitude;
        aura.FindPropertyRelative("timing").intValue = (int)spec.timing;

        totemSO.FindProperty("auraColor").colorValue = spec.color;

        totemSO.ApplyModifiedProperties();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);

        Debug.Log($"Card sheet: {(exists ? "updated" : "created")} {path}");
        return saved;
    }

    /// <summary>
    /// Rewrites just the 12 totem prefabs from BuildTotemSpecs, without touching any card or effect
    /// asset - the narrow half of Generate() above. Generate() also regenerates effects and cards, which
    /// would be far more churn than intended for a totem-only tuning pass (the Sap/Fracture/Rampart
    /// TurnTick -> plain-aura conversion, say); this is what running just that pass again should call.
    /// </summary>
    [MenuItem("Tools/Cards/Regenerate Totem Prefabs")]
    public static void RegenerateTotemPrefabs()
    {
        foreach (TotemSpec spec in BuildTotemSpecs())
        {
            CreateTotemPrefab(spec);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Card sheet: totem prefab regeneration complete.");
    }

    // ------------------------------------------------------------------------------------------
    // Effect assets
    // ------------------------------------------------------------------------------------------

    private static Dictionary<string, CardEffect> BuildEffects(Dictionary<string, GameObject> totemPrefabs)
    {
        Dictionary<string, CardEffect> effects = new();

        // Already authored by hand - Whirlwind, Sunder, Second Wind and the Draw halves of Bloodied
        // Resolve/Foresight reuse these rather than minting duplicates.
        AddExisting(effects, "Damage 4", $"{EffectRoot}/Damage/Damage 4.asset");
        AddExisting(effects, "Damage 5", $"{EffectRoot}/Damage/Damage 5.asset");
        AddExisting(effects, "Heal 5", $"{EffectRoot}/Heal/Heal 5.asset");
        AddExisting(effects, "Draw 1", $"{EffectRoot}/Draw/Draw 1.asset");

        effects["Vulnerable 2"] = CreateStatusEffect(
            $"{EffectRoot}/Status/Vulnerable 2.asset", StatusType.Vulnerable, 2, alliesOnly: false);

        effects["Self Damage 5"] = CreateSelfDamageEffect($"{EffectRoot}/SelfDamage/Self Damage 5.asset", 5);
        effects["Energy 1"] = CreateEnergyEffect($"{EffectRoot}/Energy/Energy 1.asset", 1);
        effects["Discard 1"] = CreateDiscardEffect($"{EffectRoot}/Discard/Discard 1.asset");

        foreach (TotemSpec spec in BuildTotemSpecs())
        {
            string summonKey = $"Summon{spec.fileName}";
            effects[summonKey] = CreateSummonEffect(
                $"{EffectRoot}/Summon/{summonKey}.asset", totemPrefabs[spec.fileName]);
        }

        return effects;
    }

    private static void AddExisting(Dictionary<string, CardEffect> effects, string key, string path)
    {
        CardEffect effect = AssetDatabase.LoadAssetAtPath<CardEffect>(path);
        if (effect == null)
        {
            Debug.LogError($"Card sheet: expected an existing effect at {path} - none found.");
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

    private static CardEffect CreateSelfDamageEffect(string path, int amount)
    {
        SelfDamageEffect effect = LoadOrCreate<SelfDamageEffect>(path);
        if (effect == null) { return AssetDatabase.LoadAssetAtPath<SelfDamageEffect>(path); }

        SerializedObject so = new(effect);
        so.FindProperty("amount").intValue = amount;
        so.ApplyModifiedProperties();
        return effect;
    }

    private static CardEffect CreateEnergyEffect(string path, int amount)
    {
        EnergyEffect effect = LoadOrCreate<EnergyEffect>(path);
        if (effect == null) { return AssetDatabase.LoadAssetAtPath<EnergyEffect>(path); }

        SerializedObject so = new(effect);
        so.FindProperty("amount").intValue = amount;
        so.ApplyModifiedProperties();
        return effect;
    }

    private static CardEffect CreateDiscardEffect(string path)
    {
        DiscardEffect effect = LoadOrCreate<DiscardEffect>(path);
        return effect != null ? effect : AssetDatabase.LoadAssetAtPath<DiscardEffect>(path);
    }

    private static CardEffect CreateSummonEffect(string path, GameObject totemPrefab)
    {
        SummonEffect effect = LoadOrCreate<SummonEffect>(path);
        if (effect == null) { return AssetDatabase.LoadAssetAtPath<SummonEffect>(path); }

        SerializedObject so = new(effect);
        so.FindProperty("summonedObject").objectReferenceValue = totemPrefab;
        // Permanent, same as every other totem summon in the game (SummonTotem, SummonWeakenTotem,
        // SummonVenomTotem all leave lifetimeTurns at its 0 default).
        so.ApplyModifiedProperties();
        return effect;
    }

    /// Returns null (rather than the existing asset) when one is already there, so callers can tell
    /// "just created, fields need setting" from "already exists, leave it alone" - the same idempotency
    /// rule every asset this script writes follows.
    private static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null)
        {
            Debug.Log($"Card sheet: {path} already exists - skipping.");
            return null;
        }

        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

        T asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        Debug.Log($"Card sheet: created {path}");
        return asset;
    }

    // ------------------------------------------------------------------------------------------
    // Cards
    // ------------------------------------------------------------------------------------------

    private static List<CardSpec> BuildCardSpecs()
    {
        List<CardSpec> cards = new()
        {
            new CardSpec
            {
                folder = "Knight/Melee Attack", cardName = "Whirlwind", cost = 2,
                rangeShape = RangeShape.SelfTile, rangeMin = 0, rangeMax = 0,
                requiredClass = CharacterClass.Knight, rarity = Rarity.Common,
                description = "Deal 4 damage to all adjacent enemies.",
                entries = new List<EntrySpec>
                {
                    new("Damage 4", EffectTarget.Source, AreaKind.Radius, RangeShape.Chebyshev, 0, 1),
                },
            },
            new CardSpec
            {
                folder = "Knight/Melee Attack", cardName = "Sunder", cost = 1,
                rangeShape = RangeShape.Chebyshev, rangeMin = 1, rangeMax = 1,
                requiredClass = CharacterClass.Knight, rarity = Rarity.Common,
                description = "Deal 5 damage and apply Vulnerable 2 to an enemy.",
                entries = new List<EntrySpec>
                {
                    new("Damage 5", EffectTarget.PlayedTile),
                    new("Vulnerable 2", EffectTarget.PlayedTile),
                },
            },
            new CardSpec
            {
                folder = "Knight/Heal", cardName = "Second Wind", cost = 1,
                rangeShape = RangeShape.SelfTile, rangeMin = 0, rangeMax = 0,
                requiredClass = CharacterClass.Knight, rarity = Rarity.Common,
                description = "Heal 5 health.",
                entries = new List<EntrySpec> { new("Heal 5", EffectTarget.Source) },
            },
            new CardSpec
            {
                folder = "Knight/Utility", cardName = "Bloodied Resolve", cost = 0,
                rangeShape = RangeShape.SelfTile, rangeMin = 0, rangeMax = 0,
                requiredClass = CharacterClass.Knight, rarity = Rarity.Uncommon,
                description = "Draw a card. Take 5 damage.",
                entries = new List<EntrySpec>
                {
                    new("Draw 1", EffectTarget.Source),
                    new("Self Damage 5", EffectTarget.Source),
                },
            },
            new CardSpec
            {
                folder = "Knight/Utility", cardName = "Adrenaline", cost = 0,
                rangeShape = RangeShape.SelfTile, rangeMin = 0, rangeMax = 0,
                requiredClass = CharacterClass.Knight, rarity = Rarity.Uncommon,
                description = "Gain 1 energy. Take 5 damage.",
                entries = new List<EntrySpec>
                {
                    new("Energy 1", EffectTarget.Source),
                    new("Self Damage 5", EffectTarget.Source),
                },
            },
            new CardSpec
            {
                folder = "Mage/Utility", cardName = "Foresight", cost = 0,
                rangeShape = RangeShape.SelfTile, rangeMin = 0, rangeMax = 0,
                requiredClass = CharacterClass.Mage, rarity = Rarity.Uncommon,
                description = "Draw a card, then discard a card.",
                entries = new List<EntrySpec>
                {
                    new("Draw 1", EffectTarget.Source),
                    new("Discard 1", EffectTarget.Source),
                },
            },
        };

        foreach (TotemSpec spec in BuildTotemSpecs())
        {
            bool knight = spec.affects == AuraAudience.Allies;

            cards.Add(new CardSpec
            {
                folder = knight ? "Knight/Summon" : "Mage/Summon",
                cardName = spec.displayName,
                cost = 2,
                rangeShape = RangeShape.Chebyshev,
                rangeMin = 1,
                rangeMax = 3,
                requiredClass = knight ? CharacterClass.Knight : CharacterClass.Mage,
                rarity = Rarity.Uncommon,
                description = $"Summon a {spec.displayName}.",
                entries = new List<EntrySpec>
                {
                    new($"Summon{spec.fileName}", EffectTarget.PlayedTile),
                },
            });
        }

        return cards;
    }

    private static void CreateCard(CardSpec spec, Dictionary<string, CardEffect> effects)
    {
        string path = $"{CardRoot}/{spec.folder}/{spec.cardName}.asset";

        if (AssetDatabase.LoadAssetAtPath<CardData>(path) != null)
        {
            Debug.Log($"Card sheet: {path} already exists - skipping.");
            return;
        }

        List<CardEffect> resolved = new();
        foreach (EntrySpec entry in spec.entries)
        {
            if (!effects.TryGetValue(entry.effectKey, out CardEffect effect) || effect == null)
            {
                Debug.LogError($"Card sheet: '{spec.cardName}' references effect '{entry.effectKey}', "
                                + "which was not built. Skipping this card.");
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
        so.FindProperty("<requiredClass>k__BackingField").intValue = (int)spec.requiredClass;
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
            area.FindPropertyRelative("kind").intValue = (int)source.areaKind;

            SerializedProperty radius = area.FindPropertyRelative("radius");
            radius.FindPropertyRelative("shape").intValue = (int)source.areaShape;
            radius.FindPropertyRelative("minDistance").intValue = source.areaMin;
            radius.FindPropertyRelative("maxDistance").intValue = source.areaMax;

            area.FindPropertyRelative("pattern").objectReferenceValue = null;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(card);

        Debug.Log($"Card sheet: created {path}");
    }

    // ------------------------------------------------------------------------------------------
    // Glossary
    // ------------------------------------------------------------------------------------------

    private const string GlossaryPath = "Assets/Scripts/UI/Tooltips/Glossary.asset";

    private class GlossaryRow
    {
        public StatusType type;
        public string title;
        public string body;
        public int defaultStacks = 1;
        public int defaultAmount = 1;
    }

    private static void AppendGlossaryRows()
    {
        Glossary glossary = AssetDatabase.LoadAssetAtPath<Glossary>(GlossaryPath);
        if (glossary == null)
        {
            Debug.LogWarning($"Card sheet: no Glossary asset at {GlossaryPath} - skipping tooltip rows.");
            return;
        }

        List<GlossaryRow> rows = new()
        {
            new GlossaryRow
            {
                type = StatusType.Vulnerable, title = "VULNERABLE",
                body = "Adds 3 damage to each of the next {stacks} hits taken.",
                defaultStacks = 1, defaultAmount = 3,
            },
            new GlossaryRow
            {
                type = StatusType.GainMultiplier, title = "EMPOWERED",
                body = "Doubles the stacks of whatever status this totem empowers, whenever it is applied.",
            },
            new GlossaryRow
            {
                type = StatusType.Potency, title = "POTENT",
                body = "Strengthens whatever status this totem empowers, without changing its stacks.",
            },
            new GlossaryRow
            {
                type = StatusType.TurnTick, title = "STANDING WARD",
                body = "Grants a status to whoever stands in range, once every round.",
            },
        };

        SerializedObject so = new(glossary);
        SerializedProperty statuses = so.FindProperty("statuses");

        foreach (GlossaryRow row in rows)
        {
            if (StatusEntryExists(statuses, row.type))
            {
                Debug.Log($"Card sheet: Glossary already has a {row.type} entry - skipping.");
                continue;
            }

            int index = statuses.arraySize;
            statuses.arraySize++;

            SerializedProperty entry = statuses.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("title").stringValue = row.title;
            entry.FindPropertyRelative("body").stringValue = row.body;
            entry.FindPropertyRelative("terms").arraySize = 0;
            entry.FindPropertyRelative("defaultStacks").intValue = row.defaultStacks;
            entry.FindPropertyRelative("defaultAmount").intValue = row.defaultAmount;
            entry.FindPropertyRelative("type").intValue = (int)row.type;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(glossary);
    }

    private static bool StatusEntryExists(SerializedProperty statuses, StatusType type)
    {
        for (int i = 0; i < statuses.arraySize; i++)
        {
            if (statuses.GetArrayElementAtIndex(i).FindPropertyRelative("type").intValue == (int)type)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// One-off wording fix for the three totem-only "meta" statuses - EMPOWERED/POTENT/STANDING WARD
    /// (GainMultiplier/Potency/TurnTick) - whose bodies were originally authored generic ("Grants a
    /// status to whoever stands in range") because Glossary.SummonContent did not yet read a totem's
    /// own subject/magnitude/targets/timing. Now that it does (see Glossary.SummonContent's per-aura
    /// loop), these three rows need their body text switched to the {status}/{amount}/{targets}/
    /// {timing} template so each totem reads its own sentence instead of a shared one.
    ///
    /// Deliberately separate from AppendGlossaryRows: that method's idempotency is "skip a row that
    /// already exists," which is exactly wrong here - these three rows already exist and are precisely
    /// what needs overwriting. This converges to the same three bodies every time it is run, and never
    /// touches any other row.
    /// </summary>
    [MenuItem("Tools/Cards/Update Totem Glossary Bodies")]
    private static void UpdateMetaAuraGlossaryBodies()
    {
        Glossary glossary = AssetDatabase.LoadAssetAtPath<Glossary>(GlossaryPath);
        if (glossary == null)
        {
            Debug.LogWarning($"Card sheet: no Glossary asset at {GlossaryPath} - skipping tooltip rows.");
            return;
        }

        Dictionary<StatusType, string> bodies = new()
        {
            [StatusType.GainMultiplier] = "Multiplies {status} granted to all {targets} in range by {amount}.",
            [StatusType.Potency] = "Adds {amount} to the potency of {status} for all {targets} in range.",
            [StatusType.TurnTick] = "Grants {amount} {status} to all {targets} in range {timing}.",
        };

        SerializedObject so = new(glossary);
        SerializedProperty statuses = so.FindProperty("statuses");

        for (int i = 0; i < statuses.arraySize; i++)
        {
            SerializedProperty entry = statuses.GetArrayElementAtIndex(i);
            StatusType type = (StatusType)entry.FindPropertyRelative("type").intValue;

            if (!bodies.TryGetValue(type, out string body)) { continue; }

            entry.FindPropertyRelative("body").stringValue = body;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(glossary);
        AssetDatabase.SaveAssets();

        Debug.Log("Card sheet: updated EMPOWERED/POTENT/STANDING WARD glossary bodies.");
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
