using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ONE-SHOT (2026-10-12): builds the Knight and Cleric cards chosen from balance-bot Card Labs II, III and
/// IV into the real game, and applies the rest of Card Lab II's "L4" reward pool those cards were
/// measured in. Specs: Docs/CardLab3-Handoff.md and Docs/CardLab4-Handoff.md (final numbers), and Card
/// Lab II's builder (BotRuns/claude-tools/cardlab2) for the Lab II cards, with Lab III's retunes of
/// Totem Strike, Brothers in Arms, Shrine Blessing and Blessed Shield applied.
///
/// What it writes:
///   * 47 CardData assets, each a copy of the card the lab built it from (art, tags, animation, range)
///     with its name, text, cost, rarity, class, effect entries and conditions rewritten.
///   * The effect assets those numbers need ("Shield 11", "Heal 7") - numbers live in effect assets, not
///     in amountDelta, so the card sheet's Effect column shows the real number.
///   * Chapel, Warhorn and Spring Totem: prefab, summon effect and card. 10 health, cost 2, Rare.
///   * The L4 pool: level-clear rewards Uncommon 40 / Rare 50 / Legendary 10, 17 starter-level
///     Knight/Cleric cards out of rewards, Touch Heal to Uncommon, the 7 Rare Knight/Cleric totems to
///     10 health.
///   * Every totem card that cost 3 now costs 2.
///
/// Repairs rather than skips (CLAUDE.md): every field it owns is rewritten on every run, so running it
/// against a project that has since been retuned through the card sheet WILL put these numbers back.
/// Archive it once it has run. Never batches imports - each asset is created in dependency order and
/// handed down as a live object (CLAUDE.md, StartAssetEditing).
/// </summary>
public static class CardLabContentBuilder
{
    private const string CardRoot = "Assets/Data/CardData";
    private const string EffectRoot = "Assets/Data/EffectData";
    private const string TotemRoot = "Assets/Prefabs/Totem";

    private const int Knight = 1;   // CharacterClass bits
    private const int Cleric = 8;

    private static StringBuilder log;

    // ------------------------------------------------------------------------------------------------
    // Spec types
    // ------------------------------------------------------------------------------------------------

    private enum Aim { Tile = 0, Self = 1 }

    /// One effect entry. `area` null = Single; `templateArea` >= 0 copies that entry's area off the
    /// template card (Cleave's line pattern).
    private class E
    {
        public string effect;
        public Aim aim;
        public (int shape, int min, int max)? radius;
        public int templateArea = -1;
        public CardCondition condition;
        public int bonus;
        public bool only;
    }

    private class C
    {
        public string name;
        public string template;
        public string folder;
        public int cls;
        public Rarity rarity;
        public int cost;
        public string text;
        public bool selfRange;          // overrides the template's range with SelfTile
        public CardCondition costCondition;
        public int costDiscount;
        public E[] entries;
        public GameObject summonArt;    // a totem card wears its totem's gem, as CardArtAssigner's "Fill summon art" does
    }

    private const int Chebyshev = 1, Manhattan = 2;

    private static E Tile(string effect) => new() { effect = effect, aim = Aim.Tile };
    private static E Self(string effect) => new() { effect = effect, aim = Aim.Self };

    private static E Around(E e, int shape, int min, int max) { e.radius = (shape, min, max); return e; }
    private static E Bonus(E e, CardCondition c, int bonus) { e.condition = c; e.bonus = bonus; return e; }
    private static E Only(E e, CardCondition c) { e.condition = c; e.only = true; return e; }

    // ------------------------------------------------------------------------------------------------
    // The cards
    // ------------------------------------------------------------------------------------------------

    private static C[] Cards() => new[]
    {
        // ---- Card Lab II (L4 pool numbers, Lab III retunes applied) ----
        K("Shield Bash", "Attack and Block", "Melee Attack", Rarity.Uncommon, 1, "Deal 7 damage. Gain 7 Shield.",
          Tile("Damage 7"), Self("Shield 7")),
        K("Totem Guard", "Block", "Buff (Defensive)", Rarity.Uncommon, 1, "Gain 3 Block. Draw 1. Costs 0 next to a totem.",
          Self("Block 3"), Self("Draw 1")).Costs(CardCondition.NextToTotem),
        Cl("Communion", "Guardian's Aegis", "Heal", Rarity.Rare, 2, "Heal 5 to you and allies within 2. Draw 1. Costs 1 next to another hero.",
          Around(Self("Heal 5"), Manhattan, 0, 2), Self("Draw 1")).Costs(CardCondition.NextToHero),
        Cl("Prayer Circle", "Heal Word", "Heal", Rarity.Uncommon, 1, "Heal 5. Draw 1. Costs 0 next to another hero.",
          Tile("Heal 5"), Self("Draw 1")).Costs(CardCondition.NextToHero),
        K("Bulwark Stance", "Shield", "Buff (Defensive)", Rarity.Uncommon, 1, "Gain 6 Shield. Draw 1. Costs 0 while you have Block.",
          Self("Shield 6"), Self("Draw 1")).Costs(CardCondition.HasBlock),
        K("Rally the Line", "Guardian's Aegis", "Buff (Defensive)", Rarity.Rare, 2, "Give 6 Shield to you and allies within 2. Draw 1. Costs 1 next to another hero.",
          Around(Self("Shield 6"), Manhattan, 0, 2), Self("Draw 1")).Costs(CardCondition.NextToHero),
        Cl("Lasting Renewal", "Renewing Touch", "Heal", Rarity.Uncommon, 1, "Apply Regeneration 4.",
          Tile("Regeneration 4")),
        Cl("Sanctified Guard", "Mend and Guard", "Heal", Rarity.Uncommon, 1, "Heal 5 and give 2 Block.",
          Tile("Heal 5"), Tile("Block 2")),
        K("Shield Strike", "Slash", "Melee Attack", Rarity.Uncommon, 1, "Deal 8 damage. Draw 1. Costs 0 while you have Shield.",
          Tile("Damage 8"), Self("Draw 1")).Costs(CardCondition.HasShield),
        K("Tower Shield", "Shield", "Buff (Defensive)", Rarity.Uncommon, 1, "Gain 11 Shield.",
          Self("Shield 11")),
        Cl("Blessed Shield", "Blade Ward", "Buff (Defensive)", Rarity.Uncommon, 1, "Give 10 Shield.",
          Tile("Shield 10")),
        K("Sweeping Blow", "Cleave", "Melee Attack", Rarity.Uncommon, 1, "Deal 7 damage to a line of 3.",
          new E { effect = "Damage 7", aim = Aim.Tile, templateArea = 0 }),
        K("Brothers in Arms", "Shield", "Buff (Defensive)", Rarity.Uncommon, 1, "Gain 8 Shield. Draw 1. Costs 0 next to another hero.",
          Self("Shield 8"), Self("Draw 1")).Costs(CardCondition.NextToHero),
        Cl("Rejuvenate", "Renew", "Heal", Rarity.Uncommon, 1, "Heal 6 and draw a card.",
          Tile("Heal 6"), Self("Draw 1")),
        Cl("Shrine Blessing", "Blade Ward", "Buff (Defensive)", Rarity.Uncommon, 1, "Give 9 Shield. Draw 1. Costs 0 next to a totem.",
          Tile("Shield 9"), Self("Draw 1")).Costs(CardCondition.NextToTotem),
        Cl("Holy Smite", "Smite", "Heal", Rarity.Uncommon, 1, "Deal 7 damage. Heal 4.",
          Tile("Damage 7"), Self("Heal 4")),
        K("Totem Strike", "Slash", "Melee Attack", Rarity.Uncommon, 1, "Deal 12 damage. Draw 1. Costs 0 next to a totem.",
          Tile("Damage 12"), Self("Draw 1")).Costs(CardCondition.NextToTotem),
        Cl("Radiant Smite", "Smite", "Heal", Rarity.Uncommon, 1, "Deal 6 damage. Heal 3. Draw 1. Costs 0 while you have Shield.",
          Tile("Damage 6"), Self("Heal 3"), Self("Draw 1")).Costs(CardCondition.HasShield),
        K("Shield Wall", "Guardian's Aegis", "Buff (Defensive)", Rarity.Rare, 1, "Give 6 Shield to you and allies within 2.",
          Around(Self("Shield 6"), Manhattan, 0, 2)),
        K("Shield Slam", "Attack and Block", "Melee Attack", Rarity.Uncommon, 1, "Deal 6 damage. Gain 3 Shield. Costs 0 while you have Shield.",
          Tile("Damage 6"), Self("Shield 3")).Costs(CardCondition.HasShield),
        K("Guard Up", "Shield", "Buff (Defensive)", Rarity.Uncommon, 1, "Gain 6 Shield. Costs 0 next to a totem.",
          Self("Shield 6")).Costs(CardCondition.NextToTotem),
        Cl("Mending Light", "Heal Word", "Heal", Rarity.Uncommon, 1, "Heal 4. Costs 0 next to another hero.",
          Tile("Heal 4")).Costs(CardCondition.NextToHero),
        Cl("Ward Blessing", "Blade Ward", "Buff (Defensive)", Rarity.Uncommon, 1, "Give 5 Shield. Costs 0 next to another hero.",
          Tile("Shield 5")).Costs(CardCondition.NextToHero),

        // ---- Card Lab III (handoff final numbers) ----
        Cl("Smite Together", "Smite", "Heal", Rarity.Uncommon, 1, "Deal 7 damage. Heal 3. Next to another hero: deal 10 instead.",
          Bonus(Tile("Damage 7"), CardCondition.NextToHero, 3), Self("Heal 3")),
        Cl("Sanctified Ground", "Heal Word", "Heal", Rarity.Rare, 1, "You and allies next to you gain 5 Shield and heal 5.",
          Around(Self("Shield 5"), Chebyshev, 0, 1), Around(Self("Heal 5"), Chebyshev, 0, 1)).SelfRange(),
        Cl("Laying on of Hands", "Heal Word", "Heal", Rarity.Uncommon, 1, "Heal 7 to you and allies next to you.",
          Around(Self("Heal 7"), Chebyshev, 0, 1)).SelfRange(),
        Cl("Shield of Faith", "Heal Word", "Buff (Defensive)", Rarity.Uncommon, 1, "Give 5 Shield. Next to another hero: give 9 and draw 1.",
          Bonus(Tile("Shield 5"), CardCondition.NextToHero, 4), Only(Self("Draw 1"), CardCondition.NextToHero)),
        Cl("Close Prayer", "Heal Word", "Heal", Rarity.Uncommon, 1, "Heal 7. Next to another hero: draw 1.",
          Tile("Heal 7"), Only(Self("Draw 1"), CardCondition.NextToHero)),
        Cl("Guiding Smite", "Smite", "Heal", Rarity.Uncommon, 1, "Deal 7 damage. You and allies next to you heal 4.",
          Tile("Damage 7"), Around(Self("Heal 4"), Chebyshev, 0, 1)),
        Cl("Benediction", "Heal Word", "Heal", Rarity.Uncommon, 1, "Heal 4 to an ally and every ally next to them. Draw 1.",
          Around(Tile("Heal 4"), Chebyshev, 0, 1), Self("Draw 1")),
        K("Shoulder to Shoulder", "Shield", "Buff (Defensive)", Rarity.Uncommon, 1, "You and allies next to you gain 8 Shield.",
          Around(Self("Shield 8"), Chebyshev, 0, 1)).SelfRange(),
        K("Vanguard's Oath", "Slash", "Melee Attack", Rarity.Rare, 1, "Deal 11 damage. Draw 1. Costs 0 next to another hero.",
          Tile("Damage 11"), Self("Draw 1")).Costs(CardCondition.NextToHero),
        K("Phalanx", "Block", "Buff (Defensive)", Rarity.Uncommon, 1, "You and allies next to you gain 2 Block.",
          Around(Self("Block 2"), Chebyshev, 0, 1)).SelfRange(),
        K("Hold the Line", "Shield", "Buff (Defensive)", Rarity.Rare, 2, "You and allies next to you gain 12 Shield. Draw 1.",
          Around(Self("Shield 12"), Chebyshev, 0, 1), Self("Draw 1")).SelfRange(),
        K("Covering Strike", "Slash", "Melee Attack", Rarity.Uncommon, 1, "Deal 9 damage. Allies next to you gain 5 Shield.",
          Tile("Damage 9"), Around(Self("Shield 5"), Chebyshev, 1, 1)),
        K("Flanking Strike", "Slash", "Melee Attack", Rarity.Uncommon, 1, "Deal 7 damage and gain 4 Shield. Next to another hero: deal 11 instead.",
          Bonus(Tile("Damage 7"), CardCondition.NextToHero, 4), Self("Shield 4")),

        // ---- Card Lab IV (distance 3, handoff final numbers) ----
        K("Lone Stand", "Shield", "Buff (Defensive)", Rarity.Uncommon, 1, "Gain 6 Shield. Alone: gain 12 instead.",
          Bonus(Self("Shield 6"), CardCondition.Alone, 6)),
        K("Solitary Guard", "Block", "Buff (Defensive)", Rarity.Uncommon, 1, "Gain 3 Block. Alone: gain 5 instead.",
          Bonus(Self("Block 3"), CardCondition.Alone, 2)),
        K("Last Bastion", "Taunt", "Buff (Defensive)", Rarity.Rare, 1, "Taunt every enemy within 2. Gain 6 Shield. Alone: gain 12 and draw 1.",
          Around(Self("Taunt"), Chebyshev, 1, 2), Bonus(Self("Shield 6"), CardCondition.Alone, 6),
          Only(Self("Draw 1"), CardCondition.Alone)).SelfRange(),
        Cl("Hermit's Smite", "Smite", "Heal", Rarity.Uncommon, 1, "Deal 8 damage. Heal 3. Alone: deal 12 instead.",
          Bonus(Tile("Damage 8"), CardCondition.Alone, 4), Self("Heal 3")),
        Cl("Distant Grace", "Heal Word", "Buff (Defensive)", Rarity.Uncommon, 1, "Give 6 Shield. Alone: give 10 instead.",
          Bonus(Tile("Shield 6"), CardCondition.Alone, 4)),
        Cl("Far Mend", "Heal Word", "Heal", Rarity.Uncommon, 1, "Heal 7. Alone: draw 1.",
          Tile("Heal 7"), Only(Self("Draw 1"), CardCondition.Alone)),
        Cl("Vigil", "Heal Word", "Heal", Rarity.Rare, 2, "Heal 12. Draw 1. Costs 1 when alone.",
          Tile("Heal 12"), Self("Draw 1")).Costs(CardCondition.Alone),
        Cl("Judgement", "Smite", "Heal", Rarity.Rare, 1, "Deal 9 damage. Alone: costs 0 and draw 1.",
          Tile("Damage 9"), Only(Self("Draw 1"), CardCondition.Alone)).Costs(CardCondition.Alone),
    };

    private static C K(string name, string template, string folder, Rarity rarity, int cost, string text, params E[] entries) =>
        new() { name = name, template = template, folder = "Knight/" + folder, cls = Knight, rarity = rarity, cost = cost, text = text, entries = entries };

    private static C Cl(string name, string template, string folder, Rarity rarity, int cost, string text, params E[] entries) =>
        new() { name = name, template = template, folder = "Cleric/" + folder, cls = Cleric, rarity = rarity, cost = cost, text = text, entries = entries };

    private static C Costs(this C c, CardCondition condition) { c.costCondition = condition; c.costDiscount = 1; return c; }
    private static C SelfRange(this C c) { c.selfRange = true; return c; }

    /// Starter-level Knight/Cleric cards Card Lab II took out of the reward pool. Starter decks keep them.
    private static readonly string[] OutOfRewards =
    {
        "Slash", "Shield", "Block", "Second Wind", "Attack and Block", "Cleave", "Guardian's Aegis",
        "Smite", "Renew", "Cleansing Light", "Purge", "Mend and Guard", "Renewing Touch", "Vengeful Vigor",
        "Heal Totem", "Heal Word", "Blade Ward",
    };

    /// Rare Knight/Cleric totems Card Lab II ran at 10 health (prefab file names).
    private static readonly string[] RareTotems =
    {
        "BloodmoonTotem", "LodestoneTotem", "Totem", "TwinTotem", "AegisTotem", "ReinforceTotem", "WarcryTotem",
    };

    // ------------------------------------------------------------------------------------------------
    // Entry point
    // ------------------------------------------------------------------------------------------------

    [MenuItem("Tools/Cards/Build Card Lab Content (one-shot)")]
    public static void BuildFromMenu() => Debug.Log(Build());

    public static string Build()
    {
        log = new StringBuilder();

        // Totems first: their cards point at the summon effects, which point at the prefabs.
        CardEffect draw1 = FindEffect("Draw 1");
        CardEffect energy1 = FindEffect("Energy 1");

        GameObject chapel = TotemPrefab("ChapelTotem", "ScholarTotem", "Chapel Totem",
            (TriggeringActionType.Heal, energy1, "When an ally heals nearby, they gain 1 energy."));
        GameObject warhorn = TotemPrefab("WarhornTotem", "ScholarTotem", "Warhorn Totem",
            (TriggeringActionType.Damage, draw1, "When an ally attacks nearby, they draw a card."));
        GameObject spring = TotemPrefab("SpringTotem", "HealTotem", "Spring Totem", null);

        SummonEffect summonChapel = SummonFor("SummonChapelTotem", "SummonScholarTotem", chapel);
        SummonEffect summonWarhorn = SummonFor("SummonWarhornTotem", "SummonScholarTotem", warhorn);
        SummonEffect summonSpring = SummonFor("SummonSpringTotem", "SummonHealTotem", spring);

        List<C> cards = new(Cards());
        cards.Add(TotemCard("Chapel Totem", "Echo Totem", "Cleric/Summon", Cleric,
            "Summon a Chapel Totem (10 health): allies healing nearby gain 1 energy.", summonChapel));
        cards.Add(TotemCard("Warhorn Totem", "Fury Totem", "Knight/Summon", Knight,
            "Summon a Warhorn Totem (10 health): allies attacking nearby draw a card.", summonWarhorn));
        cards.Add(TotemCard("Spring Totem", "Heal Totem", "Cleric/Summon", Cleric,
            "Summon a Spring Totem (10 health): Regeneration 5 to allies nearby.", summonSpring));

        int built = 0;
        foreach (C spec in cards)
        {
            if (WriteCard(spec)) { built++; }
        }
        log.AppendLine($"cards written: {built} of {cards.Count}");

        ApplyPool();
        TotemCosts();
        GlossaryTerms();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return log.ToString();
    }

    // ------------------------------------------------------------------------------------------------
    // Effects
    // ------------------------------------------------------------------------------------------------

    private static readonly Dictionary<string, (string template, string field)> EffectKinds = new()
    {
        { "Damage", ("Damage 9", "damageAmount") },
        { "Shield", ("Shield 7", "shieldAmount") },
        { "Heal", ("Heal 5", "healAmount") },
        { "Block", ("Block 2", "blockCount") },
    };

    /// The effect asset called `name`, created from its kind's template when it does not exist yet
    /// ("Shield 11" from "Shield 7", amount 11). An existing numbered asset has its number rewritten to
    /// match its name, which is a no-op for every asset named honestly.
    private static CardEffect Effect(string name)
    {
        CardEffect existing = FindEffect(name, required: false);
        Match m = Regex.Match(name, @"^(Damage|Shield|Heal|Block) (\d+)$");

        if (!m.Success)
        {
            if (existing == null) { throw new System.Exception($"CardLabContentBuilder: no effect named '{name}'"); }
            return existing;
        }

        (string template, string field) = EffectKinds[m.Groups[1].Value];
        int amount = int.Parse(m.Groups[2].Value);
        CardEffect effect = existing;

        if (effect == null)
        {
            CardEffect source = FindEffect(template);
            string dir = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(source)).Replace('\\', '/');
            string path = $"{dir}/{name}.asset";

            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path))
            {
                throw new System.Exception($"CardLabContentBuilder: could not create {path}");
            }

            effect = AssetDatabase.LoadAssetAtPath<CardEffect>(path);
            log.AppendLine($"effect created: {path}");
        }

        SerializedObject so = new(effect);
        SerializedProperty amountProperty = so.FindProperty(field);

        if (amountProperty.intValue != amount)
        {
            amountProperty.intValue = amount;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(effect);
        }

        return effect;
    }

    private static CardEffect FindEffect(string name, bool required = true)
    {
        foreach (string guid in AssetDatabase.FindAssets($"{name} t:CardEffect", new[] { EffectRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) != name) { continue; }

            CardEffect effect = AssetDatabase.LoadAssetAtPath<CardEffect>(path);
            if (effect != null) { return effect; }
        }

        if (required) { throw new System.Exception($"CardLabContentBuilder: no effect named '{name}'"); }
        return null;
    }

    // ------------------------------------------------------------------------------------------------
    // Cards
    // ------------------------------------------------------------------------------------------------

    private static CardData FindCard(string assetName)
    {
        foreach (string guid in AssetDatabase.FindAssets($"{assetName} t:CardData", new[] { CardRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) != assetName) { continue; }

            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(path);
            if (card != null) { return card; }
        }

        return null;
    }

    private static bool WriteCard(C spec)
    {
        CardData template = FindCard(spec.template);
        if (template == null) { log.AppendLine($"ERROR {spec.name}: no template card '{spec.template}'"); return false; }

        // Resolve every effect first, so a bad name cannot leave a half-built card.
        CardEffect[] effects = new CardEffect[spec.entries.Length];
        for (int i = 0; i < spec.entries.Length; i++) { effects[i] = Effect(spec.entries[i].effect); }

        string dir = $"{CardRoot}/{spec.folder}";
        string path = $"{dir}/{spec.name}.asset";

        CardData card = AssetDatabase.LoadAssetAtPath<CardData>(path);
        if (card == null)
        {
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(template), path))
            {
                log.AppendLine($"ERROR {spec.name}: could not copy {spec.template} to {path}");
                return false;
            }

            card = AssetDatabase.LoadAssetAtPath<CardData>(path);
            log.AppendLine($"card created: {path}");
        }

        SerializedObject t = new(template);
        SerializedObject so = new(card);

        // Copied off the template every run rather than only at creation, so a re-run repairs them too.
        so.FindProperty("<image>k__BackingField").objectReferenceValue = SummonSprite(spec.summonArt)
            ?? t.FindProperty("<image>k__BackingField").objectReferenceValue;
        so.FindProperty("<animation>k__BackingField").objectReferenceValue = t.FindProperty("<animation>k__BackingField").objectReferenceValue;
        so.CopyFromSerializedProperty(t.FindProperty("<tags>k__BackingField"));
        so.CopyFromSerializedProperty(t.FindProperty("<range>k__BackingField"));

        so.FindProperty("<cardName>k__BackingField").stringValue = spec.name;
        so.FindProperty("<description>k__BackingField").stringValue = spec.text;
        so.FindProperty("<cost>k__BackingField").intValue = spec.cost;
        so.FindProperty("<rarity>k__BackingField").intValue = (int)spec.rarity;
        so.FindProperty("<requiredClass>k__BackingField").intValue = spec.cls;
        so.FindProperty("<excludeFromRewards>k__BackingField").boolValue = false;
        so.FindProperty("<rotatableAim>k__BackingField").boolValue = false;
        so.FindProperty("<upgradedForm>k__BackingField").objectReferenceValue = null;
        so.FindProperty("<keywords>k__BackingField").arraySize = 0;
        so.FindProperty("<costCondition>k__BackingField").intValue = (int)spec.costCondition;
        so.FindProperty("<costDiscount>k__BackingField").intValue = spec.costDiscount;

        if (spec.selfRange)
        {
            SerializedProperty range = so.FindProperty("<range>k__BackingField");
            range.FindPropertyRelative("shape").intValue = (int)RangeShape.SelfTile;
            range.FindPropertyRelative("minDistance").intValue = 0;
            range.FindPropertyRelative("maxDistance").intValue = 0;
            range.FindPropertyRelative("requiresRoute").boolValue = false;
        }

        SerializedProperty templateEntries = t.FindProperty("<effectEntries>k__BackingField");
        SerializedProperty entries = so.FindProperty("<effectEntries>k__BackingField");
        entries.arraySize = spec.entries.Length;

        for (int i = 0; i < spec.entries.Length; i++)
        {
            E e = spec.entries[i];
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);

            entry.FindPropertyRelative("effect").objectReferenceValue = effects[i];
            entry.FindPropertyRelative("aimsAt").intValue = (int)e.aim;
            entry.FindPropertyRelative("amountDelta").intValue = 0;
            entry.FindPropertyRelative("amountPercent").intValue = 0;
            entry.FindPropertyRelative("condition").intValue = (int)e.condition;
            entry.FindPropertyRelative("conditionBonus").intValue = e.bonus;
            entry.FindPropertyRelative("onlyWhenMet").boolValue = e.only;

            SerializedProperty area = entry.FindPropertyRelative("area");

            if (e.templateArea >= 0)
            {
                SerializedProperty source = templateEntries.GetArrayElementAtIndex(e.templateArea).FindPropertyRelative("area");
                area.FindPropertyRelative("kind").intValue = source.FindPropertyRelative("kind").intValue;
                CopyRange(source.FindPropertyRelative("radius"), area.FindPropertyRelative("radius"));
                area.FindPropertyRelative("pattern").objectReferenceValue = source.FindPropertyRelative("pattern").objectReferenceValue;
                continue;
            }

            SerializedProperty radius = area.FindPropertyRelative("radius");
            area.FindPropertyRelative("pattern").objectReferenceValue = null;

            if (e.radius is (int shape, int min, int max))
            {
                area.FindPropertyRelative("kind").intValue = (int)AreaKind.Radius;
                radius.FindPropertyRelative("shape").intValue = shape;
                radius.FindPropertyRelative("minDistance").intValue = min;
                radius.FindPropertyRelative("maxDistance").intValue = max;
            }
            else
            {
                area.FindPropertyRelative("kind").intValue = (int)AreaKind.Single;
                radius.FindPropertyRelative("shape").intValue = 0;
                radius.FindPropertyRelative("minDistance").intValue = 0;
                radius.FindPropertyRelative("maxDistance").intValue = 0;
            }

            radius.FindPropertyRelative("requiresRoute").boolValue = false;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(card);
        return true;
    }

    private static void CopyRange(SerializedProperty from, SerializedProperty to)
    {
        to.FindPropertyRelative("shape").intValue = from.FindPropertyRelative("shape").intValue;
        to.FindPropertyRelative("minDistance").intValue = from.FindPropertyRelative("minDistance").intValue;
        to.FindPropertyRelative("maxDistance").intValue = from.FindPropertyRelative("maxDistance").intValue;
        to.FindPropertyRelative("requiresRoute").boolValue = from.FindPropertyRelative("requiresRoute").boolValue;
    }

    // ------------------------------------------------------------------------------------------------
    // Totems
    // ------------------------------------------------------------------------------------------------

    /// A 10-health totem copied from `template` - Scholar Totem for the two reaction totems, so they wear
    /// its gold reaction gem (gem shape 10 = reaction, gold = utility; see the totem gem convention), Heal
    /// Totem for Spring, which is Heal Totem's aura and so keeps its gem. `reaction` replaces the
    /// template's reactions when given; null keeps them (and the template's auras either way).
    private static GameObject TotemPrefab(string file, string template, string displayName,
        (TriggeringActionType trigger, CardEffect effect, string text)? reaction)
    {
        string path = $"{TotemRoot}/{file}.prefab";

        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            if (!AssetDatabase.CopyAsset($"{TotemRoot}/{template}.prefab", path))
            {
                throw new System.Exception($"CardLabContentBuilder: could not copy {template} to {path}");
            }

            log.AppendLine($"totem created: {path}");
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);

        try
        {
            SetHealth(root, 10);

            SerializedObject body = new(root.GetComponent<Character>());
            body.FindProperty("displayName").stringValue = displayName;
            body.ApplyModifiedPropertiesWithoutUndo();

            if (reaction is (TriggeringActionType trigger, CardEffect effect, string text))
            {
                SerializedObject totem = new(root.GetComponent<Totem>());
                SerializedProperty reactions = totem.FindProperty("reactions");
                reactions.arraySize = 1;

                SerializedProperty r = reactions.GetArrayElementAtIndex(0);
                r.FindPropertyRelative("trigger").intValue = (int)trigger;
                r.FindPropertyRelative("effect").objectReferenceValue = effect;
                r.FindPropertyRelative("description").stringValue = text;
                totem.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static void SetHealth(GameObject root, int health)
    {
        SerializedObject body = new(root.GetComponent<Character>());
        body.FindProperty("maxHealth").intValue = health;
        body.FindProperty("<Health>k__BackingField").intValue = health;
        body.ApplyModifiedPropertiesWithoutUndo();
    }

    private static SummonEffect SummonFor(string file, string template, GameObject prefab)
    {
        string dir = $"{EffectRoot}/Summon";
        string path = $"{dir}/{file}.asset";

        if (AssetDatabase.LoadAssetAtPath<SummonEffect>(path) == null)
        {
            if (!AssetDatabase.CopyAsset($"{dir}/{template}.asset", path))
            {
                throw new System.Exception($"CardLabContentBuilder: could not copy {template} to {path}");
            }

            log.AppendLine($"summon created: {path}");
        }

        SummonEffect summon = AssetDatabase.LoadAssetAtPath<SummonEffect>(path);
        SerializedObject so = new(summon);
        so.FindProperty("summonedObject").objectReferenceValue = prefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(summon);

        return summon;
    }

    private static C TotemCard(string name, string template, string folder, int cls, string text, SummonEffect summon) =>
        new()
        {
            name = name, template = template, folder = folder, cls = cls, rarity = Rarity.Rare, cost = 2, text = text,
            entries = new[] { Tile(summon.name) }, summonArt = summon.SummonedObject,
        };

    /// The gem on a totem's "Sprite" child - the same place CardArtAssigner.SummonSprite reads first.
    /// Returned as Object so the caller's ?? falls back to the template's art: null here is a real null,
    /// never a fake-null UnityEngine.Object (CLAUDE.md).
    private static Object SummonSprite(GameObject summoned)
    {
        if (summoned == null) { return null; }

        Transform art = summoned.transform.Find("Sprite");
        SpriteRenderer renderer = art != null ? art.GetComponent<SpriteRenderer>() : null;

        if (renderer == null || renderer.sprite == null) { return null; }

        return renderer.sprite;
    }

    // ------------------------------------------------------------------------------------------------
    // The L4 pool and the totem costs
    // ------------------------------------------------------------------------------------------------

    private static void ApplyPool()
    {
        LootTable table = AssetDatabase.LoadAssetAtPath<LootTable>("Assets/Data/LootTable/EarlyLevelReward.asset");
        SerializedObject so = new(table);
        SerializedProperty tiers = so.FindProperty("tierWeights");
        (Rarity rarity, int weight)[] weights = { (Rarity.Uncommon, 40), (Rarity.Rare, 50), (Rarity.Legendary, 10) };
        tiers.arraySize = weights.Length;

        for (int i = 0; i < weights.Length; i++)
        {
            tiers.GetArrayElementAtIndex(i).FindPropertyRelative("rarity").intValue = (int)weights[i].rarity;
            tiers.GetArrayElementAtIndex(i).FindPropertyRelative("weight").intValue = weights[i].weight;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(table);
        log.AppendLine("EarlyLevelReward: Uncommon 40 / Rare 50 / Legendary 10");

        foreach (string name in OutOfRewards)
        {
            CardData card = FindCard(name);
            if (card == null) { log.AppendLine($"ERROR out-of-rewards: no card '{name}'"); continue; }

            SetCard(card, s => s.FindProperty("<excludeFromRewards>k__BackingField").boolValue = true);
        }
        log.AppendLine($"out of rewards: {OutOfRewards.Length}");

        // Touch Heal's asset is Cure Wounds.
        CardData touchHeal = FindCard("Cure Wounds");
        SetCard(touchHeal, s => s.FindProperty("<rarity>k__BackingField").intValue = (int)Rarity.Uncommon);
        log.AppendLine("Touch Heal: Uncommon");

        foreach (string file in RareTotems)
        {
            string path = $"{TotemRoot}/{file}.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                SetHealth(root, 10);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        log.AppendLine($"rare totems at 10 health: {RareTotems.Length}");
    }

    private static void TotemCosts()
    {
        int changed = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:CardData", new[] { CardRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(path);

            if (card == null || card.cost != 3 || !card.HasSummonOf<Totem>()) { continue; }

            SetCard(card, s => s.FindProperty("<cost>k__BackingField").intValue = 2);
            log.AppendLine($"totem cost 3 -> 2: {card.cardName}");
            changed++;
        }

        log.AppendLine($"totem costs lowered: {changed}");
    }

    /// Whether any entry on this card summons something carrying T - what tells a totem card from a
    /// skeleton summon without trusting its name.
    private static bool HasSummonOf<T>(this CardData card) where T : Component
    {
        foreach (CardEffectEntry entry in card.effectEntries)
        {
            if (entry.effect is not SummonEffect summon) { continue; }

            GameObject summoned = summon.SummonedObject;
            if (summoned != null && summoned.GetComponent<T>() != null) { return true; }
        }

        return false;
    }

    // ------------------------------------------------------------------------------------------------
    // Glossary
    // ------------------------------------------------------------------------------------------------

    /// Hover text for the two conditions a player can misread. "Alone" says its radius through
    /// {magnitude}, which Glossary.ConditionContent fills from CardConditions.AloneDistance. The other
    /// three conditions read plainly ("while you have Shield" already links Shield).
    private static void GlossaryTerms()
    {
        Glossary glossary = AssetDatabase.LoadAssetAtPath<Glossary>("Assets/Scripts/UI/Tooltips/Glossary.asset");
        SerializedObject so = new(glossary);
        SerializedProperty list = so.FindProperty("conditions");

        (CardCondition type, string title, string body, string[] terms)[] entries =
        {
            (CardCondition.Alone, "ALONE",
             "No other hero within {magnitude} tiles of you - a diagonal step counts as 1. Summoned allies and totems don't count.",
             new[] { "Alone" }),
            (CardCondition.NextToHero, "NEXT TO",
             "On one of the 8 tiles around, diagonals included.",
             new[] { "next to another hero", "next to you", "next to them", "next to a totem" }),
        };

        list.arraySize = entries.Length;

        for (int i = 0; i < entries.Length; i++)
        {
            SerializedProperty e = list.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("type").intValue = (int)entries[i].type;
            e.FindPropertyRelative("title").stringValue = entries[i].title;
            e.FindPropertyRelative("body").stringValue = entries[i].body;
            e.FindPropertyRelative("defaultStacks").intValue = 1;
            e.FindPropertyRelative("defaultAmount").intValue = 1;

            SerializedProperty terms = e.FindPropertyRelative("terms");
            terms.arraySize = entries[i].terms.Length;
            for (int j = 0; j < entries[i].terms.Length; j++) { terms.GetArrayElementAtIndex(j).stringValue = entries[i].terms[j]; }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(glossary);
        log.AppendLine($"glossary conditions: {entries.Length}");
    }

    private static void SetCard(CardData card, System.Action<SerializedObject> write)
    {
        SerializedObject so = new(card);
        write(so);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(card);
    }
}
