using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// CARD LAB 3 (bot-mirror only, never in the real project). Rebuilds the Knight and Cleric reward pools:
/// every starter card and every draftable card at or below starter power is excluded from rewards, and
/// the lab cards below - stronger versions of the staples, and combo cards that cost less while a
/// condition holds (LabCost) and draw - are added to AllCards. Rare totems (existing and new) are set to
/// 10 health by the variant's text patches; the new lab totems are built at 10 here.
/// Starter decks are untouched: they are meant to sit below the curve.
/// </summary>
public static class CardLab4Builder
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
        Debug.Log("CardLab4Builder config: " + string.Join("; ", config));
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
        if (Cfg("cards", "1") == "0") { Debug.Log("CardLab4Builder: cards=0, no lab cards"); ClearTable(); AssetDatabase.SaveAssets(); return; }
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
        Make("Totem Strike", "Slash", $"Deal {9 + N("Totem Strike.plus", 0)} damage. Draw 1. Costs 0 next to a totem.", 1, Rarity.Uncommon,
             e => { Delta(e, 0, N("Totem Strike.plus", 0)); AddDraw(e, draw1); }, LabCost.NextToTotem);
        Make("Shield Strike", "Slash", "Deal 8 damage. Draw 1. Costs 0 while you have Shield.", 1, Rarity.Uncommon,
             e => { Delta(e, 0, -1); AddDraw(e, draw1); }, LabCost.HasShield);
        Make("Brothers in Arms", "Shield", $"Gain {7 + N("Brothers in Arms.plus", 0)} Shield. Draw 1. Costs 0 next to another hero.", 1, Rarity.Uncommon,
             e => { Delta(e, 0, N("Brothers in Arms.plus", 0)); AddDraw(e, draw1); }, LabCost.NextToHero);
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
        Make("Blessed Shield", "Blade Ward", $"Give {8 + N("Blessed Shield.plus", 0)} Shield.", 1, Rarity.Uncommon, e => { Swap(e, 0, shield7); Delta(e, 0, 1 + N("Blessed Shield.plus", 0)); });
        Make("Lasting Renewal", "Renewing Touch", "Apply Regeneration 4.", 1, Rarity.Uncommon, e => { });

        // ---------------------------------------------------------------- Cleric: combo (cheaper + draw)
        Make("Prayer Circle", "Heal Word", "Heal 5. Draw 1. Costs 0 next to another hero.", 1, Rarity.Uncommon,
             e => AddDraw(e, draw1), LabCost.NextToHero);
        Make("Shrine Blessing", "Blade Ward", $"Give {6 + N("Shrine Blessing.plus", 0)} Shield. Draw 1. Costs 0 next to a totem.", 1, Rarity.Uncommon,
             e => { Swap(e, 0, shield7); Delta(e, 0, -1 + N("Shrine Blessing.plus", 0)); AddDraw(e, draw1); }, LabCost.NextToTotem);
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

        // ---------------------------------------------------------------- L5: adjacency cards (cards=5 and up)
        if (int.Parse(Cfg("wave", "1")) >= 5) { Adjacency(cleric); }

        // ---------------------------------------------------------------- LAB 4: taunts and "alone" cards
        if (Cfg("iso", "1") != "0") { Isolation(cleric); }

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
        Debug.Log($"CardLab4Builder: {made.Count} lab cards made, library now {library.Cards.Count}, {dropped.Length} cards excluded");
    }

    // ------------------------------------------------------------------ L5: adjacency

    private const int NextTo = LabCost.NextToHero;
    private const int SelfTile = -1;

    /// Numbers live in N(name, fallback) so a round's retune is a config line (n.Flanking Strike.bonus=5),
    /// not a code edit - see cardlab3.py, which writes them.
    private static int N(string key, int fallback) => int.Parse(Cfg("n." + key, fallback.ToString()));

    private static void Adjacency(int cleric)
    {
        int knight = ClassOf(LoadCard("Slash"));

        // ------------------------------------------------ Knight
        Craft("Shoulder to Shoulder", "Shield", knight, 1, Rarity.Uncommon, LabCost.None, SelfTile,
              $"You and allies next to you gain {N("Shoulder to Shoulder.shield", 6)} Shield.",
              e => Entry(e, "Shield 7", EffectTarget.Source, N("Shoulder to Shoulder.shield", 6) - 7, 0, 1));
        Craft("Flanking Strike", "Slash", knight, 1, Rarity.Uncommon, LabCost.None, 0,
              $"Deal {N("Flanking Strike.damage", 8)} damage" + (N("Flanking Strike.shield", 0) > 0 ? $" and gain {N("Flanking Strike.shield", 0)} Shield" : "") + $". Next to another hero: deal {N("Flanking Strike.damage", 8) + N("Flanking Strike.bonus", 4)}" + (N("Flanking Strike.draw", 0) > 0 ? " and draw 1." : " instead."),
              e =>
              {
                  Entry(e, "Damage 9", EffectTarget.PlayedTile, N("Flanking Strike.damage", 8) - 9, cond: NextTo, bonus: N("Flanking Strike.bonus", 4));
                  if (N("Flanking Strike.shield", 0) > 0) { Entry(e, "Shield 5", EffectTarget.Source, N("Flanking Strike.shield", 0) - 5); }
                  if (N("Flanking Strike.draw", 0) > 0) { Entry(e, "Draw 1", EffectTarget.Source, 0, cond: NextTo); }
              });
        Craft("Covering Strike", "Slash", knight, 1, Rarity.Uncommon, LabCost.None, 0,
              $"Deal {N("Covering Strike.damage", 8)} damage. Allies next to you gain {N("Covering Strike.shield", 5)} Shield.",
              e =>
              {
                  Entry(e, "Damage 9", EffectTarget.PlayedTile, N("Covering Strike.damage", 8) - 9);
                  Entry(e, "Shield 5", EffectTarget.Source, N("Covering Strike.shield", 5) - 5, 1, 1);
              });
        CardData fallIn = Craft("Fall In", "Move", knight, N("Fall In.cost", 1), Rarity.Uncommon, LabCost.None, 0,
              $"Move up to 2 tiles. Then you and heroes next to you gain {N("Fall In.shield", 5)} Shield." + (N("Fall In.draw", 0) > 0 ? " Draw 1." : ""),
              e =>
              {
                  Entry(e, "Move", EffectTarget.PlayedTile, 0);
                  EntryAsset(e, AfterMove("Fall In", LabAfterMoveEffect.GainShield, N("Fall In.shield", 5), N("Fall In.draw", 0)), EffectTarget.Source, 0);
              });
        SetRange(fallIn, RangeShape.Chebyshev, 1, 2, true);
        Craft("Phalanx", "Block", knight, 1, Rarity.Uncommon, LabCost.None, SelfTile,
              $"You and allies next to you gain {N("Phalanx.block", 2)} Block.",
              e => Entry(e, "Block 2", EffectTarget.Source, N("Phalanx.block", 2) - 2, 0, 1));
        Craft("Guardian's Stance", "Block", knight, 1, Rarity.Uncommon, LabCost.None, SelfTile,
              $"Gain {N("Guardian's Stance.block", 2)} Block. Next to another hero: gain {N("Guardian's Stance.block", 2) + N("Guardian's Stance.bonus", 2)} instead.",
              e => Entry(e, "Block 2", EffectTarget.Source, N("Guardian's Stance.block", 2) - 2, cond: NextTo, bonus: N("Guardian's Stance.bonus", 2)));
        Craft("Hold the Line", "Shield", knight, 2, Rarity.Rare, LabCost.None, SelfTile,
              $"You and allies next to you gain {N("Hold the Line.shield", 10)} Shield. Draw 1.",
              e =>
              {
                  Entry(e, "Shield 10", EffectTarget.Source, N("Hold the Line.shield", 10) - 10, 0, 1);
                  Entry(e, "Draw 1", EffectTarget.Source, 0);
              });
        Craft("Vanguard's Oath", "Slash", knight, 1, Rarity.Rare, NextTo, 0,
              N("Vanguard's Oath.drawalways", 0) > 0 ? $"Deal {N("Vanguard's Oath.damage", 10)} damage. Draw 1. Costs 0 next to another hero." : $"Deal {N("Vanguard's Oath.damage", 10)} damage. Next to another hero: costs 0 and draw 1.",
              e =>
              {
                  Entry(e, "Damage 9", EffectTarget.PlayedTile, N("Vanguard's Oath.damage", 10) - 9);
                  Entry(e, "Draw 1", EffectTarget.Source, 0, cond: N("Vanguard's Oath.drawalways", 0) > 0 ? 0 : NextTo);
              });

        // ------------------------------------------------ Cleric
        Craft("Laying On of Hands", "Heal Word", cleric, 1, Rarity.Uncommon, LabCost.None, SelfTile,
              $"Heal {N("Laying On of Hands.heal", 5)} to you and allies next to you.",
              e => Entry(e, "Heal 5", EffectTarget.Source, N("Laying On of Hands.heal", 5) - 5, 0, 1));
        Craft("Guiding Smite", "Smite", cleric, 1, Rarity.Uncommon, LabCost.None, 0,
              $"Deal {N("Guiding Smite.damage", 6)} damage. You and allies next to you heal {N("Guiding Smite.heal", 3)}.",
              e =>
              {
                  Entry(e, "Damage 4", EffectTarget.PlayedTile, N("Guiding Smite.damage", 6) - 4);
                  Entry(e, "Heal 3", EffectTarget.Source, N("Guiding Smite.heal", 3) - 3, 0, 1);
              });
        CardData step = Craft("Sanctuary Step", "Move", cleric, N("Sanctuary Step.cost", 1), Rarity.Uncommon, LabCost.None, 0,
              $"Move up to 2 tiles. Then you and heroes next to you heal {N("Sanctuary Step.heal", 4)}." + (N("Sanctuary Step.draw", 0) > 0 ? " Draw 1." : ""),
              e =>
              {
                  Entry(e, "Move", EffectTarget.PlayedTile, 0);
                  EntryAsset(e, AfterMove("Sanctuary Step", LabAfterMoveEffect.Heal, N("Sanctuary Step.heal", 4), N("Sanctuary Step.draw", 0)), EffectTarget.Source, 0);
              });
        SetRange(step, RangeShape.Chebyshev, 1, 2, true);
        Craft("Shield of Faith", "Heal Word", cleric, 1, Rarity.Uncommon, LabCost.None, 0,
              $"Give {N("Shield of Faith.shield", 5)} Shield. Next to another hero: give {N("Shield of Faith.shield", 5) + N("Shield of Faith.bonus", 4)}" + (N("Shield of Faith.draw", 0) > 0 ? " and draw 1." : " instead."),
              e =>
              {
                  Entry(e, "Shield 5", EffectTarget.PlayedTile, N("Shield of Faith.shield", 5) - 5, cond: NextTo, bonus: N("Shield of Faith.bonus", 4));
                  if (N("Shield of Faith.draw", 0) > 0) { Entry(e, "Draw 1", EffectTarget.Source, 0, cond: NextTo); }
              });
        Craft("Close Prayer", "Heal Word", cleric, 1, Rarity.Uncommon, LabCost.None, 0,
              $"Heal {N("Close Prayer.heal", 5)}. Next to another hero: draw 1.",
              e =>
              {
                  Entry(e, "Heal 5", EffectTarget.PlayedTile, N("Close Prayer.heal", 5) - 5);
                  Entry(e, "Draw 1", EffectTarget.Source, 0, cond: NextTo);
              });
        Craft("Benediction", "Heal Word", cleric, 1, Rarity.Uncommon, LabCost.None, 0,
              (N("Benediction.heal", 0) > 0 ? $"Heal {N("Benediction.heal", 0)} to an ally and every ally next to them." : $"Give {N("Benediction.shield", 4)} Shield to an ally and every ally next to them.") + (N("Benediction.draw", 0) > 0 ? " Draw 1." : ""),
              e =>
              {
                  if (N("Benediction.heal", 0) > 0) { Entry(e, "Heal 5", EffectTarget.PlayedTile, N("Benediction.heal", 0) - 5, 0, 1); }
                  else { Entry(e, "Shield 4", EffectTarget.PlayedTile, N("Benediction.shield", 4) - 4, 0, 1); }
                  if (N("Benediction.draw", 0) > 0) { Entry(e, "Draw 1", EffectTarget.Source, 0); }
              });
        Craft("Smite Together", "Smite", cleric, 1, Rarity.Uncommon, LabCost.None, 0,
              $"Deal {N("Smite Together.damage", 6)} damage. Heal 3. Next to another hero: deal {N("Smite Together.damage", 6) + N("Smite Together.bonus", 3)} instead.",
              e =>
              {
                  Entry(e, "Damage 4", EffectTarget.PlayedTile, N("Smite Together.damage", 6) - 4, cond: NextTo, bonus: N("Smite Together.bonus", 3));
                  Entry(e, "Heal 3", EffectTarget.Source, 0);
              });
        Craft("Sanctified Ground", "Heal Word", cleric, N("Sanctified Ground.cost", 2), Rarity.Rare, LabCost.None, SelfTile,
              $"You and allies next to you gain {N("Sanctified Ground.shield", 6)} Shield and heal {N("Sanctified Ground.heal", 6)}.",
              e =>
              {
                  Entry(e, "Shield 7", EffectTarget.Source, N("Sanctified Ground.shield", 6) - 7, 0, 1);
                  Entry(e, "Heal 5", EffectTarget.Source, N("Sanctified Ground.heal", 6) - 5, 0, 1);
              });
    }

    // ------------------------------------------------------------------ LAB 4: taunts and alone

    /// "Alone" = every other hero at least `alone` tiles away (config alone=2/3/4; LabCost.AloneBase + it).
    private static void Isolation(int cleric)
    {
        int knight = ClassOf(LoadCard("Slash"));
        int dist = int.Parse(Cfg("alone", "2"));
        int A = LabCost.AloneBase + dist;
        Debug.Log($"CardLab4Builder: alone = every other hero at least {dist} tiles away (condition {A})");

        // ------------------------------------------------ Knight: taunts (no condition)
        Craft("Challenge", "Taunt", knight, 1, Rarity.Uncommon, LabCost.None, 0,
              $"Taunt an enemy. Gain {N("Challenge.shield", 7)} Shield.",
              e =>
              {
                  Entry(e, "Taunt", EffectTarget.PlayedTile, 0);
                  Entry(e, "Shield 7", EffectTarget.Source, N("Challenge.shield", 7) - 7);
              });
        Craft("Provoke", "Taunt", knight, 1, Rarity.Uncommon, LabCost.None, SelfTile,
              $"Taunt every enemy within 2. Gain {N("Provoke.block", 2)} Block.",
              e =>
              {
                  Entry(e, "Taunt", EffectTarget.Source, 0, 1, 2);
                  Entry(e, "Block 2", EffectTarget.Source, N("Provoke.block", 2) - 2);
              });
        Craft("Taunting Blow", "Slash", knight, 1, Rarity.Uncommon, LabCost.None, 0,
              $"Deal {N("Taunting Blow.damage", 8)} damage. Taunt the target.",
              e =>
              {
                  Entry(e, "Damage 9", EffectTarget.PlayedTile, N("Taunting Blow.damage", 8) - 9);
                  Entry(e, "Taunt", EffectTarget.PlayedTile, 0);
              });
        Craft("Defiant Roar", "Taunt", knight, N("Defiant Roar.cost", 2), Rarity.Rare, LabCost.None, SelfTile,
              $"Taunt every enemy within 2. Gain {N("Defiant Roar.shield", 12)} Shield. Draw 1.",
              e =>
              {
                  Entry(e, "Taunt", EffectTarget.Source, 0, 1, 2);
                  Entry(e, "Shield 10", EffectTarget.Source, N("Defiant Roar.shield", 12) - 10);
                  Entry(e, "Draw 1", EffectTarget.Source, 0);
              });

        // ------------------------------------------------ Knight: alone
        Craft("Lone Stand", "Shield", knight, 1, Rarity.Uncommon, LabCost.None, SelfTile,
              $"Gain {N("Lone Stand.shield", 6)} Shield. Alone: gain {N("Lone Stand.shield", 6) + N("Lone Stand.bonus", 6)} instead.",
              e => Entry(e, "Shield 7", EffectTarget.Source, N("Lone Stand.shield", 6) - 7, cond: A, bonus: N("Lone Stand.bonus", 6)));
        Craft("Solitary Guard", "Block", knight, 1, Rarity.Uncommon, LabCost.None, SelfTile,
              $"Gain {N("Solitary Guard.block", 2)} Block. Alone: gain {N("Solitary Guard.block", 2) + N("Solitary Guard.bonus", 2)} instead.",
              e => Entry(e, "Block 2", EffectTarget.Source, N("Solitary Guard.block", 2) - 2, cond: A, bonus: N("Solitary Guard.bonus", 2)));
        Craft("Lone Wolf", "Slash", knight, 1, Rarity.Uncommon, LabCost.None, 0,
              $"Deal {N("Lone Wolf.damage", 8)} damage. Alone: deal {N("Lone Wolf.damage", 8) + N("Lone Wolf.bonus", 5)}" + (N("Lone Wolf.draw", 0) > 0 ? " and draw 1." : " instead."),
              e =>
              {
                  Entry(e, "Damage 9", EffectTarget.PlayedTile, N("Lone Wolf.damage", 8) - 9, cond: A, bonus: N("Lone Wolf.bonus", 5));
                  if (N("Lone Wolf.draw", 0) > 0) { Entry(e, "Draw 1", EffectTarget.Source, 0, cond: A); }
              });
        Craft("Last Bastion", "Taunt", knight, 1, Rarity.Rare, LabCost.None, SelfTile,
              $"Taunt every enemy within 2. Gain {N("Last Bastion.shield", 6)} Shield. Alone: gain {N("Last Bastion.shield", 6) + N("Last Bastion.bonus", 6)} and draw 1.",
              e =>
              {
                  Entry(e, "Taunt", EffectTarget.Source, 0, 1, 2);
                  Entry(e, "Shield 7", EffectTarget.Source, N("Last Bastion.shield", 6) - 7, cond: A, bonus: N("Last Bastion.bonus", 6));
                  Entry(e, "Draw 1", EffectTarget.Source, 0, cond: A);
              });
        CardData outrider = Craft("Outrider", "Move", knight, N("Outrider.cost", 0), Rarity.Uncommon, LabCost.None, 0,
              $"Move up to 2 tiles. Then, if alone, gain {N("Outrider.shield", 6)} Shield and draw 1.",
              e =>
              {
                  Entry(e, "Move", EffectTarget.PlayedTile, 0);
                  EntryAsset(e, AfterMove("Outrider", LabAfterMoveEffect.GainShield, N("Outrider.shield", 6), 1, A), EffectTarget.Source, 0);
              });
        SetRange(outrider, RangeShape.Chebyshev, 1, 2, true);
        Craft("Iron Will", "Shield", knight, 1, Rarity.Uncommon, A, SelfTile,
              $"Gain {N("Iron Will.shield", 7)} Shield. Draw 1. Costs 0 when alone.",
              e =>
              {
                  Entry(e, "Shield 7", EffectTarget.Source, N("Iron Will.shield", 7) - 7);
                  Entry(e, "Draw 1", EffectTarget.Source, 0);
              });

        // ------------------------------------------------ Cleric: alone
        Craft("Hermit's Smite", "Smite", cleric, 1, Rarity.Uncommon, LabCost.None, 0,
              $"Deal {N("Hermit's Smite.damage", 7)} damage. Heal 3. Alone: deal {N("Hermit's Smite.damage", 7) + N("Hermit's Smite.bonus", 4)} instead.",
              e =>
              {
                  Entry(e, "Damage 4", EffectTarget.PlayedTile, N("Hermit's Smite.damage", 7) - 4, cond: A, bonus: N("Hermit's Smite.bonus", 4));
                  Entry(e, "Heal 3", EffectTarget.Source, 0);
              });
        Craft("Distant Grace", "Heal Word", cleric, 1, Rarity.Uncommon, LabCost.None, 0,
              $"Give {N("Distant Grace.shield", 6)} Shield. Alone: give {N("Distant Grace.shield", 6) + N("Distant Grace.bonus", 4)} instead.",
              e => Entry(e, "Shield 5", EffectTarget.PlayedTile, N("Distant Grace.shield", 6) - 5, cond: A, bonus: N("Distant Grace.bonus", 4)));
        Craft("Far Mend", "Heal Word", cleric, 1, Rarity.Uncommon, LabCost.None, 0,
              $"Heal {N("Far Mend.heal", 6)}. Alone: draw 1.",
              e =>
              {
                  Entry(e, "Heal 5", EffectTarget.PlayedTile, N("Far Mend.heal", 6) - 5);
                  Entry(e, "Draw 1", EffectTarget.Source, 0, cond: A);
              });
        CardData withdraw = Craft("Withdraw", "Move", cleric, N("Withdraw.cost", 0), Rarity.Uncommon, LabCost.None, 0,
              $"Move up to 2 tiles. Then, if alone, heal {N("Withdraw.heal", 4)} and draw 1.",
              e =>
              {
                  Entry(e, "Move", EffectTarget.PlayedTile, 0);
                  EntryAsset(e, AfterMove("Withdraw", LabAfterMoveEffect.Heal, N("Withdraw.heal", 4), 1, A), EffectTarget.Source, 0);
              });
        SetRange(withdraw, RangeShape.Chebyshev, 1, 2, true);
        Craft("Vigil", "Heal Word", cleric, 2, Rarity.Rare, A, 0,
              $"Heal {N("Vigil.heal", 10)}. Draw 1. Costs 1 when alone.",
              e =>
              {
                  Entry(e, "Heal 10", EffectTarget.PlayedTile, N("Vigil.heal", 10) - 10);
                  Entry(e, "Draw 1", EffectTarget.Source, 0);
              });
        Craft("Judgement", "Smite", cleric, 1, Rarity.Rare, A, 0,
              $"Deal {N("Judgement.damage", 10)} damage. Alone: costs 0 and draw 1.",
              e =>
              {
                  Entry(e, "Damage 9", EffectTarget.PlayedTile, N("Judgement.damage", 10) - 9);
                  Entry(e, "Draw 1", EffectTarget.Source, 0, cond: A);
              });
    }


    /// A lab card copied from `template` (art, tags, animation, range) with its entries rebuilt by `entries`.
    /// `range` SelfTile sets a self-only range; 0 keeps the template's.
    private static CardData Craft(string cardName, string template, int requiredClass, int cost, Rarity rarity, int condition,
                                  int range, string text, System.Action<SerializedProperty> entries)
    {
        CardData card = Make(cardName, template, text, cost, rarity, e => { e.arraySize = 0; entries(e); }, condition);
        if (card == null) { return null; }

        SetClass(card, requiredClass);
        if (range == SelfTile) { SetRange(card, RangeShape.SelfTile, 0, 0, false); }
        return card;
    }

    private static void Entry(SerializedProperty entries, string effect, EffectTarget aim, int delta,
                              int areaMin = -1, int areaMax = -1, int cond = 0, int bonus = 0) =>
        EntryAsset(entries, Load<CardEffect>(effect), aim, delta, areaMin, areaMax, cond, bonus);

    /// One effect entry; areaMax >= 0 makes it a Chebyshev ring areaMin..areaMax around its aim tile.
    private static void EntryAsset(SerializedProperty entries, CardEffect effect, EffectTarget aim, int delta,
                                   int areaMin = -1, int areaMax = -1, int cond = 0, int bonus = 0)
    {
        entries.arraySize++;
        SerializedProperty e = entries.GetArrayElementAtIndex(entries.arraySize - 1);
        e.FindPropertyRelative("effect").objectReferenceValue = effect;
        e.FindPropertyRelative("aimsAt").intValue = (int)aim;
        e.FindPropertyRelative("amountDelta").intValue = delta;
        e.FindPropertyRelative("amountPercent").intValue = 0;
        e.FindPropertyRelative("labCondition").intValue = cond;
        e.FindPropertyRelative("labBonus").intValue = bonus;
        SerializedProperty area = e.FindPropertyRelative("area");
        area.FindPropertyRelative("kind").intValue = areaMax >= 0 ? (int)AreaKind.Radius : (int)AreaKind.Single;
        area.FindPropertyRelative("pattern").objectReferenceValue = null;
        SerializedProperty radius = area.FindPropertyRelative("radius");
        radius.FindPropertyRelative("shape").intValue = areaMax >= 0 ? (int)RangeShape.Chebyshev : 0;
        radius.FindPropertyRelative("minDistance").intValue = Mathf.Max(0, areaMin);
        radius.FindPropertyRelative("maxDistance").intValue = Mathf.Max(0, areaMax);
        radius.FindPropertyRelative("requiresRoute").boolValue = false;
    }

    private static void SetRange(CardData card, RangeShape shape, int min, int max, bool route)
    {
        if (card == null) { return; }

        SerializedObject so = new(card);
        SerializedProperty r = so.FindProperty("<range>k__BackingField");
        r.FindPropertyRelative("shape").intValue = (int)shape;
        r.FindPropertyRelative("minDistance").intValue = min;
        r.FindPropertyRelative("maxDistance").intValue = max;
        r.FindPropertyRelative("requiresRoute").boolValue = route;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(card);
    }

    private static LabAfterMoveEffect AfterMove(string cardName, int kind, int amount, int draw = 0, int condition = 0)
    {
        string path = $"{Dir}/Lab {cardName} After Move.asset";
        AssetDatabase.DeleteAsset(path);
        LabAfterMoveEffect effect = ScriptableObject.CreateInstance<LabAfterMoveEffect>();
        AssetDatabase.CreateAsset(effect, path);
        SerializedObject so = new(effect);
        so.FindProperty("kind").intValue = kind;
        so.FindProperty("amount").intValue = amount;
        so.FindProperty("includeSelf").boolValue = true;
        so.FindProperty("draw").intValue = draw;
        so.FindProperty("condition").intValue = condition;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(effect);
        return AssetDatabase.LoadAssetAtPath<LabAfterMoveEffect>(path);
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

        throw new System.Exception($"CardLab4Builder: no {typeof(T).Name} named '{assetName}'");
    }

    private static CardData LoadCard(string assetName) => Load<CardData>(assetName);

    private static CardData LoadCardByCardName(string cardName)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:CardData"))
        {
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(guid));
            if (card != null && card.cardName == cardName) { return card; }
        }

        throw new System.Exception($"CardLab4Builder: no card called '{cardName}'");
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
            throw new System.Exception($"CardLab4Builder: copy to {path} failed");
        }

        return AssetDatabase.LoadAssetAtPath<T>(path);
    }

    /// A lab card: a copy of `template` keeping its range, class, art and entries (areas included), renamed,
    /// re-costed and re-rarified, with `edit` adjusting the entries and an optional LabCost condition that
    /// takes 1 off the cost while it holds.
    private static CardData Make(string cardName, string template, string text, int cost, Rarity rarity,
                                 System.Action<SerializedProperty> edit, int condition = LabCost.None, int discount = 1)
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
        so.FindProperty("<labDiscount>k__BackingField").intValue = condition == LabCost.None ? 0 : discount;
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
