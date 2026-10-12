using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// CARD LAB 2 (bot-mirror only, never in the real project). Rebuilds the Knight and Cleric reward pools:
/// every starter card and every draftable card at or below starter power is excluded from rewards, and
/// the lab cards below - stronger versions of the staples, and combo cards that cost less while a
/// condition holds (LabCost) and draw - are added to AllCards. Rare totems (existing and new) are set to
/// 10 health by the variant's text patches; the new lab totems are built at 10 here.
/// Starter decks are untouched: they are meant to sit below the curve.
/// </summary>
public static class CardLab2Builder
{
    private const string Dir = "Assets/Data/CardData/Lab";
    private static readonly List<CardData> made = new();
    private static readonly Dictionary<string, string> config = new();

    private static string Cfg(string key, string fallback)
    {
        return config.TryGetValue(key, out string v) ? v : fallback;
    }

    private static void ReadConfig()
    {
        config.Clear();
        string path = "Assets/Editor/CardLab/config.txt";
        if (!System.IO.File.Exists(path)) { return; }
        foreach (string line in System.IO.File.ReadAllLines(path))
        {
            int eq = line.IndexOf('=');
            if (eq > 0) { config[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim(); }
        }
        Debug.Log("CardLab2Builder config: " + string.Join("; ", config));
    }

    public static void BuildFromCommandLine()
    {
        Generate();
        BotPlayerBuild.BuildFromCommandLine();
    }

    private static void Generate()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) { AssetDatabase.CreateFolder("Assets/Data/CardData", "Lab"); }

        made.Clear();
        ReadConfig();
        if (Cfg("cards", "1") == "0") { Debug.Log("CardLab2Builder: cards=0, no lab cards"); ClearTable(); AssetDatabase.SaveAssets(); return; }
        CardEffect draw1 = Load<CardEffect>("Draw 1");
        CardEffect shield7 = Load<CardEffect>("Shield 7");
        CardEffect heal5 = Load<CardEffect>("Heal 5");
        CardEffect energy1 = Load<CardEffect>("Energy 1");
        int cleric = ClassOf(LoadCard("Heal Word"));

        // ---------------------------------------------------------------- Knight: stronger staples
        Make("Heavy Slash", "Slash", "Deal 13 damage.", 1, Rarity.Uncommon, e => Delta(e, 0, 4));
        Make("Tower Shield", "Shield", "Gain 11 Shield.", 1, Rarity.Uncommon, e => Delta(e, 0, 4));
        Make("Iron Stance", "Block", "Gain 5 Block.", 1, Rarity.Uncommon, e => Delta(e, 0, 2));
        Make("Shield Bash", "Attack and Block", "Deal 7 damage. Gain 7 Shield.", 1, Rarity.Uncommon,
             e => { Delta(e, 0, 2); Delta(e, 1, 2); });
        Make("Sweeping Blow", "Cleave", "Deal 7 damage to a line of 3.", 1, Rarity.Uncommon, e => Delta(e, 0, 2));
        Make("Shield Wall", "Guardian's Aegis", "Give 6 Shield to you and allies within 2.", 1, Rarity.Rare, e => Delta(e, 0, 1));

        // ---------------------------------------------------------------- Knight: combo (cheaper + draw)
        Make("Totem Strike", "Slash", "Deal 9 damage. Draw 1. Costs 0 next to a totem.", 1, Rarity.Uncommon,
             e => AddDraw(e, draw1), LabCost.NextToTotem);
        Make("Shield Strike", "Slash", "Deal 8 damage. Draw 1. Costs 0 while you have Shield.", 1, Rarity.Uncommon,
             e => { Delta(e, 0, -1); AddDraw(e, draw1); }, LabCost.HasShield);
        Make("Brothers in Arms", "Shield", "Gain 7 Shield. Draw 1. Costs 0 next to another hero.", 1, Rarity.Uncommon,
             e => AddDraw(e, draw1), LabCost.NextToHero);
        Make("Bulwark Stance", "Shield", "Gain 6 Shield. Draw 1. Costs 0 while you have Block.", 1, Rarity.Uncommon,
             e => { Delta(e, 0, -1); AddDraw(e, draw1); }, LabCost.HasBlock);
        Make("Totem Guard", "Block", "Gain 3 Block. Draw 1. Costs 0 next to a totem.", 1, Rarity.Uncommon,
             e => AddDraw(e, draw1), LabCost.NextToTotem);
        Make("Rally the Line", "Guardian's Aegis", "Give 6 Shield to you and allies within 2. Draw 1. Costs 1 less next to another hero.",
             2, Rarity.Rare, e => { Delta(e, 0, 1); AddDraw(e, draw1); }, LabCost.NextToHero);

        // ---------------------------------------------------------------- Cleric: stronger staples
        Make("Holy Smite", "Smite", "Deal 7 damage. Heal 4.", 1, Rarity.Uncommon, e => { Delta(e, 0, 3); Delta(e, 1, 1); });
        Make("Rejuvenate", "Renew", "Heal 6 and draw a card.", 1, Rarity.Uncommon, e => Delta(e, 0, 3));
        Make("Sanctified Guard", "Mend and Guard", "Heal 5 and give 2 Block.", 1, Rarity.Uncommon, e => { });
        Make("Blessed Shield", "Blade Ward", "Give 8 Shield.", 1, Rarity.Uncommon, e => { Swap(e, 0, shield7); Delta(e, 0, 1); });
        Make("Lasting Renewal", "Renewing Touch", "Apply Regeneration 4.", 1, Rarity.Uncommon, e => { });

        // ---------------------------------------------------------------- Cleric: combo (cheaper + draw)
        Make("Prayer Circle", "Heal Word", "Heal 5. Draw 1. Costs 0 next to another hero.", 1, Rarity.Uncommon,
             e => AddDraw(e, draw1), LabCost.NextToHero);
        Make("Shrine Blessing", "Blade Ward", "Give 6 Shield. Draw 1. Costs 0 next to a totem.", 1, Rarity.Uncommon,
             e => { Swap(e, 0, shield7); Delta(e, 0, -1); AddDraw(e, draw1); }, LabCost.NextToTotem);
        Make("Radiant Smite", "Smite", "Deal 6 damage. Heal 3. Draw 1. Costs 0 while you have Shield.", 1, Rarity.Uncommon,
             e => { Delta(e, 0, 2); AddDraw(e, draw1); }, LabCost.HasShield);
        Make("Ward of Faith", "Blade Ward", "Give 2 Block. Draw 1. Costs 0 while you have Block.", 1, Rarity.Uncommon,
             e => AddDraw(e, draw1), LabCost.HasBlock);
        CardData communion = Make("Communion", "Guardian's Aegis", "Heal 5 to you and allies within 2. Draw 1. Costs 1 less next to another hero.",
             2, Rarity.Rare, e => { Swap(e, 0, heal5); AddDraw(e, draw1); }, LabCost.NextToHero);
        if (communion != null) { SetClass(communion, cleric); }

        // ---------------------------------------------------------------- new 10-HP rare totems
        GameObject warhorn = Totem("Lab WarhornTotem", TriggeringActionType.Damage, draw1, "When an ally attacks nearby, they draw a card.");
        GameObject chapel = Totem("Lab ChapelTotem", TriggeringActionType.Heal, energy1, "When an ally heals nearby, they gain 1 energy.");
        TotemCard("Warhorn Totem", "Fury Totem", warhorn, "Summon a Warhorn Totem (10 health): allies attacking nearby draw a card.", -1);
        TotemCard("Chapel Totem", "Fury Totem", chapel, "Summon a Chapel Totem (10 health): allies healing nearby gain 1 energy.", cleric);

        // ---------------------------------------------------------------- L3: free-when-condition, no draw (cards=3 and up)
        if (int.Parse(Cfg("wave", "1")) >= 3)
        {
            CardEffect shield3 = Load<CardEffect>("Shield 3");
            CardEffect shield5 = Load<CardEffect>("Shield 5");
            CardEffect heal3 = Load<CardEffect>("Heal 3");
            Make("Shield Slam", "Attack and Block", "Deal 6 damage. Gain 3 Shield. Costs 0 while you have Shield.", 1, Rarity.Uncommon,
                 e => { Delta(e, 0, 1); Swap(e, 1, shield3); }, LabCost.HasShield);
            Make("Guard Up", "Shield", "Gain 6 Shield. Costs 0 next to a totem.", 1, Rarity.Uncommon,
                 e => Delta(e, 0, -1), LabCost.NextToTotem);
            Make("Mending Light", "Heal Word", "Heal 4. Costs 0 next to another hero.", 1, Rarity.Uncommon,
                 e => { Swap(e, 0, heal3); Delta(e, 0, 1); }, LabCost.NextToHero);
            Make("Ward Blessing", "Blade Ward", "Give 5 Shield. Costs 0 next to another hero.", 1, Rarity.Uncommon,
                 e => Swap(e, 0, shield5), LabCost.NextToHero);
            GameObject spring = CopyTotem("HealTotem", "Lab SpringTotem", 10);
            TotemCard("Spring Totem", "Heal Totem", spring, "Summon a Spring Totem (10 health): Regeneration 5 to allies nearby.", -1);
        }

        // ---------------------------------------------------------------- the pool
        string[] dropped =
        {
            // Knight starters and below-curve draftables
            "Slash", "Shield", "Block", "Second Wind", "Attack and Block", "Cleave", "Guardian's Aegis",
            // Cleric starters and below-curve draftables
            "Smite", "Renew", "Cleansing Light", "Purge", "Mend and Guard", "Renewing Touch", "Vengeful Vigor",
            "Heal Totem", "Heal Word", "Blade Ward",
        };

        if (Cfg("exclude", "1") != "0")
        {
            foreach (string name in dropped)
            {
                if (("|" + Cfg("keep", "") + "|").Contains("|" + name + "|")) { continue; }
                Exclude(LoadCard(name));
            }
        }

        // rare=Name|Name / uncommon=Name|Name : rarity overrides for lab cards (by card name).
        foreach (CardData card in made)
        {
            if (("|" + Cfg("rare", "") + "|").Contains("|" + card.cardName + "|")) { SetRarity(card, Rarity.Rare); }
            if (("|" + Cfg("uncommon", "") + "|").Contains("|" + card.cardName + "|")) { SetRarity(card, Rarity.Uncommon); }
        }
        ClearTable();

        // Touch Heal (Heal 10) is already above the curve; Uncommon so it is actually offered.
        SetRarity(LoadCardByCardName("Touch Heal"), Rarity.Uncommon);

        CardLibrary library = AssetDatabase.LoadAssetAtPath<CardLibrary>("Assets/Data/CardLibrary/AllCards.asset");
        SerializedObject lo = new(library);
        SerializedProperty cards = lo.FindProperty("cards");

        HashSet<Object> present = new();
        for (int i = 0; i < cards.arraySize; i++) { present.Add(cards.GetArrayElementAtIndex(i).objectReferenceValue); }

        foreach (CardData card in made)
        {
            if (present.Contains(card)) { continue; }
            cards.arraySize++;
            cards.GetArrayElementAtIndex(cards.arraySize - 1).objectReferenceValue = card;
        }

        lo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        Debug.Log($"CardLab2Builder: {made.Count} lab cards made, library now {library.Cards.Count}, {dropped.Length} cards excluded");
    }

    // ------------------------------------------------------------------ helpers

    /// clear=U,R,L rewrites EarlyLevelReward's tier weights (Uncommon, Rare, Legendary).
    private static void ClearTable()
    {
        string clear = Cfg("clear", "");
        if (clear == "") { return; }
        string[] w = clear.Split(',');
        LootTable table = AssetDatabase.LoadAssetAtPath<LootTable>("Assets/Data/LootTable/EarlyLevelReward.asset");
        SerializedObject so = new(table);
        SerializedProperty tiers = so.FindProperty("tierWeights");
        tiers.arraySize = 3;
        for (int i = 0; i < 3; i++)
        {
            tiers.GetArrayElementAtIndex(i).FindPropertyRelative("rarity").intValue = i + 1;
            tiers.GetArrayElementAtIndex(i).FindPropertyRelative("weight").intValue = int.Parse(w[i]);
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(table);
    }

    private static T Load<T>(string assetName) where T : Object
    {
        foreach (string guid in AssetDatabase.FindAssets(assetName + " t:" + typeof(T).Name))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith(Dir)) { continue; }
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null && asset.name == assetName) { return asset; }
        }

        throw new System.Exception($"CardLab2Builder: no {typeof(T).Name} named '{assetName}'");
    }

    private static CardData LoadCard(string assetName) => Load<CardData>(assetName);

    private static CardData LoadCardByCardName(string cardName)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:CardData"))
        {
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(guid));
            if (card != null && card.cardName == cardName) { return card; }
        }

        throw new System.Exception($"CardLab2Builder: no card called '{cardName}'");
    }

    private static int ClassOf(CardData card) =>
        new SerializedObject(card).FindProperty("<requiredClass>k__BackingField").intValue;

    private static void SetClass(CardData card, int value) => SetInt(card, "<requiredClass>k__BackingField", value);

    private static void SetRarity(CardData card, Rarity rarity) => SetInt(card, "<rarity>k__BackingField", (int)rarity);

    private static void Exclude(CardData card)
    {
        SerializedObject so = new(card);
        so.FindProperty("<excludeFromRewards>k__BackingField").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(card);
    }

    private static void SetInt(Object asset, string property, int value)
    {
        SerializedObject so = new(asset);
        so.FindProperty(property).intValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
    }

    private static T CopyAsset<T>(Object template, string name) where T : Object
    {
        string ext = template is GameObject ? ".prefab" : ".asset";
        string path = $"{Dir}/{name}{ext}";
        AssetDatabase.DeleteAsset(path);

        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(template), path))
        {
            throw new System.Exception($"CardLab2Builder: copy to {path} failed");
        }

        return AssetDatabase.LoadAssetAtPath<T>(path);
    }

    /// A lab card: a copy of `template` keeping its range, class, art and entries (areas included), renamed,
    /// re-costed and re-rarified, with `edit` adjusting the entries and an optional LabCost condition that
    /// takes 1 off the cost while it holds.
    private static CardData Make(string cardName, string template, string text, int cost, Rarity rarity,
                                 System.Action<SerializedProperty> edit, int condition = LabCost.None)
    {
        if (("|" + Cfg("skip", "") + "|").Contains("|" + cardName + "|")) { return null; }
        CardData card = CopyAsset<CardData>(LoadCard(template), "Lab " + cardName);
        SerializedObject so = new(card);
        so.FindProperty("<cardName>k__BackingField").stringValue = cardName;
        so.FindProperty("<description>k__BackingField").stringValue = text;
        so.FindProperty("<cost>k__BackingField").intValue = cost;
        so.FindProperty("<rarity>k__BackingField").intValue = (int)rarity;
        so.FindProperty("<excludeFromRewards>k__BackingField").boolValue = false;
        so.FindProperty("<upgradedForm>k__BackingField").objectReferenceValue = null;
        so.FindProperty("<labCondition>k__BackingField").intValue = condition;
        so.FindProperty("<labDiscount>k__BackingField").intValue = condition == LabCost.None ? 0 : 1;
        edit(so.FindProperty("<effectEntries>k__BackingField"));
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(card);
        made.Add(card);
        return card;
    }

    private static void Delta(SerializedProperty entries, int index, int delta) =>
        entries.GetArrayElementAtIndex(index).FindPropertyRelative("amountDelta").intValue += delta;

    private static void Swap(SerializedProperty entries, int index, CardEffect effect) =>
        entries.GetArrayElementAtIndex(index).FindPropertyRelative("effect").objectReferenceValue = effect;

    private static void AddDraw(SerializedProperty entries, CardEffect draw)
    {
        entries.arraySize++;
        SerializedProperty e = entries.GetArrayElementAtIndex(entries.arraySize - 1);
        e.FindPropertyRelative("effect").objectReferenceValue = draw;
        e.FindPropertyRelative("aimsAt").intValue = (int)EffectTarget.Source;
        e.FindPropertyRelative("amountDelta").intValue = 0;
        e.FindPropertyRelative("amountPercent").intValue = 0;
        e.FindPropertyRelative("area.kind").intValue = 0;
    }

    private static GameObject Totem(string name, TriggeringActionType trigger, CardEffect effect, string text)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Totem/EchoTotem.prefab");
        GameObject copy = CopyAsset<GameObject>(source, name);
        string path = AssetDatabase.GetAssetPath(copy);
        GameObject root = PrefabUtility.LoadPrefabContents(path);

        SerializedObject so = new(root.GetComponent<Totem>());
        so.FindProperty("auras").arraySize = 0;
        SerializedProperty r = so.FindProperty("reactions");
        r.arraySize = 1;
        SerializedProperty e = r.GetArrayElementAtIndex(0);
        e.FindPropertyRelative("trigger").intValue = (int)trigger;
        e.FindPropertyRelative("effect").objectReferenceValue = effect;
        e.FindPropertyRelative("description").stringValue = text;
        so.FindProperty("affects").intValue = 0;
        so.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject body = new(root.GetComponent<Character>());
        body.FindProperty("maxHealth").intValue = 10;
        body.FindProperty("<Health>k__BackingField").intValue = 10;
        body.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static GameObject CopyTotem(string template, string name, int health)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Totem/{template}.prefab");
        GameObject copy = CopyAsset<GameObject>(source, name);
        string path = AssetDatabase.GetAssetPath(copy);
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        SerializedObject body = new(root.GetComponent<Character>());
        body.FindProperty("maxHealth").intValue = health;
        body.FindProperty("<Health>k__BackingField").intValue = health;
        body.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static void TotemCard(string cardName, string template, GameObject totem, string text, int requiredClass)
    {
        CardData source = LoadCard(template);
        CardEffect summonTemplate = new SerializedObject(source).FindProperty("<effectEntries>k__BackingField")
            .GetArrayElementAtIndex(0).FindPropertyRelative("effect").objectReferenceValue as CardEffect;
        SummonEffect summon = CopyAsset<SummonEffect>(summonTemplate, "Lab " + cardName + " Summon");
        SerializedObject se = new(summon);
        se.FindProperty("summonedObject").objectReferenceValue = totem;
        se.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(summon);

        CardData card = Make(cardName, template, text, 2, Rarity.Rare, e => Swap(e, 0, summon));

        if (card != null && requiredClass >= 0) { SetClass(card, requiredClass); }
    }
}
