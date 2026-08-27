using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One Animator state: what it is called in our own vocabulary, which AnimationCue reaches it, and
/// where its frames come from.
///
/// Two sources, because the ten art packs disagree about what they ship. A pack with per-action
/// spritesheets (Idle.png -> sub-sprites Idle_0..7) is *built* from those sheets. A pack whose frames
/// live in one index-only mega-sheet (Black_Knight_All_Frame_With_Border_0..323) ships hand-made .anim
/// clips instead, and those are *reused* verbatim - reconstructing which of 324 frames make up an
/// attack would be guesswork where the artist already answered it. Every shipped clip in every such
/// pack binds m_Sprite on path "" (the root), which is exactly where our SpriteRenderer sits, so a
/// reused clip drops straight onto our prefab.
/// </summary>
public sealed class StateSpec
{
    /// Animator state name. This is what CharacterAnimator's cue table points at, so it stays in our
    /// vocabulary (Idle / MeleeAttack / Hurt / Die / Move) whatever the underlying asset is called.
    public string state;

    /// Which cue plays this state. None for Idle - idle is the state everything *returns* to, named
    /// by CharacterAnimator.idleStateName rather than reached by a cue.
    public AnimationCue cue;

    /// Sheet file name without extension (reuse == false), or clip file name without extension
    /// (reuse == true). Resolved against the character's spriteFolder / clipFolder.
    public string source;

    /// True to reference a clip the pack already ships; false to build one from a sliced sheet.
    public bool reuse;

    /// Build only. The pack's own clips carry their own rate and are never re-timed.
    public float fps = 12f;

    public bool loop;
}

/// <summary>
/// One CardEffect asset to mint if it does not exist yet. Only the four magnitude-named kinds
/// (Damage 6, Heal 3, ...) are ever created here - every status, summon and tile effect this roster
/// uses already exists under Assets/Data/EffectData and is resolved by name.
/// </summary>
public sealed class EffectSpec
{
    public string name;
    public EffectKind kind;
    public int amount;
}

public enum EffectKind { Damage, Heal, Shield, Block }

/// A SummonEffect asset pointing at one of the prefabs this roster builds. Enemies are generated
/// before bosses precisely so these can resolve - a boss's summon card needs its minion to exist.
public sealed class SummonSpec
{
    public string name;

    /// A character name from this same roster.
    public string prefab;

    /// 0 = permanent. See SummonEffect.lifetimeTurns.
    public int lifetimeTurns;
}

/// One effect on one card: which effect asset, where it aims, and how wide it lands.
public sealed class EntrySpec
{
    public string effect;

    public EffectTarget aim = EffectTarget.PlayedTile;

    /// 0 is Single (one tile). Above 0 becomes a Chebyshev 0..radius footprint around the aim tile.
    public int radius;
}

/// <summary>
/// One enemy card. Always Rarity.NotOffered - these are dealt to enemies, never offered to a player,
/// which is the same gate EnemySlash and EnemyArrow already sit behind.
///
/// Worth knowing before authoring one: the brains only ever produce Attack, Move and Summon intents,
/// so a purely defensive card would never be played at all. Defence therefore rides along on an attack
/// card as a second, Source-aimed entry - "deal 5 damage, gain 8 shield" - rather than living on a
/// card of its own.
/// </summary>
public sealed class CardSpec
{
    public string name;
    public string cardName;
    public string description;
    public int cost = 1;
    public RangeShape shape = RangeShape.Chebyshev;
    public int min = 1;
    public int max = 1;
    public CardTag tag = CardTag.Attack;

    /// Turns before it may be played again. 0 authors no keyword at all.
    public int cooldown;

    public EntrySpec[] entries;
}

/// <summary>
/// One body: its identity, its stats, where its frames come from, and what it holds.
///
/// Scale is deliberately absent. Frame sizes across these packs run from 48px at 32 PPU to 250px at
/// 100 PPU, so twenty-five authored scale numbers would be twenty-five things to get wrong; the
/// generator measures the idle sprite and solves for `height` instead.
/// </summary>
public sealed class CharacterSpec
{
    public string name;
    public string displayName;
    public bool boss;

    /// Folder holding the sheets, for the pivot pass. Also where built clips read their frames from.
    public string spriteFolder;

    /// Folder holding the pack's own .anim files. Only needed when any state sets reuse.
    public string clipFolder;

    // -----------------------------------------------------------------------------------------
    // Seed stats. These are written ONLY when the generator creates a prefab for the first time -
    // from then on Docs/EnemyDesign owns them, through EnemySheetImporter, and re-running this tool
    // leaves them alone. See EnemyRosterGenerator.ApplyCharacter for the full ownership split.
    //
    // So expect these to drift from what is actually on the prefab, and do not balance here: a
    // number changed below reaches a body that already exists only if its prefab is deleted first.
    // battleRole, powerLevel and isBoss are the sheet's alone and have no counterpart here at all.
    // -----------------------------------------------------------------------------------------
    public int maxHealth = 10;
    public int actionPoints = 1;
    public BrainType brain = BrainType.Warrior;

    /// TargetingPattern asset name, or null for the default (always the most hurt legal target).
    public string targeting;

    /// True copies EnemyRanger as the template instead of SkeletonWarrior - it carries the
    /// RangedAttackAnchor child that CharacterAnimator.muzzle points at.
    public bool ranged;

    /// How tall this body should read on the board, in world units. Measured against the opaque
    /// artwork, not the frame canvas - see EnemyRosterGenerator.ApplyScale.
    public float height = EnemyRoster.EnemyHeight;

    /// How far to lift this body off its tile, as a fraction of its own height. 0 stands it on the
    /// ground, which is right for everything that walks; the ones that fly want a little air under
    /// them or they read as standing on the tile with their wings out.
    public float hover;

    /// The first entry is the idle state, by convention - it names CharacterAnimator.idleStateName and
    /// its first frame becomes the prefab's resting sprite.
    public StateSpec[] states;

    public (string card, int copies)[] deck;

    /// LootTable asset name, or null to inherit the level's.
    public string loot;
}

/// One boss loot table: better odds than the level's default, plus a signature card it always drops.
public sealed class LootSpec
{
    public string name;

    /// A player card, resolved by asset file name. Never an enemy card - guaranteedCards ignores both
    /// the rarity roll and excludeFromRewards, so a NotOffered card put here would really be offered.
    public string guaranteed;
}

/// One level to author from this roster.
public sealed class LevelSpec
{
    public string name;
    public Vector2Int boardSize;
    public int turnsToSurvive = 10;

    /// One-based cells, as typed in the Inspector. The generator subtracts one on the way in.
    public (string prefab, Vector2Int cell)[] enemies;

    public (int turn, string prefab, Vector2Int cell)[] waves;

    public Vector2Int[] partySpawnCells;
}

/// <summary>
/// One campaign: which levels, in order.
///
/// The generator owns this list outright rather than appending to it, so removing a level here really
/// removes it from the run. That is the point - it is what lets the debug run be rearranged without
/// hand-editing a .asset while the Editor holds the lockfile. Nothing else on the RunData is touched:
/// the party, the deck overrides and carryDamageBetweenLevels stay exactly as authored.
/// </summary>
public sealed class RunSpec
{
    public string name;

    /// LevelData asset names, in play order.
    public string[] levels;

    /// <summary>
    /// Another run to copy the starting party from, but only when this asset is being created for the
    /// first time - a party edited afterwards is never overwritten.
    ///
    /// Without it a freshly generated run has nobody in it, and opening Game.unity and pressing Play
    /// on it would build a board with no heroes. See RunData.StartingParty.
    /// </summary>
    public string seedPartyFrom;
}

/// <summary>
/// The roster as authored: every body, every card it holds, and every asset those cards need.
///
/// This file is data only. EnemyRosterGenerator turns it into prefabs, controllers, clips and assets,
/// and re-running it rewrites every field listed here - so tune numbers in the Inspector *after* the
/// last generator run, or lift the change back into this table.
///
/// That said, Health, Actions Per Turn, Brain, Targeting, Loot Table and Deck are now tuned from
/// Docs/EnemySheets.xlsx instead (Tools/EnemySheet, synced via Tools > Sync Enemies With Sheet) - this
/// table stays the place to build a NEW body, not the place to rebalance an existing one.
/// </summary>
public static class EnemyRoster
{
    // ---------------------------------------------------------------------------------------------
    // Where things go
    // ---------------------------------------------------------------------------------------------

    public const string EnemyPrefabFolder = "Assets/Prefabs/Enemies";
    public const string BossPrefabFolder = "Assets/Prefabs/Bosses";
    public const string AnimationFolder = "Assets/Animations";
    /// Parent of the per-body card folders. Each card is filed under the enemy that carries it -
    /// Assets/Data/CardData/Enemy/Goblin/Backstab.asset - so opening a folder shows one enemy's whole
    /// kit. A card carried by more than one body has no single owner and stays at this root, which is
    /// where the hand-authored MoveInnate, EnemySlash and EnemyArrow already live.
    public const string CardFolder = "Assets/Data/CardData/Enemy";
    public const string SummonEffectFolder = "Assets/Data/EffectData/Summon";
    public const string LootFolder = "Assets/Data/LootTable";
    public const string LevelFolder = "Assets/Data/LevelData";
    public const string RunFolder = "Assets/Data/RunData";

    /// Templates. The ranged one carries the RangedAttackAnchor child wired to CharacterAnimator.muzzle;
    /// copying either brings the whole overhead health-bar Canvas, the CharacterOverheadViewer wiring,
    /// the Board layer and the SortingGroup along with it.
    public const string MeleeTemplate = "Assets/Prefabs/Enemies/SkeletonWarrior.prefab";
    public const string RangedTemplate = "Assets/Prefabs/Enemies/EnemyRanger.prefab";

    /// <summary>
    /// Defaults for a body that does not state its own height. Every entry in the table below does
    /// state one, so these are only what a newly added body starts at.
    ///
    /// Deliberately not one shared height. These packs range from 11px to 104px of actual character,
    /// so a single number would blow a 14px crab up 16x and a 104px wizard only 3x - five times the
    /// difference in how chunky the pixels read. Heights are picked per body from the measured
    /// artwork instead, so a rat reads small and a boss reads large while source-pixel density stays
    /// inside a 3x band (24 to 65 px per world unit).
    ///
    /// For scale: the hand-authored SkeletonWarrior is 2.3 world units on a 2x1 iso cell, at 42 px
    /// per unit. This roster sits at about half that height, which puts most of it *finer* than the
    /// SkeletonWarrior rather than chunkier.
    ///
    /// Retune freely - the generator solves the transform scale, so height is the only number to
    /// touch, and it is measured against the opaque artwork rather than the frame canvas.
    /// </summary>
    public const float EnemyHeight = 0.9f;
    public const float BossHeight = 1.4f;

    // ---------------------------------------------------------------------------------------------
    // Pack roots. Every one of these is the only thing tying the roster to where the art lives, so
    // moving a pack breaks all 130 sheet and clip references at once - and the symptom is not an
    // error but every body silently falling back to the template's sprite and scale, which reads in
    // game as "the sizes reverted". If that happens, check here first.
    //
    // Four differ from how the packs are usually referred to: the folder on disk is "Enemy Galore 1 -
    // Pixel Art", "EVil Wizard" (capital V), "Bringer Of Death" (sprites under "Sprite Sheet") and
    // "Medieval King Pack".
    // ---------------------------------------------------------------------------------------------

    private const string Bandits = "Assets/Extra Assets/Bandits - Pixel Art";
    private const string Galore = "Assets/Extra Assets/Enemy Galore 1 - Pixel Art/Sprites";
    private const string Fantasy = "Assets/Extra Assets/Monsters Creatures Fantasy/Sprites";
    private const string Fantasy2 = "Assets/Extra Assets/Monsters Creatures Fantasy 2/Sprites";
    private const string BlackKnight = "Assets/Extra Assets/2D Pixel Art Black Knight";
    private const string Reaper = "Assets/Extra Assets/Bringer Of Death";
    private const string HeroKnight = "Assets/Extra Assets/Hero Knight - Pixel Art";
    private const string Wizard1 = "Assets/Extra Assets/EVil Wizard/Sprites";
    private const string Wizard2 = "Assets/Extra Assets/Evil Wizard 2/Sprites";
    private const string Wizard3 = "Assets/Extra Assets/Evil Wizard 3/Sprites";
    private const string King = "Assets/Extra Assets/Medieval King Pack/Sprites";

    // ---------------------------------------------------------------------------------------------
    // Shorthand for a state row. S builds a clip from a sliced sheet; R reuses one the pack ships.
    // ---------------------------------------------------------------------------------------------

    private static StateSpec S(string state, AnimationCue cue, string sheet, bool loop = false,
                               float fps = 12f) =>
        new() { state = state, cue = cue, source = sheet, loop = loop, fps = fps };

    private static StateSpec R(string state, AnimationCue cue, string clip, bool loop = false) =>
        new() { state = state, cue = cue, source = clip, reuse = true, loop = loop };

    private static EntrySpec E(string effect, EffectTarget aim = EffectTarget.PlayedTile,
                               int radius = 0) =>
        new() { effect = effect, aim = aim, radius = radius };

    // ---------------------------------------------------------------------------------------------
    // Effect assets to mint if missing. Everything else these cards use - every status, the summons
    // below, Move - already exists under Assets/Data/EffectData.
    // ---------------------------------------------------------------------------------------------

    public static readonly EffectSpec[] Effects =
    {
        new() { name = "Damage 2", kind = EffectKind.Damage, amount = 2 },
        new() { name = "Damage 6", kind = EffectKind.Damage, amount = 6 },
        new() { name = "Damage 7", kind = EffectKind.Damage, amount = 7 },
        new() { name = "Damage 8", kind = EffectKind.Damage, amount = 8 },
        new() { name = "Heal 3", kind = EffectKind.Heal, amount = 3 },
        new() { name = "Shield 10", kind = EffectKind.Shield, amount = 10 },
        new() { name = "Block 4", kind = EffectKind.Block, amount = 4 },
    };

    /// Minions the bosses call in. Permanent (0 turns) on purpose - a boss whose adds evaporate on
    /// their own gives the player nothing to do about them.
    public static readonly SummonSpec[] Summons =
    {
        new() { name = "SummonSkeletonMinion", prefab = "Skeleton" },
        new() { name = "SummonPebbleMinion", prefab = "Pebble" },
        new() { name = "SummonGoblinMinion", prefab = "Goblin" },
        new() { name = "SummonBatMinion", prefab = "Bat" },
        new() { name = "SummonSlimeMinion", prefab = "Slime" },
        new() { name = "SummonHeavyBanditMinion", prefab = "HeavyBandit" },
        new() { name = "SummonLightBanditMinion", prefab = "LightBandit" },
    };

    // ---------------------------------------------------------------------------------------------
    // Cards. One signature per enemy; three per boss - a reach attack, a defensive attack, a summon.
    //
    // Defence never gets a card of its own: the brains only produce Attack, Move and Summon intents,
    // so a card with no damage footprint and no SummonEffect is never chosen. Every Shield, Block and
    // Parry below therefore rides on an attack card as a second Source-aimed entry.
    // ---------------------------------------------------------------------------------------------

    public static readonly CardSpec[] Cards =
    {
        // --- Enemies -----------------------------------------------------------------------------

        new() { name = "HeavySwing", cardName = "Heavy Swing", description = "Deal 6 damage.",
                entries = new[] { E("Damage 6") } },

        new() { name = "QuickCut", cardName = "Quick Cut",
                description = "Deal 3 damage. Apply Weaken.",
                entries = new[] { E("Damage 3"), E("Weaken") } },

        new() { name = "DrainBite", cardName = "Drain Bite",
                description = "Deal 2 damage. Heal 3.",
                entries = new[] { E("Damage 2"), E("Heal 3", EffectTarget.Source) } },

        new() { name = "PincerGrip", cardName = "Pincer Grip",
                description = "Deal 4 damage. Gain 2 Block.",
                entries = new[] { E("Damage 4"), E("Block 2", EffectTarget.Source) } },

        new() { name = "BoulderSmash", cardName = "Boulder Smash", description = "Deal 5 damage.",
                entries = new[] { E("Damage 5") } },

        new() { name = "Roll", cardName = "Roll", description = "Deal 2 damage.",
                entries = new[] { E("Damage 2") } },

        new() { name = "Gnaw", cardName = "Gnaw", description = "Deal 2 damage. Apply 2 Poison.",
                tag = CardTag.Poison,
                entries = new[] { E("Damage 2"), E("Poison 2") } },

        new() { name = "BoneChill", cardName = "Bone Chill",
                description = "Deal 3 damage. Apply Vulnerable.", max = 3,
                entries = new[] { E("Damage 3"), E("Vulnerable") } },

        new() { name = "SpikeBurst", cardName = "Spike Burst",
                description = "Deal 3 damage to everything within 1 tile.",
                entries = new[] { E("Damage 3", radius: 1) } },

        new() { name = "WitheringGaze", cardName = "Withering Gaze", description = "Deal 3 damage.",
                min = 2, max = 4,
                entries = new[] { E("Damage 3") } },

        new() { name = "Backstab", cardName = "Backstab", description = "Deal 4 damage.",
                entries = new[] { E("Damage 4") } },

        new() { name = "SporeCloud", cardName = "Spore Cloud",
                description = "Apply 3 Poison to everything within 1 tile.", max = 3,
                tag = CardTag.Poison,
                entries = new[] { E("Poison 3", radius: 1) } },

        new() { name = "BoneBulwark", cardName = "Bone Bulwark",
                description = "Deal 3 damage. Gain 5 Shield.",
                entries = new[] { E("Damage 3"), E("Shield 5", EffectTarget.Source) } },

        new() { name = "LifeLeech", cardName = "Life Leech", description = "Deal 3 damage. Heal 3.",
                entries = new[] { E("Damage 3"), E("Heal 3", EffectTarget.Source) } },

        new() { name = "Chomp", cardName = "Chomp", description = "Deal 7 damage.",
                entries = new[] { E("Damage 7") } },

        new() { name = "PlagueBite", cardName = "Plague Bite",
                description = "Deal 2 damage. Apply 3 Poison.", tag = CardTag.Poison,
                entries = new[] { E("Damage 2"), E("Poison 3") } },

        new() { name = "Engulf", cardName = "Engulf", description = "Deal 3 damage. Apply Root.",
                entries = new[] { E("Damage 3"), E("Root") } },

        // --- Bosses ------------------------------------------------------------------------------

        new() { name = "DreadCleave", cardName = "Dread Cleave",
                description = "Deal 8 damage to everything within 1 tile.",
                entries = new[] { E("Damage 8", radius: 1) } },

        new() { name = "BlackGuard", cardName = "Black Guard",
                description = "Deal 5 damage. Gain 10 Shield.",
                entries = new[] { E("Damage 5"), E("Shield 10", EffectTarget.Source) } },

        new() { name = "CallTheDamned", cardName = "Call the Damned",
                description = "Summon a Skeleton.", cost = 0, max = 3, tag = CardTag.Summon,
                cooldown = 3,
                entries = new[] { E("SummonSkeletonMinion") } },

        new() { name = "SeismicSlam", cardName = "Seismic Slam",
                description = "Deal 7 damage to everything within 1 tile.",
                entries = new[] { E("Damage 7", radius: 1) } },

        new() { name = "StoneSkin", cardName = "Stone Skin",
                description = "Deal 4 damage. Gain 10 Shield.",
                entries = new[] { E("Damage 4"), E("Shield 10", EffectTarget.Source) } },

        new() { name = "SplinterOff", cardName = "Splinter Off",
                description = "Summon a Pebble.", cost = 0, max = 2, tag = CardTag.Summon,
                cooldown = 2,
                entries = new[] { E("SummonPebbleMinion") } },

        new() { name = "SoulReap", cardName = "Soul Reap",
                description = "Deal 6 damage. Heal 5.", max = 2,
                entries = new[] { E("Damage 6"), E("Heal 5", EffectTarget.Source) } },

        new() { name = "DeathsToll", cardName = "Death's Toll",
                description = "Deal 4 damage and apply Vulnerable within 1 tile.", max = 4,
                entries = new[] { E("Damage 4", radius: 1), E("Vulnerable", radius: 1) } },

        new() { name = "RaiseDead", cardName = "Raise Dead",
                description = "Summon a Skeleton.", cost = 0, max = 4, tag = CardTag.Summon,
                cooldown = 3,
                entries = new[] { E("SummonSkeletonMinion") } },

        new() { name = "DarkBolt", cardName = "Dark Bolt", description = "Deal 5 damage.",
                min = 2, max = 5,
                entries = new[] { E("Damage 5") } },

        new() { name = "ShadowWard", cardName = "Shadow Ward",
                description = "Deal 3 damage. Gain 7 Shield.", max = 4,
                entries = new[] { E("Damage 3"), E("Shield 7", EffectTarget.Source) } },

        new() { name = "SummonImp", cardName = "Summon Imp", description = "Summon a Goblin.",
                cost = 0, max = 3, tag = CardTag.Summon, cooldown = 3,
                entries = new[] { E("SummonGoblinMinion") } },

        new() { name = "ArcaneLance", cardName = "Arcane Lance", description = "Deal 6 damage.",
                min = 2, max = 5,
                entries = new[] { E("Damage 6") } },

        new() { name = "VoidBarrier", cardName = "Void Barrier",
                description = "Deal 3 damage. Gain 4 Block.", max = 4,
                entries = new[] { E("Damage 3"), E("Block 4", EffectTarget.Source) } },

        new() { name = "CallTheSwarm", cardName = "Call the Swarm", description = "Summon a Bat.",
                cost = 0, max = 3, tag = CardTag.Summon, cooldown = 2,
                entries = new[] { E("SummonBatMinion") } },

        new() { name = "MeteorFall", cardName = "Meteor Fall",
                description = "Deal 6 damage to everything within 1 tile.", min = 2, max = 5,
                tag = CardTag.Fire,
                entries = new[] { E("Damage 6", radius: 1) } },

        new() { name = "EmberShroud", cardName = "Ember Shroud",
                description = "Deal 4 damage. Gain 7 Shield.", max = 4, tag = CardTag.Fire,
                entries = new[] { E("Damage 4"), E("Shield 7", EffectTarget.Source) } },

        new() { name = "ConjureOoze", cardName = "Conjure Ooze", description = "Summon a Slime.",
                cost = 0, max = 3, tag = CardTag.Summon, cooldown = 3,
                entries = new[] { E("SummonSlimeMinion") } },

        new() { name = "Riposte", cardName = "Riposte",
                description = "Deal 6 damage. Gain 1 Parry.",
                entries = new[] { E("Damage 6"), E("Parry 1", EffectTarget.Source) } },

        new() { name = "ShieldBash", cardName = "Shield Bash",
                description = "Deal 4 damage. Gain 10 Shield.",
                entries = new[] { E("Damage 4"), E("Shield 10", EffectTarget.Source) } },

        new() { name = "Rally", cardName = "Rally", description = "Summon a Heavy Bandit.",
                cost = 0, max = 3, tag = CardTag.Summon, cooldown = 4,
                entries = new[] { E("SummonHeavyBanditMinion") } },

        new() { name = "ExecutionersBlow", cardName = "Executioner's Blow",
                description = "Deal 9 damage.",
                entries = new[] { E("Damage 9") } },

        new() { name = "RoyalGuard", cardName = "Royal Guard",
                description = "Deal 5 damage within 1 tile. Gain 10 Shield.",
                entries = new[] { E("Damage 5", radius: 1), E("Shield 10", EffectTarget.Source) } },

        new() { name = "RoyalCommand", cardName = "Royal Command",
                description = "Summon a Light Bandit.", cost = 0, max = 3, tag = CardTag.Summon,
                cooldown = 3,
                entries = new[] { E("SummonLightBanditMinion") } },
    };

    // ---------------------------------------------------------------------------------------------
    // The bodies. Enemies first: bosses summon them, so their prefabs have to exist before a boss
    // SummonEffect can point at one.
    //
    // Every deck carries MoveInnate. Without a Move card TryFindMove has nothing to offer and the body
    // stands where it spawned for the whole fight.
    // ---------------------------------------------------------------------------------------------

    public static readonly CharacterSpec[] Characters =
    {
        new()
        {
            name = "HeavyBandit", displayName = "Heavy Bandit",
            spriteFolder = Bandits + "/Sprites", clipFolder = Bandits + "/Animations/Heavy Bandit",
            maxHealth = 22, height = 0.95f, actionPoints = 1, targeting = "Strongest",
            states = new[]
            {
                R("Idle", AnimationCue.None, "HeavyBandit_Idle", loop: true),
                R("Move", AnimationCue.Move, "HeavyBandit_Run", loop: true),
                R("MeleeAttack", AnimationCue.MeleeAttack, "HeavyBandit_Attack"),
                R("Hurt", AnimationCue.Hurt, "HeavyBandit_Hurt"),
                R("Die", AnimationCue.Die, "HeavyBandit_Death"),
            },
            deck = new[] { ("HeavySwing", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "LightBandit", displayName = "Light Bandit",
            spriteFolder = Bandits + "/Sprites", clipFolder = Bandits + "/Animations/Light Bandit",
            maxHealth = 12, height = 0.9f, actionPoints = 2, targeting = "Weakest",
            states = new[]
            {
                R("Idle", AnimationCue.None, "LightBandit_Idle", loop: true),
                R("Move", AnimationCue.Move, "LightBandit_Run", loop: true),
                R("MeleeAttack", AnimationCue.MeleeAttack, "LightBandit_Attack"),
                R("Hurt", AnimationCue.Hurt, "LightBandit_Hurt"),
                R("Die", AnimationCue.Die, "LightBandit_Death"),
            },
            deck = new[] { ("QuickCut", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "Bat", displayName = "Bat",
            spriteFolder = Galore + "/Bat",
            maxHealth = 15, height = 0.55f, actionPoints = 2, targeting = "Weakest", hover = 0.3f,
            states = new[]
            {
                S("Idle", AnimationCue.None, "Bat_Fly", loop: true),
                S("Move", AnimationCue.Move, "Bat_Fly", loop: true),
                S("MeleeAttack", AnimationCue.MeleeAttack, "Bat_Attack"),
                S("Hurt", AnimationCue.Hurt, "Bat_Hit"),
                S("Die", AnimationCue.Die, "Bat_Death"),
            },
            deck = new[] { ("DrainBite", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "Crab", displayName = "Crab",
            spriteFolder = Galore + "/Crab",
            maxHealth = 18, height = 0.5f, actionPoints = 1, targeting = "Closest",
            states = new[]
            {
                S("Idle", AnimationCue.None, "Crab_Idle", loop: true),
                S("Move", AnimationCue.Move, "Crab_Run", loop: true),
                S("MeleeAttack", AnimationCue.MeleeAttack, "Crab_AttackA"),
                S("Hurt", AnimationCue.Hurt, "Crab_Hit"),
                S("Die", AnimationCue.Die, "Crab_Death"),
            },
            deck = new[] { ("PincerGrip", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "Golem", displayName = "Golem",
            spriteFolder = Galore + "/Golem",
            maxHealth = 30, height = 0.85f, actionPoints = 1, targeting = "Closest",
            states = new[]
            {
                S("Idle", AnimationCue.None, "Golem_IdleA", loop: true),
                S("Move", AnimationCue.Move, "Golem_Run", loop: true),
                S("MeleeAttack", AnimationCue.MeleeAttack, "Golem_AttackA"),
                S("Hurt", AnimationCue.Hurt, "Golem_HitA"),
                S("Die", AnimationCue.Die, "Golem_DeathA"),
            },
            deck = new[] { ("BoulderSmash", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "Pebble", displayName = "Pebble",
            spriteFolder = Galore + "/Pebble",
            maxHealth = 6, height = 0.4f, actionPoints = 1, targeting = "Closest",
            states = new[]
            {
                S("Idle", AnimationCue.None, "Pebble_Idle", loop: true),
                S("Move", AnimationCue.Move, "Pebble_Run", loop: true),
                // The pack ships no attack for this one - it rolls at you, so Run reads as the swing.
                S("MeleeAttack", AnimationCue.MeleeAttack, "Pebble_Run"),
                S("Hurt", AnimationCue.Hurt, "Pebble_Hit"),
                S("Die", AnimationCue.Die, "Pebble_Death"),
            },
            deck = new[] { ("Roll", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "Rat", displayName = "Rat",
            spriteFolder = Galore + "/Rat",
            maxHealth = 9, height = 0.425f, actionPoints = 2, targeting = "Weakest",
            states = new[]
            {
                S("Idle", AnimationCue.None, "Rat_Idle", loop: true),
                S("Move", AnimationCue.Move, "Rat_Run", loop: true),
                S("MeleeAttack", AnimationCue.MeleeAttack, "Rat_Attack"),
                S("Hurt", AnimationCue.Hurt, "Rat_Hit"),
                S("Die", AnimationCue.Die, "Rat_Death"),
            },
            deck = new[] { ("Gnaw", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "Skull", displayName = "Skull",
            spriteFolder = Galore + "/Skull",
            maxHealth = 7, height = 0.45f, actionPoints = 1, targeting = "Random", hover = 0.25f,
            states = new[]
            {
                S("Idle", AnimationCue.None, "Bones_SingleSkull_Idle", loop: true),
                S("Move", AnimationCue.Move, "Bones_SingleSkull_Fly", loop: true),
                // No attack sheet either; the lunge it flies with is the closest thing to a swing.
                S("MeleeAttack", AnimationCue.MeleeAttack, "Bones_SingleSkull_Fly"),
                S("Hurt", AnimationCue.Hurt, "Bones_SingleSkull_Hit"),
                S("Die", AnimationCue.Die, "Bones_SingleSkull_Death"),
            },
            deck = new[] { ("BoneChill", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "SpikedSlime", displayName = "Spiked Slime",
            spriteFolder = Galore + "/Spiked Slime",
            maxHealth = 16, height = 0.6f, actionPoints = 1, targeting = "Closest",
            states = new[]
            {
                S("Idle", AnimationCue.None, "Slime_Spiked_Idle", loop: true),
                S("Move", AnimationCue.Move, "Slime_Spiked_Run", loop: true),
                S("MeleeAttack", AnimationCue.MeleeAttack, "Slime_Spiked_Ability"),
                S("Hurt", AnimationCue.Hurt, "Slime_Spiked_Hit"),
                S("Die", AnimationCue.Die, "Slime_Spiked_Death"),
            },
            deck = new[] { ("SpikeBurst", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "FlyingEye", displayName = "Flying Eye",
            spriteFolder = Fantasy + "/Flying eye",
            maxHealth = 10, height = 0.75f, actionPoints = 1, brain = BrainType.Ranger,
            targeting = "Furthest",
            ranged = true, hover = 0.35f,
            states = new[]
            {
                S("Idle", AnimationCue.None, "Flight", loop: true),
                S("Move", AnimationCue.Move, "Flight", loop: true),
                S("RangedAttack", AnimationCue.RangedAttack, "Attack1"),
                S("Hurt", AnimationCue.Hurt, "Take Hit"),
                S("Die", AnimationCue.Die, "Death"),
            },
            deck = new[] { ("WitheringGaze", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "Goblin", displayName = "Goblin",
            spriteFolder = Fantasy + "/Goblin",
            maxHealth = 14, height = 0.85f, actionPoints = 2, targeting = "Weakest",
            states = new[]
            {
                S("Idle", AnimationCue.None, "Idle", loop: true),
                S("Move", AnimationCue.Move, "Run", loop: true),
                S("MeleeAttack", AnimationCue.MeleeAttack, "Attack1"),
                S("Hurt", AnimationCue.Hurt, "Take Hit"),
                S("Die", AnimationCue.Die, "Death"),
            },
            deck = new[] { ("Backstab", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "Mushroom", displayName = "Mushroom",
            spriteFolder = Fantasy + "/Mushroom",
            maxHealth = 13, height = 0.85f, actionPoints = 1, brain = BrainType.Ranger,
            targeting = "Closest",
            ranged = true, loot = "PoisonSkeleton",
            states = new[]
            {
                S("Idle", AnimationCue.None, "Idle", loop: true),
                S("Move", AnimationCue.Move, "Run", loop: true),
                S("RangedAttack", AnimationCue.RangedAttack, "Attack1"),
                S("Hurt", AnimationCue.Hurt, "Take Hit"),
                S("Die", AnimationCue.Die, "Death"),
            },
            deck = new[] { ("SporeCloud", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "Skeleton", displayName = "Skeleton",
            spriteFolder = Fantasy + "/Skeleton",
            maxHealth = 16, height = 1.05f, actionPoints = 1, targeting = "Closest",
            states = new[]
            {
                S("Idle", AnimationCue.None, "Idle", loop: true),
                S("Move", AnimationCue.Move, "Walk", loop: true),
                S("MeleeAttack", AnimationCue.MeleeAttack, "Attack1"),
                S("Cast", AnimationCue.Cast, "Shield"),
                S("Hurt", AnimationCue.Hurt, "Take Hit"),
                S("Die", AnimationCue.Die, "Death"),
            },
            deck = new[] { ("BoneBulwark", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "VampireBat", displayName = "Vampire Bat",
            spriteFolder = Fantasy2 + "/Bat",
            maxHealth = 9, height = 0.85f, actionPoints = 2, targeting = "Weakest", hover = 0.3f,
            states = new[]
            {
                S("Idle", AnimationCue.None, "fly", loop: true),
                S("Move", AnimationCue.Move, "fly", loop: true),
                S("MeleeAttack", AnimationCue.MeleeAttack, "attack"),
                S("Hurt", AnimationCue.Hurt, "hurt"),
                S("Die", AnimationCue.Die, "death"),
            },
            deck = new[] { ("LifeLeech", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "Mimic", displayName = "Mimic",
            spriteFolder = Fantasy2 + "/Mimic",
            maxHealth = 26, height = 0.75f, actionPoints = 1, targeting = "Closest",
            states = new[]
            {
                S("Idle", AnimationCue.None, "idle_transformed", loop: true),
                S("Move", AnimationCue.Move, "walk", loop: true),
                S("MeleeAttack", AnimationCue.MeleeAttack, "attack_1"),
                S("Hurt", AnimationCue.Hurt, "hurt"),
                S("Die", AnimationCue.Die, "death"),
            },
            deck = new[] { ("Chomp", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "PlagueRat", displayName = "Plague Rat",
            spriteFolder = Fantasy2 + "/Rat",
            maxHealth = 11, height = 0.5f, actionPoints = 2, targeting = "Weakest",
            loot = "PoisonSkeleton",
            states = new[]
            {
                S("Idle", AnimationCue.None, "idle", loop: true),
                S("Move", AnimationCue.Move, "run", loop: true),
                S("MeleeAttack", AnimationCue.MeleeAttack, "attack_bite"),
                S("Hurt", AnimationCue.Hurt, "hurt"),
                S("Die", AnimationCue.Die, "rat-death"),
            },
            deck = new[] { ("PlagueBite", 3), ("MoveInnate", 2) },
        },

        new()
        {
            name = "Slime", displayName = "Slime",
            spriteFolder = Fantasy2 + "/Slime",
            maxHealth = 20, height = 0.65f, actionPoints = 1, targeting = "Closest",
            states = new[]
            {
                S("Idle", AnimationCue.None, "idle", loop: true),
                S("Move", AnimationCue.Move, "walk", loop: true),
                S("MeleeAttack", AnimationCue.MeleeAttack, "attack"),
                S("Hurt", AnimationCue.Hurt, "hurt"),
                S("Die", AnimationCue.Die, "death"),
            },
            deck = new[] { ("Engulf", 3), ("MoveInnate", 2) },
        },

        // --- Bosses ------------------------------------------------------------------------------
        //
        // Every one runs BrainType.Summoner, which asks TryFindSummon before TryFindAttack. Ordered
        // the other way round - the way WarriorBrain does it - a boss holding both an attack and a
        // summon would attack every round and never call anything in.
        //
        // Durability is health plus Shield riding on their attack cards, not a rule. There is no
        // invulnerability status anywhere in this, so a boss is killable in principle and simply not
        // worth trying to out-damage inside one level.

        new()
        {
            name = "BlackKnight", displayName = "Black Knight", boss = true,
            spriteFolder = BlackKnight + "/Sprites",
            clipFolder = BlackKnight + "/Animations/Black_Knight",
            maxHealth = 120, height = 1.3f, actionPoints = 2, brain = BrainType.Summoner,
            targeting = "Strongest", loot = "BlackKnightDrop",
            states = new[]
            {
                R("Idle", AnimationCue.None, "BK_weapon_idle", loop: true),
                R("Move", AnimationCue.Move, "BK_weapon_run", loop: true),
                R("MeleeAttack", AnimationCue.MeleeAttack, "BK_heavy_attack_1"),
                R("Cast", AnimationCue.Cast, "BK_weapon_buff"),
                R("Summon", AnimationCue.Summon, "BK_weapon_art"),
                R("Hurt", AnimationCue.Hurt, "BK_weapon_hurt"),
                R("Die", AnimationCue.Die, "BK_death"),
            },
            deck = new[] { ("DreadCleave", 2), ("BlackGuard", 2), ("CallTheDamned", 1),
                           ("MoveInnate", 2) },
        },

        new()
        {
            name = "ReinforcedGolem", displayName = "Reinforced Golem", boss = true,
            spriteFolder = Galore + "/Reinforced Golem",
            maxHealth = 150, height = 1.1f, actionPoints = 2, brain = BrainType.Summoner,
            targeting = "Closest", loot = "ReinforcedGolemDrop",
            states = new[]
            {
                S("Idle", AnimationCue.None, "Golem_Armor_Idle", loop: true),
                S("Move", AnimationCue.Move, "Golem_Armor_Run", loop: true),
                S("MeleeAttack", AnimationCue.MeleeAttack, "Golem_Armor_AttackA"),
                S("Summon", AnimationCue.Summon, "Golem_Armor_Ability"),
                S("Hurt", AnimationCue.Hurt, "Golem_Armor_Hit"),
                // The pack ships no death sheet for the armoured phase - the armour shattering is the
                // closest thing it has, and reads better on this boss than a plain fade would.
                S("Die", AnimationCue.Die, "Golem_Armor_ArmorBreak"),
            },
            deck = new[] { ("SeismicSlam", 2), ("StoneSkin", 2), ("SplinterOff", 1),
                           ("MoveInnate", 2) },
        },

        new()
        {
            name = "Reaper", displayName = "Reaper", boss = true,
            spriteFolder = Reaper + "/Sprite Sheet", clipFolder = Reaper + "/Animation",
            maxHealth = 110, height = 1.45f, actionPoints = 2, brain = BrainType.Summoner,
            targeting = "Weakest", loot = "ReaperDrop",
            states = new[]
            {
                R("Idle", AnimationCue.None, "Idle", loop: true),
                R("Move", AnimationCue.Move, "Walk", loop: true),
                R("MeleeAttack", AnimationCue.MeleeAttack, "Attack"),
                R("Cast", AnimationCue.Cast, "Cast"),
                R("Summon", AnimationCue.Summon, "Spell"),
                R("Hurt", AnimationCue.Hurt, "Hurt"),
                R("Die", AnimationCue.Die, "Death"),
            },
            deck = new[] { ("SoulReap", 2), ("DeathsToll", 2), ("RaiseDead", 1),
                           ("MoveInnate", 2) },
        },

        new()
        {
            name = "EvilWizard", displayName = "Evil Wizard", boss = true,
            spriteFolder = Wizard1,
            maxHealth = 90, height = 1.4f, actionPoints = 2, brain = BrainType.Summoner,
            targeting = "Weakest",
            ranged = true, loot = "EvilWizardDrop",
            states = new[]
            {
                S("Idle", AnimationCue.None, "Idle", loop: true),
                S("Move", AnimationCue.Move, "Move", loop: true),
                S("RangedAttack", AnimationCue.RangedAttack, "Attack"),
                S("Cast", AnimationCue.Cast, "Attack"),
                S("Summon", AnimationCue.Summon, "Attack"),
                S("Hurt", AnimationCue.Hurt, "Take Hit"),
                S("Die", AnimationCue.Die, "Death"),
            },
            deck = new[] { ("DarkBolt", 2), ("ShadowWard", 2), ("SummonImp", 1),
                           ("MoveInnate", 2) },
        },

        new()
        {
            name = "EvilWizard2", displayName = "Dread Sorcerer", boss = true,
            spriteFolder = Wizard2,
            // Height is 40% above what the measurement alone would give it, and deliberately out of
            // band with the rest: this one carries a staff that stands well clear of its head, so the
            // 104px the generator measures is mostly prop. Sizing the staff to a boss's height left
            // the wizard inside it looking smaller than the enemies. Do not "correct" this back.
            maxHealth = 95, height = 2.24f, actionPoints = 2, brain = BrainType.Summoner,
            targeting = "Furthest",
            ranged = true, loot = "EvilWizard2Drop",
            states = new[]
            {
                S("Idle", AnimationCue.None, "Idle", loop: true),
                S("Move", AnimationCue.Move, "Run", loop: true),
                S("RangedAttack", AnimationCue.RangedAttack, "Attack1"),
                S("Cast", AnimationCue.Cast, "Attack2"),
                S("Summon", AnimationCue.Summon, "Attack2"),
                S("Hurt", AnimationCue.Hurt, "Take hit"),
                S("Die", AnimationCue.Die, "Death"),
            },
            deck = new[] { ("ArcaneLance", 2), ("VoidBarrier", 2), ("CallTheSwarm", 1),
                           ("MoveInnate", 2) },
        },

        new()
        {
            name = "EvilWizard3", displayName = "Emberlord", boss = true,
            spriteFolder = Wizard3,
            maxHealth = 100, height = 1.4f, actionPoints = 2, brain = BrainType.Summoner,
            targeting = "Strongest",
            ranged = true, loot = "EvilWizard3Drop",
            states = new[]
            {
                S("Idle", AnimationCue.None, "Idle", loop: true),
                S("Move", AnimationCue.Move, "Run", loop: true),
                S("RangedAttack", AnimationCue.RangedAttack, "Attack"),
                S("Cast", AnimationCue.Cast, "Attack"),
                S("Summon", AnimationCue.Summon, "Attack"),
                S("Hurt", AnimationCue.Hurt, "Get hit"),
                S("Die", AnimationCue.Die, "Death"),
            },
            deck = new[] { ("MeteorFall", 2), ("EmberShroud", 2), ("ConjureOoze", 1),
                           ("MoveInnate", 2) },
        },

        new()
        {
            name = "EvilKnight", displayName = "Evil Knight", boss = true,
            spriteFolder = HeroKnight + "/Sprites", clipFolder = HeroKnight + "/Animations",
            maxHealth = 130, height = 1.35f, actionPoints = 2, brain = BrainType.Summoner,
            targeting = "Closest", loot = "EvilKnightDrop",
            states = new[]
            {
                R("Idle", AnimationCue.None, "HeroKnight_Idle", loop: true),
                R("Move", AnimationCue.Move, "HeroKnight_Run", loop: true),
                R("MeleeAttack", AnimationCue.MeleeAttack, "HeroKnight_Attack1"),
                R("Cast", AnimationCue.Cast, "HeroKnight_Block"),
                R("Summon", AnimationCue.Summon, "HeroKnight_Attack3"),
                R("Hurt", AnimationCue.Hurt, "HeroKnight_Hurt"),
                R("Die", AnimationCue.Die, "HeroKnight_Death"),
            },
            deck = new[] { ("Riposte", 2), ("ShieldBash", 2), ("Rally", 1),
                           ("MoveInnate", 2) },
        },

        new()
        {
            name = "EvilKing", displayName = "Evil King", boss = true,
            spriteFolder = King,
            maxHealth = 140, height = 1.5f, actionPoints = 2, brain = BrainType.Summoner,
            targeting = "Strongest", loot = "EvilKingDrop",
            states = new[]
            {
                S("Idle", AnimationCue.None, "Idle", loop: true),
                S("Move", AnimationCue.Move, "Run", loop: true),
                S("MeleeAttack", AnimationCue.MeleeAttack, "Attack_1"),
                S("Cast", AnimationCue.Cast, "Attack_2"),
                S("Summon", AnimationCue.Summon, "Attack_2"),
                S("Hurt", AnimationCue.Hurt, "Hit"),
                S("Die", AnimationCue.Die, "Death"),
            },
            deck = new[] { ("ExecutionersBlow", 2), ("RoyalGuard", 2), ("RoyalCommand", 1),
                           ("MoveInnate", 2) },
        },
    };

    // ---------------------------------------------------------------------------------------------
    // Boss loot. Better tiers than the level default, plus one signature card that always shows up.
    // Every guaranteed card is a real player card - guaranteedCards ignores the rarity roll and
    // excludeFromRewards alike, so an enemy card put here would genuinely be offered.
    // ---------------------------------------------------------------------------------------------

    public static readonly LootSpec[] Loot =
    {
        new() { name = "BlackKnightDrop", guaranteed = "Guardian's Aegis" },
        new() { name = "ReinforcedGolemDrop", guaranteed = "Sunder" },
        new() { name = "ReaperDrop", guaranteed = "Poison Dart" },
        new() { name = "EvilWizardDrop", guaranteed = "Firebolt" },
        new() { name = "EvilWizard2Drop", guaranteed = "Ice Shard" },
        new() { name = "EvilWizard3Drop", guaranteed = "Fireball" },
        new() { name = "EvilKnightDrop", guaranteed = "Parry" },
        new() { name = "EvilKingDrop", guaranteed = "Strengthen" },
    };

    // ---------------------------------------------------------------------------------------------
    // Two levels, so the roster is playable without hand-placing anything. Cells are one-based, the
    // way the Inspector shows them - [OneBasedCell] stores them one lower than that.
    // ---------------------------------------------------------------------------------------------

    public static readonly LevelSpec[] Levels =
    {
        new()
        {
            name = "Level3Menagerie",
            boardSize = new Vector2Int(6, 7),
            turnsToSurvive = 10,
            enemies = new[]
            {
                ("Goblin", new Vector2Int(2, 6)),
                ("Rat", new Vector2Int(4, 7)),
                ("Crab", new Vector2Int(5, 6)),
            },
            waves = new[]
            {
                (3, "Bat", new Vector2Int(1, 7)),
                (3, "VampireBat", new Vector2Int(6, 7)),
                (5, "SpikedSlime", new Vector2Int(3, 7)),
                (7, "Mushroom", new Vector2Int(5, 7)),
                (7, "Skeleton", new Vector2Int(2, 7)),
            },
            partySpawnCells = new[]
            {
                new Vector2Int(2, 1), new Vector2Int(4, 1), new Vector2Int(3, 2),
            },
        },

        new()
        {
            name = "Level4BlackKnight",
            boardSize = new Vector2Int(7, 7),
            turnsToSurvive = 12,
            enemies = new[]
            {
                ("BlackKnight", new Vector2Int(4, 7)),
                ("HeavyBandit", new Vector2Int(2, 6)),
                ("LightBandit", new Vector2Int(6, 6)),
            },
            waves = new[]
            {
                (4, "Skull", new Vector2Int(1, 7)),
                (8, "Skull", new Vector2Int(7, 7)),
            },
            partySpawnCells = new[]
            {
                new Vector2Int(3, 1), new Vector2Int(5, 1), new Vector2Int(4, 2),
            },
        },

        // Two short parades, the debug run's opening pair. Between them they stand up all 25 bodies,
        // twelve then thirteen, on a wide board with a gap between each - so two turns of play is
        // enough to see every pivot, scale and idle animation next to each other. Two turns because
        // this is a look, not a fight; Level3Menagerie and Level4BlackKnight after them are the fight.
        //
        // Laid out on alternating columns rather than a solid block: on a 2x1 iso cell a row is only
        // half a unit above the one behind it, and bodies up to 3.2 units tall would otherwise bury
        // each other whatever the sorting says.

        new()
        {
            name = "DebugParade1",
            boardSize = new Vector2Int(8, 8),
            turnsToSurvive = 2,
            enemies = new[]
            {
                ("Pebble", new Vector2Int(1, 8)),
                ("Rat", new Vector2Int(4, 8)),
                ("Skull", new Vector2Int(7, 8)),
                ("Crab", new Vector2Int(2, 7)),
                ("Bat", new Vector2Int(5, 7)),
                ("VampireBat", new Vector2Int(8, 7)),
                ("SpikedSlime", new Vector2Int(1, 6)),
                ("Golem", new Vector2Int(4, 6)),
                ("LightBandit", new Vector2Int(7, 6)),
                ("BlackKnight", new Vector2Int(2, 5)),
                ("ReinforcedGolem", new Vector2Int(5, 5)),
                ("Reaper", new Vector2Int(8, 5)),
            },
            partySpawnCells = new[]
            {
                new Vector2Int(2, 1), new Vector2Int(4, 1), new Vector2Int(6, 1),
            },
        },

        new()
        {
            name = "DebugParade2",
            boardSize = new Vector2Int(8, 8),
            turnsToSurvive = 2,
            enemies = new[]
            {
                ("Goblin", new Vector2Int(1, 8)),
                ("Mushroom", new Vector2Int(4, 8)),
                ("Skeleton", new Vector2Int(7, 8)),
                ("Mimic", new Vector2Int(2, 7)),
                ("PlagueRat", new Vector2Int(5, 7)),
                ("Slime", new Vector2Int(8, 7)),
                ("FlyingEye", new Vector2Int(1, 6)),
                ("HeavyBandit", new Vector2Int(4, 6)),
                ("EvilWizard", new Vector2Int(7, 6)),
                ("EvilWizard2", new Vector2Int(2, 5)),
                ("EvilWizard3", new Vector2Int(5, 5)),
                ("EvilKnight", new Vector2Int(8, 5)),
                ("EvilKing", new Vector2Int(4, 4)),
            },
            partySpawnCells = new[]
            {
                new Vector2Int(2, 1), new Vector2Int(4, 1), new Vector2Int(6, 1),
            },
        },
    };

    // ---------------------------------------------------------------------------------------------
    // The campaigns. RealRun holds the hand-authored levels; DebugRun is the test harness - the two
    // parades to look at every body, then the two levels that actually fight them.
    //
    // MainMenu.unity's campaign field still points at DebugRun. Switching to the real campaign is one
    // Inspector change on that field, not a code change.
    // ---------------------------------------------------------------------------------------------

    public static readonly RunSpec[] Runs =
    {
        new()
        {
            name = "RealRun",
            levels = new[] { "Level1", "Level2" },
            seedPartyFrom = "DebugRun",
        },

        new()
        {
            name = "DebugRun",
            levels = new[]
            {
                "DebugParade1", "DebugParade2", "Level3Menagerie", "Level4BlackKnight",
            },
        },
    };
}
