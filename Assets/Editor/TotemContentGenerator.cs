using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
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

    /// <summary>
    /// One aura entry on a totem, mirroring AuraData's five serialized fields exactly.
    ///
    /// A list on TotemSpec rather than five fields, because Lodestone Totem carries two - Taunt to pull
    /// enemies in and Rooted to hold them there - and nothing in the prefab writer ever cared that
    /// there had only ever been one. Build these through the named helpers below rather than by
    /// setting fields: which of subject/magnitude/timing a given type actually reads is decided in
    /// AuraData.CreateEffect, and the helpers are what stop a spec from quietly setting one that its
    /// type ignores.
    /// </summary>
    internal class AuraSpec
    {
        public StatusType type;

        /// The base counter written onto the AuraData entry. Inert for every parameterised type
        /// (GainMultiplier, Potency, TurnTick, GainBonus and the two FlatDamage types), which read
        /// magnitude instead - see AuraData.CreateEffect. A plain aura (Weaken, Block, Stealth, ...)
        /// reads this as the real magnitude, same as Status.stacks everywhere else.
        public int stacks = 1;

        public StatusType subject;
        public int magnitude;
        public TurnTiming timing;
    }

    /// A maintained status - the carrier holds it for as long as it stands in range.
    internal static AuraSpec Plain(StatusType type, int stacks) =>
        new() { type = type, stacks = stacks };

    /// Multiplies the stacks of `subject` whenever that status is applied to someone in range.
    internal static AuraSpec Mult(StatusType subject, int times) =>
        new() { type = StatusType.GainMultiplier, subject = subject, magnitude = times };

    /// Deepens `subject` without touching its stack count. Only fires while the carrier already holds
    /// a charge of it - see PotencyStatus, which implements Strength, Block, Weaken and Vulnerable and
    /// silently does nothing for any other subject.
    internal static AuraSpec Pot(StatusType subject, int bonus) =>
        new() { type = StatusType.Potency, subject = subject, magnitude = bonus };

    /// Grants `amount` stacks of `subject` once per round. Timing matters: a grant sharing its end of
    /// the round with the granted status's own reset is erased in the same pass - see TurnTiming.
    internal static AuraSpec Tick(StatusType subject, int amount, TurnTiming timing) =>
        new() { type = StatusType.TurnTick, subject = subject, magnitude = amount, timing = timing };

    /// Adds a flat amount to the stacks of `subject` whenever it is granted - Mult's additive sibling.
    internal static AuraSpec Bonus(StatusType subject, int amount) =>
        new() { type = StatusType.GainBonus, subject = subject, magnitude = amount };

    /// Unconditional +damage on every attack made from inside. Unlike Pot(Strength) this needs no
    /// Strength on the board, and unlike Plain(Strength) it carries its own number rather than
    /// inheriting StrengthStatus.AmountPerHit's const 3.
    internal static AuraSpec DamageDealt(int amount) =>
        new() { type = StatusType.FlatDamageDealt, magnitude = amount };

    /// Unconditional -damage on every hit taken inside. BlockStatus.AmountPerHit is likewise a const 3,
    /// so this is the only way to author any other number.
    internal static AuraSpec DamageTaken(int amount) =>
        new() { type = StatusType.FlatDamageTaken, magnitude = amount };

    /// <summary>
    /// One reaction entry on a totem, mirroring AuraReaction's three serialized fields. `effectKey`
    /// names an entry in the dictionary BuildReactionEffects returns, the same indirection CardSpec's
    /// EntrySpec already uses, so a spec never holds a live asset reference.
    /// </summary>
    internal class ReactionSpec
    {
        public TriggeringActionType trigger;
        public string effectKey;
        public string description;
    }

    /// Internal, not private: ClericContentGenerator constructs these directly for the Heal/Beacon/
    /// Sentinel totems rather than going through BuildTotemSpecs/BuildCardSpecs, so that it keeps
    /// authoring their cards alongside the rest of the Cleric set - see CreateTotemPrefab's own
    /// accessibility note.
    internal class TotemSpec
    {
        public string fileName;
        public string displayName;
        public AuraAudience affects;

        /// Maintained/parameterised auras, projected onto everyone in range. Empty for a totem that is
        /// purely reactive.
        public List<AuraSpec> auras = new();

        /// One-shot effects fired at whoever triggers them. Empty for a totem that is purely passive.
        public List<ReactionSpec> reactions = new();

        /// Written onto the totem's own Character component alongside displayName. Every totem here
        /// leaves this at the source prefab's default of 1 except the Taunt totems, whose whole job is
        /// to be attacked - see Character.maxHealth.
        public int maxHealth = 1;

        /// <summary>
        /// Which class's deck the generated summon card belongs to.
        ///
        /// Replaces the old "Allies means Knight, Enemies means Mage" guess in BuildCardSpecs, which
        /// had no way to say Cleric or Rogue at all - and which is why ClericContentGenerator used to
        /// have to move six freshly-written cards from Knight/Summon to Cleric/Summon after the fact.
        /// </summary>
        public CharacterClass requiredClass = CharacterClass.Knight;

        public int cost = 2;

        public Rarity rarity = Rarity.Uncommon;

        /// <summary>
        /// Chebyshev radius of the aura, measured from the totem's own tile. The minimum is always 0:
        /// a totem stands inside its own ring.
        ///
        /// Was hardcoded to 2 for every totem. Twin and Reinforce need 1 - their ×2 is untunable and
        /// their charges never deplete as auras, so the radius is the only lever left - and Decoy needs
        /// 3 to pull from far enough away to be worth a card.
        /// </summary>
        public int radius = 2;

        /// <summary>
        /// Which karsiori gem this totem wears. Shape says what the totem does - one shape per mechanic
        /// - and colour says which status it touches. See EnsureGemController.
        ///
        /// The colour also decides the aura wash on the floor: auraColor is looked up from GemColours
        /// rather than authored, so the ring and the gem standing in it can never disagree.
        /// </summary>
        public int gem;

        public string gemColour;
    }

    /// <summary>
    /// The aura wash for each gem colour, averaged from the frames themselves.
    ///
    /// Keyed by the pack's own folder names, so this table doubles as the list of colours that exist -
    /// a spec naming anything else fails loudly in GemColour rather than silently drawing black.
    /// </summary>
    private static readonly Dictionary<string, Color> GemColours = new()
    {
        ["RED"] = Hex(0xd4, 0x7a, 0x7a),
        ["LIGHT GREEN"] = Hex(0x88, 0xbe, 0x82),
        ["DARK BLUE"] = Hex(0x7c, 0x8b, 0xc2),
        ["TURQUOISE"] = Hex(0x72, 0xb8, 0xa9),
        ["BLUE"] = Hex(0x6e, 0xa6, 0xb7),
        ["PURPLE"] = Hex(0x90, 0x7e, 0xba),
        ["LILAC"] = Hex(0xb6, 0x6d, 0xa9),
        ["GOLD"] = Hex(0xb5, 0xab, 0x6c),
    };

    private static Color Hex(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

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
        // Before anything is written: a card whose file is not named after the card would otherwise be
        // invisible to CreateCard's already-exists check and get a duplicate written beside it.
        RenameLegacyCards();

        // Strict dependency order, and the reason there is no AssetDatabase.StartAssetEditing anywhere
        // in this file: a reaction totem's prefab holds a live CardEffect, and a summon effect holds a
        // live prefab, so each stage is built and handed down as objects rather than re-loaded by path.
        Dictionary<string, CardEffect> reactionEffects = BuildReactionEffects();

        Dictionary<string, GameObject> totemPrefabs = new();
        foreach (TotemSpec spec in BuildTotemSpecs())
        {
            totemPrefabs[spec.fileName] = CreateTotemPrefab(spec, reactionEffects);
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

    /// <summary>
    /// The whole totem roster, grouped by gem shape - which is to say by mechanic, since that is what a
    /// shape means. Colour then says which status the totem touches, so a player can read both facts
    /// off the board without a tooltip.
    ///
    /// Heal, Beacon and Sentinel are deliberately absent: ClericContentGenerator builds those three, so
    /// that it can keep authoring their cards alongside the rest of the Cleric set. Totem.prefab itself
    /// is absent for a different reason - it is the source every prefab here is copied from, and also a
    /// shipped Knight card ("Shield Totem"), so regenerating it from a spec would mean rewriting the
    /// template mid-run.
    /// </summary>
    private static List<TotemSpec> BuildTotemSpecs()
    {
        return new List<TotemSpec>
        {
            // -- Gem 1, turn-tick boon: grants stacks to allies in range once per round ---------------
            new() { fileName = "BastionTotem", displayName = "Bastion Totem", gem = 1, gemColour = "TURQUOISE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Cleric,
                auras = { Tick(StatusType.Shield, 5, TurnTiming.TurnEnd) } },
            new() { fileName = "AnvilTotem", displayName = "Anvil Totem", gem = 1, gemColour = "DARK BLUE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Knight,
                auras = { Tick(StatusType.Block, 2, TurnTiming.TurnEnd) } },
            new() { fileName = "RallyTotem", displayName = "Rally Totem", gem = 1, gemColour = "LIGHT GREEN",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Knight,
                auras = { Tick(StatusType.Strength, 2, TurnTiming.TurnStart) } },
            new() { fileName = "WellspringTotem", displayName = "Wellspring Totem", gem = 1, gemColour = "TURQUOISE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Cleric,
                auras = { Tick(StatusType.Regeneration, 2, TurnTiming.TurnEnd) } },
            new() { fileName = "GhoststepTotem", displayName = "Ghoststep Totem", gem = 1, gemColour = "BLUE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Rogue,
                auras = { Tick(StatusType.Dodge, 1, TurnTiming.TurnStart) } },

            // -- Gem 2, maintained boon: held only while you stand inside ------------------------------
            new() { fileName = "FuryTotem", displayName = "Fury Totem", gem = 2, gemColour = "LIGHT GREEN",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Knight,
                auras = { Plain(StatusType.Strength, 1) } },
            new() { fileName = "LeechTotem", displayName = "Leech Totem", gem = 2, gemColour = "TURQUOISE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Rogue,
                auras = { Plain(StatusType.Lifesteal, 1) } },
            new() { fileName = "VenombladeTotem", displayName = "Venomblade Totem", gem = 2, gemColour = "RED",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Rogue,
                auras = { Plain(StatusType.PoisonBlade, 1) } },
            new() { fileName = "VeilTotem", displayName = "Veil Totem", gem = 2, gemColour = "BLUE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Rogue, cost = 3,
                auras = { Plain(StatusType.Stealth, 1) } },

            // -- Gem 3, potency boon: changes the size of a number rather than granting a status --------
            new() { fileName = "WarlordTotem", displayName = "Warlord Totem", gem = 3, gemColour = "LIGHT GREEN",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Cleric,
                auras = { Pot(StatusType.Strength, 3) } },
            new() { fileName = "AegisTotem", displayName = "Aegis Totem", gem = 3, gemColour = "DARK BLUE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Cleric,
                auras = { Pot(StatusType.Block, 3) } },
            // Was authored as Block 1. Because an aura is rebuilt fresh per query its charge never
            // depletes, so that was already a flat -3 on every hit forever, wearing a charge counter's
            // clothes. This says what it does, and the 3 becomes a number somebody can tune.
            new() { fileName = "RampartTotem", displayName = "Rampart Totem", gem = 3, gemColour = "DARK BLUE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Cleric,
                auras = { DamageTaken(3) } },
            new() { fileName = "WhetstoneTotem", displayName = "Whetstone Totem", gem = 3, gemColour = "RED",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Knight,
                auras = { DamageDealt(3) } },
            new() { fileName = "TowerTotem", displayName = "Tower Totem", gem = 3, gemColour = "TURQUOISE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Cleric,
                auras = { Bonus(StatusType.Shield, 3) } },
            // Radius 1 and cost 3, both load-bearing: DoubleNextAttack and DoubleShield have no
            // magnitude field, and their charges never spend as auras, so these double *every* attack
            // and *every* shield gain for as long as somebody stands there. The footprint is the only
            // dial left.
            new() { fileName = "TwinTotem", displayName = "Twin Totem", gem = 3, gemColour = "RED",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Knight,
                radius = 1, cost = 3, rarity = Rarity.Rare,
                auras = { Plain(StatusType.DoubleNextAttack, 1) } },
            new() { fileName = "ReinforceTotem", displayName = "Reinforce Totem", gem = 3, gemColour = "TURQUOISE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Cleric,
                radius = 1, cost = 3, rarity = Rarity.Rare,
                auras = { Plain(StatusType.DoubleShield, 1) } },

            // -- Gem 4, turn-tick bane -----------------------------------------------------------------
            // Enemies, not Everyone: this shipped set to Everyone and quietly poisoned your own party.
            new() { fileName = "VenomTotem", displayName = "Venom Totem", gem = 4, gemColour = "RED",
                affects = AuraAudience.Enemies, requiredClass = CharacterClass.Rogue,
                auras = { Tick(StatusType.Poison, 2, TurnTiming.TurnEnd) } },
            new() { fileName = "RotTotem", displayName = "Rot Totem", gem = 4, gemColour = "RED",
                affects = AuraAudience.Enemies, requiredClass = CharacterClass.Rogue,
                cost = 3, rarity = Rarity.Rare,
                auras = { Tick(StatusType.Poison, 4, TurnTiming.TurnEnd) } },
            // TurnStart, not TurnEnd: RootedStatus ages itself on the carrier's own turn, so a TurnEnd
            // grant would be spent by the reset in the same pass - see TurnTiming.
            new() { fileName = "ShackleTotem", displayName = "Shackle Totem", gem = 4, gemColour = "BLUE",
                affects = AuraAudience.Enemies, requiredClass = CharacterClass.Mage,
                auras = { Tick(StatusType.Rooted, 1, TurnTiming.TurnStart) } },

            // -- Gem 5, gain-multiplier boon -----------------------------------------------------------
            new() { fileName = "WarcryTotem", displayName = "Warcry Totem", gem = 5, gemColour = "LIGHT GREEN",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Cleric,
                cost = 3, rarity = Rarity.Rare,
                auras = { Mult(StatusType.Strength, 2) } },
            new() { fileName = "BulwarkTotem", displayName = "Bulwark Totem", gem = 5, gemColour = "DARK BLUE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Cleric,
                auras = { Mult(StatusType.Block, 2) } },
            new() { fileName = "WellheadTotem", displayName = "Wellhead Totem", gem = 5, gemColour = "TURQUOISE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Cleric,
                auras = { Mult(StatusType.Shield, 2) } },
            new() { fileName = "BloomTotem", displayName = "Bloom Totem", gem = 5, gemColour = "TURQUOISE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Cleric,
                auras = { Mult(StatusType.Regeneration, 2) } },
            new() { fileName = "FortuneTotem", displayName = "Fortune Totem", gem = 5, gemColour = "BLUE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Rogue,
                auras = { Mult(StatusType.Dodge, 2) } },

            // -- Gem 6, maintained bane ----------------------------------------------------------------
            // Plain auras, not TurnTick: a TurnTick totem only grants at OnTurnStart/OnTurnEnd, so one
            // summoned mid-PlayerActing would do nothing until the round after next - BattleManager's
            // TurnStart has already run by then. A maintained aura is live the instant Totem.OnEnable
            // registers it, inside SummonAction itself, and gone the instant its carrier steps out of
            // range rather than lingering as a carried status.
            new() { fileName = "SapTotem", displayName = "Sap Totem", gem = 6, gemColour = "PURPLE",
                affects = AuraAudience.Enemies, requiredClass = CharacterClass.Mage,
                auras = { Plain(StatusType.Weaken, 2) } },
            new() { fileName = "FractureTotem", displayName = "Fracture Totem", gem = 6, gemColour = "LILAC",
                affects = AuraAudience.Enemies, requiredClass = CharacterClass.Mage,
                auras = { Plain(StatusType.Vulnerable, 2) } },

            // -- Gem 6, double-edged: the only totems that reach both sides -----------------------------
            new() { fileName = "BloodmoonTotem", displayName = "Bloodmoon Totem", gem = 6, gemColour = "LIGHT GREEN",
                affects = AuraAudience.Everyone, requiredClass = CharacterClass.Knight,
                cost = 1, rarity = Rarity.Rare,
                auras = { Plain(StatusType.Strength, 2) } },
            new() { fileName = "ChaosTotem", displayName = "Chaos Totem", gem = 6, gemColour = "LILAC",
                affects = AuraAudience.Everyone, requiredClass = CharacterClass.Mage,
                cost = 1, rarity = Rarity.Rare,
                auras = { Plain(StatusType.Vulnerable, 2) } },

            // -- Gem 7, potency bane -------------------------------------------------------------------
            new() { fileName = "EnfeebleTotem", displayName = "Enfeeble Totem", gem = 7, gemColour = "PURPLE",
                affects = AuraAudience.Enemies, requiredClass = CharacterClass.Mage,
                auras = { Pot(StatusType.Weaken, 3) } },
            new() { fileName = "RuinTotem", displayName = "Ruin Totem", gem = 7, gemColour = "LILAC",
                affects = AuraAudience.Enemies, requiredClass = CharacterClass.Mage,
                auras = { Pot(StatusType.Vulnerable, 3) } },

            // -- Gem 8, gain-multiplier bane -----------------------------------------------------------
            new() { fileName = "HexTotem", displayName = "Hex Totem", gem = 8, gemColour = "PURPLE",
                affects = AuraAudience.Enemies, requiredClass = CharacterClass.Mage,
                cost = 3, rarity = Rarity.Rare,
                auras = { Mult(StatusType.Weaken, 2) } },
            new() { fileName = "CurseTotem", displayName = "Curse Totem", gem = 8, gemColour = "LILAC",
                affects = AuraAudience.Enemies, requiredClass = CharacterClass.Mage,
                cost = 3, rarity = Rarity.Rare,
                auras = { Mult(StatusType.Vulnerable, 2) } },
            new() { fileName = "ContagionTotem", displayName = "Contagion Totem", gem = 8, gemColour = "RED",
                affects = AuraAudience.Enemies, requiredClass = CharacterClass.Rogue,
                auras = { Mult(StatusType.Poison, 2) } },
            new() { fileName = "BindingTotem", displayName = "Binding Totem", gem = 8, gemColour = "BLUE",
                affects = AuraAudience.Enemies, requiredClass = CharacterClass.Mage,
                auras = { Mult(StatusType.Rooted, 2) } },

            // -- Gem 9, taunt / control ----------------------------------------------------------------
            new() { fileName = "DecoyTotem", displayName = "Decoy Totem", gem = 9, gemColour = "GOLD",
                affects = AuraAudience.Enemies, requiredClass = CharacterClass.Rogue,
                radius = 3, cost = 1, maxHealth = 1,
                auras = { Plain(StatusType.Taunt, 1) } },
            // The only totem carrying two auras: pulls them in, then holds them there.
            new() { fileName = "LodestoneTotem", displayName = "Lodestone Totem", gem = 9, gemColour = "BLUE",
                affects = AuraAudience.Enemies, requiredClass = CharacterClass.Knight,
                maxHealth = 8, cost = 3, rarity = Rarity.Rare,
                auras = { Plain(StatusType.Taunt, 1), Plain(StatusType.Rooted, 1) } },

            // -- Gem 10, reaction: fires when somebody in range resolves a matching action --------------
            new() { fileName = "ThornwoodTotem", displayName = "Thornwood Totem", gem = 10, gemColour = "RED",
                affects = AuraAudience.Enemies, requiredClass = CharacterClass.Knight,
                reactions = { React(TriggeringActionType.Damage, "Damage 3",
                    "When an enemy attacks nearby, deal 3 damage back to them.") } },
            new() { fileName = "SpikeTotem", displayName = "Spike Totem", gem = 10, gemColour = "RED",
                affects = AuraAudience.Enemies, requiredClass = CharacterClass.Rogue,
                reactions = { React(TriggeringActionType.Move, "Damage 2",
                    "When an enemy moves nearby, deal 2 damage to them.") } },
            new() { fileName = "MomentumTotem", displayName = "Momentum Totem", gem = 10, gemColour = "LIGHT GREEN",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Knight,
                reactions = { React(TriggeringActionType.Move, "Strength 1",
                    "When an ally moves nearby, they gain Strength 1.") } },
            new() { fileName = "BloodpactTotem", displayName = "Bloodpact Totem", gem = 10, gemColour = "TURQUOISE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Cleric,
                reactions = { React(TriggeringActionType.Damage, "Heal 3",
                    "When an ally attacks nearby, they heal 3.") } },
            new() { fileName = "EchoTotem", displayName = "Echo Totem", gem = 10, gemColour = "TURQUOISE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Cleric,
                reactions = { React(TriggeringActionType.Summon, "Shield 5",
                    "When an ally summons nearby, they gain Shield 5.") } },
            new() { fileName = "SanctuaryTotem", displayName = "Sanctuary Totem", gem = 10, gemColour = "GOLD",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Cleric,
                reactions = { React(TriggeringActionType.Heal, "Draw 1",
                    "When an ally heals nearby, they draw a card.") } },
            new() { fileName = "ScholarTotem", displayName = "Scholar Totem", gem = 10, gemColour = "GOLD",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Mage,
                reactions = { React(TriggeringActionType.Draw, "Energy 1",
                    "When an ally draws nearby, they gain 1 energy.") } },
            new() { fileName = "ConduitTotem", displayName = "Conduit Totem", gem = 10, gemColour = "GOLD",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Mage,
                reactions = { React(TriggeringActionType.Status, "Draw 1",
                    "When an ally applies a status nearby, they draw a card.") } },

            // Last on purpose, and the one spec whose fileName is not its own name: this rewrites
            // Totem.prefab, which is both a shipped Knight card and TotemPrefabSource - the prefab every
            // other totem here is first copied from. Specced rather than left alone so it wears a gem
            // like the rest; kept last so the copying is done by the time it is rewritten.
            //
            // Naming it "Totem" is what keeps the existing chain intact: the effect key becomes
            // SummonTotem, which is the effect asset that already exists and already points at this
            // prefab, so nothing has to be re-pointed and no duplicate effect is minted. The card is
            // reached by RenameLegacyCards instead - its file is SummonTotem.asset while the card
            // inside it is called "Shield Totem".
            new() { fileName = "Totem", displayName = "Shield Totem", gem = 10, gemColour = "TURQUOISE",
                affects = AuraAudience.Allies, requiredClass = CharacterClass.Knight,
                cost = 3, rarity = Rarity.Rare,
                reactions = { React(TriggeringActionType.Damage, "Shield 3",
                    "When an ally attacks nearby, they gain Shield 3.") } },
        };
    }

    private static ReactionSpec React(TriggeringActionType trigger, string effectKey, string description) =>
        new() { trigger = trigger, effectKey = effectKey, description = description };

    /// <summary>
    /// Brings a card whose filename predates the "the file is named after the card" convention into
    /// line, so CreateCard's already-exists check recognises it instead of writing a second copy beside
    /// it under the spaced name.
    ///
    /// A rename and emphatically not a delete-and-recreate: AssetDatabase.MoveAsset keeps the GUID, and
    /// Venom Totem is referenced by RogueStarter, RogueTest and AllCards. Recreating it would empty a
    /// slot in two decks the moment this ran.
    /// </summary>
    private static void RenameLegacyCards()
    {
        RenameCard($"{CardRoot}/Rogue/Summon/VenomTotem.asset",
                   $"{CardRoot}/Rogue/Summon/Venom Totem.asset");

        // Named after the effect that summons it rather than the card inside it, which is called
        // "Shield Totem" - see the Totem spec in BuildTotemSpecs.
        RenameCard($"{CardRoot}/Knight/Summon/SummonTotem.asset",
                   $"{CardRoot}/Knight/Summon/Shield Totem.asset");
    }

    private static void RenameCard(string from, string to)
    {
        if (AssetDatabase.LoadAssetAtPath<CardData>(from) == null) { return; }

        if (AssetDatabase.LoadAssetAtPath<CardData>(to) != null)
        {
            Debug.LogError($"Card sheet: cannot rename {from} - something already sits at {to}. "
                            + "One of the two is a duplicate and wants deleting by hand.");
            return;
        }

        string error = AssetDatabase.MoveAsset(from, to);

        if (string.IsNullOrEmpty(error)) { Debug.Log($"Card sheet: renamed {from} -> {to}"); }
        else { Debug.LogError($"Card sheet: renaming {from} failed - {error}"); }
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
    internal static GameObject CreateTotemPrefab(TotemSpec spec,
        Dictionary<string, CardEffect> reactionEffects = null)
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
        range.FindPropertyRelative("maxDistance").intValue = spec.radius;

        totemSO.FindProperty("affects").intValue = (int)spec.affects;

        SerializedProperty auras = totemSO.FindProperty("auras");
        auras.arraySize = spec.auras.Count;
        for (int i = 0; i < spec.auras.Count; i++)
        {
            AuraSpec source = spec.auras[i];
            SerializedProperty aura = auras.GetArrayElementAtIndex(i);

            // Every field written unconditionally, including the three a given type ignores, so
            // converting a spec away from TurnTick actually clears what a previous run left behind
            // rather than leaving a stale subject/magnitude/timing on disk.
            aura.FindPropertyRelative("type").intValue = (int)source.type;
            aura.FindPropertyRelative("stacks").intValue = source.stacks;
            aura.FindPropertyRelative("subject").intValue = (int)source.subject;
            aura.FindPropertyRelative("magnitude").intValue = source.magnitude;
            aura.FindPropertyRelative("timing").intValue = (int)source.timing;
        }

        SerializedProperty reactions = totemSO.FindProperty("reactions");
        reactions.arraySize = spec.reactions.Count;
        for (int i = 0; i < spec.reactions.Count; i++)
        {
            ReactionSpec source = spec.reactions[i];
            SerializedProperty reaction = reactions.GetArrayElementAtIndex(i);

            CardEffect effect = null;
            if (reactionEffects != null) { reactionEffects.TryGetValue(source.effectKey, out effect); }

            if (effect == null)
            {
                Debug.LogError($"Card sheet: {spec.displayName} reacts with effect "
                                + $"'{source.effectKey}', which was not built. Its reaction will be empty.");
            }

            reaction.FindPropertyRelative("trigger").intValue = (int)source.trigger;
            reaction.FindPropertyRelative("effect").objectReferenceValue = effect;
            reaction.FindPropertyRelative("description").stringValue = source.description;
        }

        totemSO.FindProperty("auraColor").colorValue = GemColour(spec);

        totemSO.ApplyModifiedProperties();

        ApplyGem(root, spec);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);

        Debug.Log($"Card sheet: {(exists ? "updated" : "created")} {path}");
        return saved;
    }

    /// The wash this totem's gem colour implies, or the pack's blue with a shout if the spec named a
    /// colour the pack does not have - a typo should cost you a log line, not a silently black aura.
    private static Color GemColour(TotemSpec spec)
    {
        if (string.IsNullOrEmpty(spec.gemColour)) { return GemColours["BLUE"]; }

        if (GemColours.TryGetValue(spec.gemColour, out Color colour)) { return colour; }

        Debug.LogError($"Card sheet: {spec.displayName} asks for gem colour '{spec.gemColour}', "
                        + "which the karsiori pack does not have.");
        return GemColours["BLUE"];
    }

    // ------------------------------------------------------------------------------------------
    // Retiring content
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Everything each retired totem owns, grouped so a totem's card, prefab and summon effect go
    /// together and nothing is left pointing at half of one.
    /// </summary>
    private static readonly string[][] RetiredTotems =
    {
        // Mechanically identical to Sap Totem: same status, same magnitude, same radius, differing
        // only in aura colour. Sap is the one that stays.
        new[]
        {
            $"{CardRoot}/Mage/Summon/WeakenTotem.asset",
            $"{TotemFolder}/WeakenTotem.prefab",
            $"{EffectRoot}/Summon/SummonWeakenTotem.asset",
        },

        // Never worked: empty effectEntries, and neither a DrawTotem prefab nor a SummonDrawTotem
        // effect was ever authored behind it, so the card was playable and did nothing. Scholar and
        // Conduit Totem cover draw properly now.
        new[]
        {
            $"{CardRoot}/Mage/Summon/SummonDrawTotem.asset",
        },
    };

    /// <summary>
    /// Deletes the retired totems and clears the holes that leaves behind.
    ///
    /// Its own menu item rather than a step inside Generate(), because it is the one irreversible thing
    /// in this file: Generate() only ever adds or repairs, and running it should never be able to cost
    /// you an asset. Both of these are referenced by AllCards, and Draw Totem is in the MageTest deck
    /// as well, so the sweep afterwards is not optional.
    /// </summary>
    [MenuItem("Tools/Cards/Delete Retired Totems")]
    public static void DeleteRetiredTotems()
    {
        int deleted = 0;

        foreach (string[] group in RetiredTotems)
        {
            foreach (string path in group)
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
                {
                    Debug.Log($"Card sheet: nothing at {path} - already gone.");
                    continue;
                }

                if (AssetDatabase.DeleteAsset(path))
                {
                    deleted++;
                    Debug.Log($"Card sheet: deleted {path}");
                }
                else
                {
                    Debug.LogError($"Card sheet: could not delete {path}.");
                }
            }
        }

        int cleared = SweepNullCards("t:CardLibrary") + SweepNullCards("t:DeckData");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Card sheet: retired {deleted} asset(s), cleared {cleared} dangling reference(s).");
    }

    /// <summary>
    /// Drops every hole a deletion just punched in a `cards` list.
    ///
    /// Deliberately generic - it removes *any* null entry from every asset of the given type rather
    /// than hunting the two paths above. A null in a deck is a bug whatever put it there, so this stays
    /// correct when somebody deletes a card by hand in the Project window instead.
    ///
    /// Walks backwards because DeleteArrayElementAtIndex shifts everything after it down. That call
    /// also has a quirk worth knowing: on an object-reference element that is *not* null it nulls the
    /// element rather than removing it, and only a second call removes it. Only ever calling it on
    /// already-null elements is what makes one call the right number here.
    /// </summary>
    private static int SweepNullCards(string filter)
    {
        int cleared = 0;

        foreach (string guid in AssetDatabase.FindAssets(filter))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

            if (asset == null) { continue; }

            SerializedObject so = new(asset);
            SerializedProperty list = so.FindProperty("cards");

            if (list == null || !list.isArray) { continue; }

            int before = list.arraySize;

            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    list.DeleteArrayElementAtIndex(i);
                }
            }

            if (list.arraySize == before) { continue; }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);

            cleared += before - list.arraySize;
            Debug.Log($"Card sheet: cleared {before - list.arraySize} dangling card(s) from {path}");
        }

        return cleared;
    }

    // ------------------------------------------------------------------------------------------
    // Gem art
    // ------------------------------------------------------------------------------------------

    private const string GemRoot = "Assets/Extra Assets/karsiori/Sprites";
    private const string GemClipFolder = "Assets/Animations/Gems";

    /// Frames per second every gem idles at. Slow enough that ten totems on one board do not read as
    /// static noise, fast enough that the shimmer is legible at the size a tile gives it.
    private const float GemFrameRate = 12f;

    /// <summary>
    /// Drawn height, in world units, that every gem is normalised to.
    ///
    /// The pack's frames run from 22px tall to 44px, and its import settings are inconsistent on top of
    /// that - 192 of the sprites sit at 25 pixels-per-unit and the rest at 100. Normalising to a height
    /// makes both differences vanish, so shape reads as meaning rather than as scale. ApplyGem derives
    /// the scale from each sprite's own pixelsPerUnit, which is what absorbs the 25-vs-100 split.
    ///
    /// 0.62 is 20% off 0.78, which was itself 40% off the 1.3 this started at - the shield prop's own
    /// drawn height. The prop was a character-sized body standing on the tile; a gem is a small object
    /// hovering over it.
    /// </summary>
    private const float GemHeightWorld = 0.62f;

    /// <summary>
    /// Where the middle of the gem hovers, relative to the totem's own origin.
    ///
    /// A centre rather than the ground line this used to be measured from: the gem floats now, so there
    /// is no contact point to anchor to, and the bob below is a sine around exactly this value.
    /// </summary>
    private const float GemCentreY = 0.12f;

    /// How far the gem drifts either side of GemCentreY. Small on purpose - this should read as the gem
    /// being weightless, not as it being thrown.
    private const float GemBobHeight = 0.05f;

    /// Seconds for one full up-and-down. Slow enough to be felt rather than watched.
    private const float GemBobPeriod = 1.6f;

    /// The layer the bob lives on, and the clip every gem shares for it.
    private const string BobLayerName = "Bob";
    private const string BobClipName = "GemBob";

    /// <summary>
    /// Points this totem's art at its gem: the first frame on the SpriteRenderer, a looping clip on the
    /// Animator, and a transform scaled so every gem draws the same height.
    ///
    /// The SpriteRenderer lives on the "Sprite" child while the Animator is on the root, which is why
    /// the clip's curve path is "Sprite" and not empty - see EnsureGemController.
    ///
    /// A missing gem folder is reported and skipped rather than throwing: the rest of the prefab is
    /// still worth writing, and leaving the previous sprite in place is a better failure than a totem
    /// with no art at all.
    /// </summary>
    private static void ApplyGem(GameObject root, TotemSpec spec)
    {
        if (spec.gem <= 0 || string.IsNullOrEmpty(spec.gemColour))
        {
            // Not silent, because the fallback is no longer harmless: TotemPrefabSource is Totem.prefab,
            // which is itself a specced totem now and so carries gem 10 turquoise. A new spec that
            // forgot its gem would inherit the Shield Totem's art and look like it had been authored
            // that way rather than like something missing.
            Debug.LogError($"Card sheet: {spec.displayName} names no gem, so it will wear whatever "
                            + "Totem.prefab is currently carrying. Give the spec a gem and gemColour.");
            return;
        }

        Sprite[] frames = LoadGemFrames(spec.gem, spec.gemColour);
        if (frames == null || frames.Length == 0) { return; }

        Transform art = root.transform.Find("Sprite");
        if (art == null)
        {
            Debug.LogError($"Card sheet: {spec.displayName} has no 'Sprite' child to put its gem on.");
            return;
        }

        Sprite first = frames[0];
        float pixelsPerUnit = first.pixelsPerUnit;
        float nativeHeight = first.rect.height / pixelsPerUnit;
        float nativeWidth = first.rect.width / pixelsPerUnit;
        float scale = nativeHeight > 0f ? GemHeightWorld / nativeHeight : 1f;

        SpriteRenderer renderer = art.GetComponent<SpriteRenderer>();
        if (renderer != null) { renderer.sprite = first; }

        // Gem pivots are centred, so the sprite's own middle is what GemCentreY positions. The bob clip
        // animates this same y at runtime; setting it here is what makes the prefab look right in the
        // Project window and at the instant it is summoned, before the Animator's first update.
        art.localPosition = new Vector3(0f, GemCentreY, 0f);
        art.localScale = new Vector3(scale, scale, 1f);

        // TotemTooltip's collider exists to make the art hoverable and forwards its clicks to the tile
        // beneath, so it has to follow the art it is standing in for - see that class's own doc. Tall
        // enough to cover the whole bob, because a hitbox that drifts out from under the cursor while
        // you are reading the tooltip is worse than one slightly larger than the art.
        BoxCollider2D hitbox = root.GetComponent<BoxCollider2D>();
        if (hitbox != null)
        {
            hitbox.size = new Vector2(nativeWidth * scale, GemHeightWorld + (GemBobHeight * 2f));
            hitbox.offset = new Vector2(0f, GemCentreY);
        }

        Animator animator = root.GetComponent<Animator>();
        if (animator != null) { animator.runtimeAnimatorController = EnsureGemController(spec, frames); }
    }

    /// <summary>
    /// Every frame of one gem, in order.
    ///
    /// Globbed rather than built from a filename pattern, deliberately: GEM 6's gold frames are named
    /// "GEM - 6 - GOLD - 0000.png", with an extra dash after GEM that no other file in the pack has, so
    /// any $"GEM {n} - {colour} - ..." pattern silently finds nothing for exactly one of the eighty
    /// combinations. Ordinal sort then puts them in frame order, which the pack's zero-padded four-digit
    /// suffixes make correct.
    ///
    /// These are the individual frame PNGs, not the "- Spritesheet.png" files beside them. The frames
    /// are already imported one-sprite-per-file at PPU 25 with point filtering; the sheets are single
    /// sprites at PPU 100 and would need slicing, which on Unity 6 means the removed
    /// TextureImporter.spritesheet API. Nothing here slices anything.
    /// </summary>
    private static Sprite[] LoadGemFrames(int gem, string colour)
    {
        string folder = $"{GemRoot}/GEM {gem}/{colour}";

        if (!AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogError($"Card sheet: no gem frames at {folder}.");
            return null;
        }

        string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { folder });
        List<string> paths = new();

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);

            // FindAssets searches subfolders too; this pack has none, but a stray import elsewhere
            // under the tree should not end up in the middle of an animation.
            if (Path.GetDirectoryName(assetPath).Replace('\\', '/') == folder) { paths.Add(assetPath); }
        }

        paths.Sort(StringComparer.Ordinal);

        List<Sprite> frames = new();
        foreach (string assetPath in paths)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite != null) { frames.Add(sprite); }
        }

        if (frames.Count == 0) { Debug.LogError($"Card sheet: {folder} holds no sprites."); }

        return frames.ToArray();
    }

    /// <summary>
    /// The looping controller for one gem shape and colour, built once and shared by every totem
    /// wearing it - Bastion and Wellspring are both gem 1 turquoise, and there is no reason for them to
    /// own two identical clips.
    ///
    /// Repairs rather than skips, the same rule CreateTotemPrefab follows: an existing clip has its
    /// curve and loop flag rewritten, and an existing controller has its state re-pointed at the clip,
    /// so a change to GemFrameRate or to the pack's contents reaches disk on the next run.
    /// </summary>
    private static AnimatorController EnsureGemController(TotemSpec spec, Sprite[] frames)
    {
        EnsureFolder(GemClipFolder);

        string key = $"Gem{spec.gem}-{spec.gemColour.Replace(' ', '-')}";
        string clipPath = $"{GemClipFolder}/{key}.anim";
        string controllerPath = $"{GemClipFolder}/{key}.controller";

        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, clipPath);
        }

        clip.frameRate = GemFrameRate;

        // "Sprite", not "": the Animator sits on the prefab root and the SpriteRenderer on its child,
        // so a curve bound to the empty path would target a component the root does not have.
        EditorCurveBinding binding =
            EditorCurveBinding.PPtrCurve("Sprite", typeof(SpriteRenderer), "m_Sprite");

        ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            keys[i] = new ObjectReferenceKeyframe { time = i / GemFrameRate, value = frames[i] };
        }

        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);

        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);

        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(controllerPath, clip);
        }
        else
        {
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            if (machine.states.Length > 0) { machine.states[0].state.motion = clip; }
            else { machine.defaultState = machine.AddState("Idle"); machine.defaultState.motion = clip; }

            machine.defaultState.speed = 1f;
        }

        EnsureBobLayer(controller, EnsureBobClip());
        EditorUtility.SetDirty(controller);

        return controller;
    }

    /// <summary>
    /// The hover every gem shares - one clip, reused by all 33 controllers, because the motion is
    /// identical for all of them and GemCentreY is a constant.
    ///
    /// Absolute values rather than an additive curve: the layer is an Override, so this *is* the gem's
    /// y while it plays, which is why the sine is centred on GemCentreY rather than on zero.
    /// </summary>
    private static AnimationClip EnsureBobClip()
    {
        string path = $"{GemClipFolder}/{BobClipName}.anim";

        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }

        clip.frameRate = GemFrameRate;

        // Eight keys a cycle, smoothed - enough that the eye reads a sine rather than a bounce, and the
        // first and last key hold the same value so the loop has no seam.
        const int keysPerCycle = 8;
        Keyframe[] keys = new Keyframe[keysPerCycle + 1];

        for (int i = 0; i <= keysPerCycle; i++)
        {
            float time = GemBobPeriod * i / keysPerCycle;
            float offset = Mathf.Sin(2f * Mathf.PI * i / keysPerCycle) * GemBobHeight;
            keys[i] = new Keyframe(time, GemCentreY + offset);
        }

        AnimationCurve curve = new(keys);
        for (int i = 0; i < curve.length; i++) { curve.SmoothTangents(i, 0f); }

        AnimationUtility.SetEditorCurve(clip,
            EditorCurveBinding.FloatCurve("Sprite", typeof(Transform), "m_LocalPosition.y"), curve);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);

        return clip;
    }

    /// <summary>
    /// Puts the shared bob on its own layer above the gem's sprite loop.
    ///
    /// A second layer rather than a second curve in the sprite clip, because the two loops have nothing
    /// to do with each other. A sprite clip's length is however many frames that gem happens to have
    /// been drawn with - 10 for gem 6, 81 for gem 10 - so folding the bob in would make one gem hover
    /// eight times slower than another purely by accident of the art. On its own layer the bob runs at
    /// GemBobPeriod for every gem.
    ///
    /// Safe as an Override layer despite the name: layer 0 animates m_Sprite and this animates
    /// m_LocalPosition.y, so there is no property for the override to take away.
    /// </summary>
    private static void EnsureBobLayer(AnimatorController controller, AnimationClip bob)
    {
        if (controller.layers.Length < 2) { controller.AddLayer(BobLayerName); }

        // controller.layers hands back a copy, so weight and blending have to be assigned back through
        // the property rather than set on the element in place.
        AnimatorControllerLayer[] layers = controller.layers;
        layers[1].name = BobLayerName;
        layers[1].defaultWeight = 1f;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        controller.layers = layers;

        AnimatorStateMachine machine = controller.layers[1].stateMachine;

        if (machine.states.Length > 0)
        {
            machine.states[0].state.motion = bob;
            machine.defaultState = machine.states[0].state;
        }
        else
        {
            AnimatorState state = machine.AddState(BobLayerName);
            state.motion = bob;
            machine.defaultState = state;
        }

        // Written rather than left at its default, so every gem is guaranteed to bob in exactly
        // GemBobPeriod. State speed is a multiplier on clip length, so one state left at 2 somewhere
        // would give that one gem a half-length hover while sharing the very clip that is supposed to
        // make them identical - and it would survive every re-run that did not rewrite it.
        machine.defaultState.speed = 1f;
    }

    /// <summary>
    /// Rewrites just the totem prefabs from BuildTotemSpecs, without touching any card asset - the
    /// narrow half of Generate() above. Generate() also regenerates cards, which would be far more
    /// churn than intended for a totem-only tuning pass (a radius change, a recolour, a new gem); this
    /// is what running just that pass again should call.
    ///
    /// Reaction effects are still built, because a reaction totem's prefab holds a live reference to
    /// one and rewriting the prefab without them would blank it - they are all LoadOrCreate'd from
    /// existing assets, so this costs nothing on a repeat run.
    /// </summary>
    [MenuItem("Tools/Cards/Regenerate Totem Prefabs")]
    public static void RegenerateTotemPrefabs()
    {
        Dictionary<string, CardEffect> reactionEffects = BuildReactionEffects();

        foreach (TotemSpec spec in BuildTotemSpecs())
        {
            CreateTotemPrefab(spec, reactionEffects);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Card sheet: totem prefab regeneration complete.");
    }

    // ------------------------------------------------------------------------------------------
    // Effect assets
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The effects a reaction totem fires, which is everything CreateTotemPrefab needs before any
    /// prefab is written.
    ///
    /// Separate from BuildEffects below because the dependency runs both ways otherwise: a summon
    /// effect needs its totem prefab, while a reaction totem's prefab needs its effect. Splitting the
    /// half that depends on nothing lets both be built in dependency order and passed down as live
    /// objects, which is the same discipline that keeps AssetDatabase.StartAssetEditing out of this
    /// file - see the class doc.
    ///
    /// All but one are already authored by hand and simply looked up; Strength 1 is the only status
    /// magnitude nothing else in the game needed yet.
    /// </summary>
    private static Dictionary<string, CardEffect> BuildReactionEffects()
    {
        Dictionary<string, CardEffect> effects = new();

        AddExisting(effects, "Damage 2", $"{EffectRoot}/Damage/Damage 2.asset");
        AddExisting(effects, "Damage 3", $"{EffectRoot}/Damage/Damage 3.asset");
        AddExisting(effects, "Heal 3", $"{EffectRoot}/Heal/Heal 3.asset");
        AddExisting(effects, "Shield 3", $"{EffectRoot}/Shield/Shield 3.asset");
        AddExisting(effects, "Shield 5", $"{EffectRoot}/Shield/Shield 5.asset");
        AddExisting(effects, "Draw 1", $"{EffectRoot}/Draw/Draw 1.asset");

        effects["Energy 1"] = CreateEnergyEffect($"{EffectRoot}/Energy/Energy 1.asset", 1);
        effects["Strength 1"] = CreateStatusEffect(
            $"{EffectRoot}/Status/Strength 1.asset", StatusType.Strength, 1, alliesOnly: true);

        return effects;
    }

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
            cards.Add(new CardSpec
            {
                // Both read straight off the spec now. They used to be derived from the audience -
                // Allies meant Knight, Enemies meant Mage - which could not express Cleric or Rogue at
                // all, and left ClericContentGenerator moving six finished cards between folders.
                folder = $"{ClassFolder(spec.requiredClass)}/Summon",
                requiredClass = spec.requiredClass,
                cardName = spec.displayName,
                cost = spec.cost,
                rangeShape = RangeShape.Chebyshev,
                rangeMin = 1,
                rangeMax = 3,
                rarity = spec.rarity,
                description = $"Summon a {spec.displayName}.",
                entries = new List<EntrySpec>
                {
                    new($"Summon{spec.fileName}", EffectTarget.PlayedTile),
                },
            });
        }

        return cards;
    }

    /// CharacterClass is a [Flags] enum, so a totem naming two classes has no one folder to live in.
    /// No spec does, and this shouts rather than guessing if one ever starts.
    private static string ClassFolder(CharacterClass requiredClass) => requiredClass switch
    {
        CharacterClass.Knight => "Knight",
        CharacterClass.Mage => "Mage",
        CharacterClass.Rogue => "Rogue",
        CharacterClass.Cleric => "Cleric",
        _ => throw new ArgumentOutOfRangeException(nameof(requiredClass), requiredClass,
            "A totem card needs exactly one owning class."),
    };

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
            // Written in token form from the start, unlike the three above - those predate
            // Glossary.SummonContent reading a totem's own fields and are repaired by
            // UpdateMetaAuraGlossaryBodies instead.
            new GlossaryRow
            {
                type = StatusType.FlatDamageDealt, title = "HONED",
                body = "All {targets} in range deal {amount} more damage.",
            },
            new GlossaryRow
            {
                type = StatusType.FlatDamageTaken, title = "SHELTERED",
                body = "All {targets} in range take {amount} less damage.",
            },
            new GlossaryRow
            {
                type = StatusType.GainBonus, title = "BOLSTERED",
                body = "Adds {amount} to {status} granted to all {targets} in range.",
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
            [StatusType.FlatDamageDealt] = "All {targets} in range deal {amount} more damage.",
            [StatusType.FlatDamageTaken] = "All {targets} in range take {amount} less damage.",
            [StatusType.GainBonus] = "Adds {amount} to {status} granted to all {targets} in range.",
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
