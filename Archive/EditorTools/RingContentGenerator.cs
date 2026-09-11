using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Authors the 25-ring set from Assets/Myriad Aesthetics/25 Free Ring Icons - the first EquipmentData
/// assets in the project to carry a real icon. Every ring lives in EquipmentSlot.Ring, so equipping any
/// number of them stacks rather than replacing one another - see EquipmentSlots.IsUnlimited.
///
/// Repairs rather than skips, the same convention EquipmentExampleContent now follows: each asset is
/// reused if it already exists at its path and every field (and every modifier sub-asset) it owns is
/// rewritten on every run, so a half-built set from an interrupted pass, or a field hand-tuned in the
/// Inspector and later reverted, stays fixable by re-running this rather than only by deleting files.
///
/// Created in dependency order and every new asset is passed *down* as a live object rather than
/// re-loaded by path - never wrapped in AssetDatabase.StartAssetEditing(), which would defer every
/// import in the block and make LoadAssetAtPath return null for anything created in the same block; see
/// CLAUDE.md's note on TutorialContentGenerator for what that failure looks like in-game.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 without -IncludeEditor - verify
/// by focusing the Editor and checking the console.
/// </summary>
public static class RingContentGenerator
{
    private const string Folder = "Assets/Data/Equipment/Rings";
    private const string IconFolder = "Assets/Myriad Aesthetics/25 Free Ring Icons/PNG";

    [MenuItem("Tools/Equipment/Author Ring Set")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Ring set: exit Play Mode first.");
            return;
        }

        EnsureFolder(Folder);

        AuthorRimeguardBand();
        AuthorSerpentsCoil();
        AuthorAmberSignet();
        AuthorRingOfFocus();
        AuthorGlacierBand();
        AuthorAstrologersLoop();
        AuthorEmberwoodBand();
        AuthorWardensBraid();
        AuthorStormsplitRing();
        AuthorManaflowRing();
        AuthorDeathgripSignet();
        AuthorCascadeRing();
        AuthorSeraphsWard();
        AuthorVampiresToken();
        AuthorMarigoldBand();
        AuthorBloodshardRing();
        AuthorZephyrRing();
        AuthorSporecapRing();
        AuthorDeepcoralSignet();
        AuthorPanthersEye();
        AuthorBramblewreath();
        AuthorCinderforgeRing();
        AuthorOpalineCharm();
        AuthorHarlequinBand();
        AuthorDreadSovereign();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Ring set: done.");
    }

    private static void AuthorRimeguardBand()
    {
        EquipmentData item = BeginRing(1, "Rimeguard Band", "Permanently under the effect of Block 2.", Rarity.Uncommon);

        StatusGrantModifier grant = AddModifier<StatusGrantModifier>(item, so =>
        {
            so.FindProperty("type").intValue = (int)StatusType.Block;
            so.FindProperty("stacks").intValue = 2;
        });

        FinishRing(item, grant);
    }

    private static void AuthorSerpentsCoil()
    {
        EquipmentData item = BeginRing(2, "Serpent's Coil", "Permanently under the effect of Poison Blade.", Rarity.Uncommon);

        StatusGrantModifier grant = AddModifier<StatusGrantModifier>(item, so =>
        {
            so.FindProperty("type").intValue = (int)StatusType.PoisonBlade;
            so.FindProperty("stacks").intValue = 1;
        });

        FinishRing(item, grant);
    }

    private static void AuthorAmberSignet()
    {
        EquipmentData item = BeginRing(3, "Amber Signet", "-1 damage from every hit taken.", Rarity.Common);

        DamageBonusModifier bonus = AddModifier<DamageBonusModifier>(item, so =>
        {
            so.FindProperty("outgoing").intValue = 0;
            so.FindProperty("incomingReduction").intValue = 1;
        });

        FinishRing(item, bonus);
    }

    private static void AuthorRingOfFocus()
    {
        EquipmentData item = BeginRing(4, "Ring of Focus", "+1 energy each turn.", Rarity.Rare);

        ResourceModifier resource = AddModifier<ResourceModifier>(item, so =>
        {
            so.FindProperty("bonusEnergy").intValue = 1;
            so.FindProperty("bonusHandSize").intValue = 0;
        });

        FinishRing(item, resource);
    }

    private static void AuthorGlacierBand()
    {
        EquipmentData item = BeginRing(5, "Glacier Band", "Permanently under the effect of Shield 2.", Rarity.Uncommon);

        StatusGrantModifier grant = AddModifier<StatusGrantModifier>(item, so =>
        {
            so.FindProperty("type").intValue = (int)StatusType.Shield;
            so.FindProperty("stacks").intValue = 2;
        });

        FinishRing(item, grant);
    }

    private static void AuthorAstrologersLoop()
    {
        EquipmentData item = BeginRing(6, "Astrologer's Loop", "+1 card drawn each turn.", Rarity.Rare);

        ResourceModifier resource = AddModifier<ResourceModifier>(item, so =>
        {
            so.FindProperty("bonusEnergy").intValue = 0;
            so.FindProperty("bonusHandSize").intValue = 1;
        });

        FinishRing(item, resource);
    }

    private static void AuthorEmberwoodBand()
    {
        EquipmentData item = BeginRing(7, "Emberwood Band", "Fire cards cost 1 less.", Rarity.Uncommon);

        CostModifier cheaper = AddModifier<CostModifier>(item, so =>
        {
            so.FindProperty("delta").intValue = -1;
        });

        CardTuningModifier tuning = AddModifier<CardTuningModifier>(item, so =>
        {
            SetTagFilter(so, CardTag.Fire);
            SetSingleModifier(so, cheaper);
        });

        FinishRing(item, tuning);
    }

    private static void AuthorWardensBraid()
    {
        EquipmentData item = BeginRing(8, "Wardens' Braid", "+1 Poison whenever you apply Poison.", Rarity.Common);

        GrantBonusModifier bonus = AddModifier<GrantBonusModifier>(item, so =>
        {
            so.FindProperty("subject").intValue = (int)StatusType.Poison;
            so.FindProperty("bonus").intValue = 1;
        });

        FinishRing(item, bonus);
    }

    private static void AuthorStormsplitRing()
    {
        EquipmentData item = BeginRing(9, "Stormsplit Ring", "+1 damage per Strength stack you hold.", Rarity.Rare);

        ScalingDamageModifier scaling = AddModifier<ScalingDamageModifier>(item, so =>
        {
            so.FindProperty("subject").intValue = (int)StatusType.Strength;
            so.FindProperty("perStack").intValue = 1;
        });

        FinishRing(item, scaling);
    }

    private static void AuthorManaflowRing()
    {
        EquipmentData item = BeginRing(10, "Manaflow Ring", "Defence cards cost 1 less.", Rarity.Common);

        CostModifier cheaper = AddModifier<CostModifier>(item, so =>
        {
            so.FindProperty("delta").intValue = -1;
        });

        CardTuningModifier tuning = AddModifier<CardTuningModifier>(item, so =>
        {
            SetTagFilter(so, CardTag.Defence);
            SetSingleModifier(so, cheaper);
        });

        FinishRing(item, tuning);
    }

    private static void AuthorDeathgripSignet()
    {
        EquipmentData item = BeginRing(11, "Deathgrip Signet", "Killing an enemy grants 1 energy.", Rarity.Rare);

        KillRewardModifier reward = AddModifier<KillRewardModifier>(item, so =>
        {
            so.FindProperty("bonusEnergy").intValue = 1;
            so.FindProperty("grantedStatus").intValue = (int)StatusType.None;
            so.FindProperty("grantedStacks").intValue = 0;
        });

        FinishRing(item, reward);
    }

    private static void AuthorCascadeRing()
    {
        EquipmentData item = BeginRing(12, "Cascade Ring", "+1 Parry whenever you gain Parry.", Rarity.Uncommon);

        GrantBonusModifier bonus = AddModifier<GrantBonusModifier>(item, so =>
        {
            so.FindProperty("subject").intValue = (int)StatusType.Parry;
            so.FindProperty("bonus").intValue = 1;
        });

        FinishRing(item, bonus);
    }

    private static void AuthorSeraphsWard()
    {
        EquipmentData item = BeginRing(13, "Seraph's Ward",
            "Permanently under the effect of Shield 3, and +1 Shield whenever you gain Shield.", Rarity.Legendary);

        StatusGrantModifier grant = AddModifier<StatusGrantModifier>(item, so =>
        {
            so.FindProperty("type").intValue = (int)StatusType.Shield;
            so.FindProperty("stacks").intValue = 3;
        });

        GrantBonusModifier bonus = AddModifier<GrantBonusModifier>(item, so =>
        {
            so.FindProperty("subject").intValue = (int)StatusType.Shield;
            so.FindProperty("bonus").intValue = 1;
        });

        FinishRing(item, grant, bonus);
    }

    private static void AuthorVampiresToken()
    {
        EquipmentData item = BeginRing(14, "Vampire's Token", "Permanently under the effect of Lifesteal.", Rarity.Rare);

        StatusGrantModifier grant = AddModifier<StatusGrantModifier>(item, so =>
        {
            so.FindProperty("type").intValue = (int)StatusType.Lifesteal;
            so.FindProperty("stacks").intValue = 1;
        });

        FinishRing(item, grant);
    }

    private static void AuthorMarigoldBand()
    {
        EquipmentData item = BeginRing(15, "Marigold Band", "Permanently under the effect of Regeneration 2.", Rarity.Uncommon);

        StatusGrantModifier grant = AddModifier<StatusGrantModifier>(item, so =>
        {
            so.FindProperty("type").intValue = (int)StatusType.Regeneration;
            so.FindProperty("stacks").intValue = 2;
        });

        FinishRing(item, grant);
    }

    private static void AuthorBloodshardRing()
    {
        EquipmentData item = BeginRing(16, "Bloodshard Ring", "+3 damage while below half health.", Rarity.Rare);

        ConditionalDamageModifier conditional = AddModifier<ConditionalDamageModifier>(item, so =>
        {
            so.FindProperty("condition").intValue = (int)DamageCondition.BelowHalfHealth;
            so.FindProperty("bonus").intValue = 3;
        });

        FinishRing(item, conditional);
    }

    private static void AuthorZephyrRing()
    {
        EquipmentData item = BeginRing(17, "Zephyr Ring", "Movement cards reach 1 tile further.", Rarity.Uncommon);

        RangeModifier reach = AddModifier<RangeModifier>(item, so =>
        {
            so.FindProperty("maxDelta").intValue = 1;
        });

        CardTuningModifier tuning = AddModifier<CardTuningModifier>(item, so =>
        {
            SetTagFilter(so, CardTag.Movement);
            SetSingleModifier(so, reach);
        });

        FinishRing(item, tuning);
    }

    private static void AuthorSporecapRing()
    {
        EquipmentData item = BeginRing(18, "Sporecap Ring", "The Vulnerable you apply cuts 1 deeper.", Rarity.Uncommon);

        AppliedPotencyModifier potency = AddModifier<AppliedPotencyModifier>(item, so =>
        {
            so.FindProperty("subject").intValue = (int)StatusType.Vulnerable;
            so.FindProperty("bonus").intValue = 1;
        });

        FinishRing(item, potency);
    }

    private static void AuthorDeepcoralSignet()
    {
        EquipmentData item = BeginRing(19, "Deepcoral Signet", "+2 damage dealt, -1 damage taken.", Rarity.Legendary);

        DamageBonusModifier bonus = AddModifier<DamageBonusModifier>(item, so =>
        {
            so.FindProperty("outgoing").intValue = 2;
            so.FindProperty("incomingReduction").intValue = 1;
        });

        FinishRing(item, bonus);
    }

    private static void AuthorPanthersEye()
    {
        EquipmentData item = BeginRing(20, "Panther's Eye", "+3 damage while at full health.", Rarity.Rare);

        ConditionalDamageModifier conditional = AddModifier<ConditionalDamageModifier>(item, so =>
        {
            so.FindProperty("condition").intValue = (int)DamageCondition.AtFullHealth;
            so.FindProperty("bonus").intValue = 3;
        });

        FinishRing(item, conditional);
    }

    private static void AuthorBramblewreath()
    {
        EquipmentData item = BeginRing(21, "Bramblewreath", "Attackers take 2 damage.", Rarity.Uncommon);

        ThornsModifier thorns = AddModifier<ThornsModifier>(item, so =>
        {
            so.FindProperty("amount").intValue = 2;
        });

        FinishRing(item, thorns);
    }

    private static void AuthorCinderforgeRing()
    {
        EquipmentData item = BeginRing(22, "Cinderforge Ring", "Fire cards deal +3 damage.", Rarity.Rare);

        MagnitudeModifier magnitude = AddModifier<MagnitudeModifier>(item, so =>
        {
            SerializedProperty on = so.FindProperty("on");
            on.FindPropertyRelative("allEntries").boolValue = true;
            so.FindProperty("delta").intValue = 3;
        });

        CardTuningModifier tuning = AddModifier<CardTuningModifier>(item, so =>
        {
            SetTagFilter(so, CardTag.Fire);
            SetSingleModifier(so, magnitude);
        });

        FinishRing(item, tuning);
    }

    private static void AuthorOpalineCharm()
    {
        EquipmentData item = BeginRing(23, "Opaline Charm", "+1 energy and +1 card each turn.", Rarity.Legendary);

        ResourceModifier resource = AddModifier<ResourceModifier>(item, so =>
        {
            so.FindProperty("bonusEnergy").intValue = 1;
            so.FindProperty("bonusHandSize").intValue = 1;
        });

        FinishRing(item, resource);
    }

    private static void AuthorHarlequinBand()
    {
        EquipmentData item = BeginRing(24, "Harlequin Band", "+1 Dodge whenever you gain Dodge.", Rarity.Common);

        GrantBonusModifier bonus = AddModifier<GrantBonusModifier>(item, so =>
        {
            so.FindProperty("subject").intValue = (int)StatusType.Dodge;
            so.FindProperty("bonus").intValue = 1;
        });

        FinishRing(item, bonus);
    }

    private static void AuthorDreadSovereign()
    {
        EquipmentData item = BeginRing(25, "Dread Sovereign",
            "The Weaken you apply cuts 2 deeper. Killing an enemy grants 1 Strength.", Rarity.Legendary);

        AppliedPotencyModifier potency = AddModifier<AppliedPotencyModifier>(item, so =>
        {
            so.FindProperty("subject").intValue = (int)StatusType.Weaken;
            so.FindProperty("bonus").intValue = 2;
        });

        KillRewardModifier reward = AddModifier<KillRewardModifier>(item, so =>
        {
            so.FindProperty("bonusEnergy").intValue = 0;
            so.FindProperty("grantedStatus").intValue = (int)StatusType.Strength;
            so.FindProperty("grantedStacks").intValue = 1;
        });

        FinishRing(item, potency, reward);
    }

    // --- shared plumbing -----------------------------------------------------------------------

    private static void SetTagFilter(SerializedObject cardTuningModifier, CardTag tag)
    {
        SerializedProperty tags = cardTuningModifier.FindProperty("filter").FindPropertyRelative("tags");
        tags.arraySize = 1;
        tags.GetArrayElementAtIndex(0).intValue = (int)tag;
    }

    private static void SetSingleModifier(SerializedObject cardTuningModifier, CardModifier modifier)
    {
        SerializedProperty mods = cardTuningModifier.FindProperty("modifiers");
        mods.arraySize = 1;
        mods.GetArrayElementAtIndex(0).objectReferenceValue = modifier;
    }

    /// Creates the EquipmentData asset for ring `spriteNumber` if it does not exist yet, or loads and
    /// reuses it if it does - repair, not skip, matching EquipmentExampleContent's convention. Every
    /// ring is EquipmentSlot.Ring and CharacterClass.Any: rings are the unlimited slot and nothing in
    /// this set is class-restricted.
    private static EquipmentData BeginRing(int spriteNumber, string ringName, string description, Rarity rarity)
    {
        string path = $"{Folder}/{ringName}.asset";

        EquipmentData item = AssetDatabase.LoadAssetAtPath<EquipmentData>(path);

        if (item == null)
        {
            item = ScriptableObject.CreateInstance<EquipmentData>();
            AssetDatabase.CreateAsset(item, path);
        }

        Sprite icon = LoadIcon(spriteNumber);

        SerializedObject so = new(item);
        so.FindProperty("<equipmentName>k__BackingField").stringValue = ringName;
        so.FindProperty("<description>k__BackingField").stringValue = description;
        so.FindProperty("<icon>k__BackingField").objectReferenceValue = icon;
        so.FindProperty("<rarity>k__BackingField").intValue = (int)rarity;
        so.FindProperty("<requiredClass>k__BackingField").intValue = (int)CharacterClass.Any;
        so.FindProperty("<slot>k__BackingField").intValue = (int)EquipmentSlot.Ring;
        so.ApplyModifiedProperties();

        if (icon == null)
        {
            Debug.LogWarning($"Ring set: {ringName} - icon ring_{spriteNumber:000}.png not found at {IconFolder}.");
        }

        return item;
    }

    private static Sprite LoadIcon(int spriteNumber) =>
        AssetDatabase.LoadAssetAtPath<Sprite>($"{IconFolder}/ring_{spriteNumber:000}.png");

    /// Builds one modifier, configures it, and attaches it as a sub-asset of `owner` - reusing an
    /// existing sub-asset of type T already attached to `owner` if one is there, rather than adding a
    /// fresh duplicate every time this runs.
    private static T AddModifier<T>(EquipmentData owner, Action<SerializedObject> configure) where T : ScriptableObject
    {
        T instance = FindSubAsset<T>(owner);
        bool isNew = instance == null;

        if (isNew)
        {
            instance = ScriptableObject.CreateInstance<T>();
            instance.name = typeof(T).Name;
        }

        SerializedObject so = new(instance);
        configure(so);
        so.ApplyModifiedProperties();

        if (isNew) { AssetDatabase.AddObjectToAsset(instance, owner); }

        EditorUtility.SetDirty(instance);

        return instance;
    }

    /// The first sub-asset of type T already attached to `owner`, or null if there is none yet - what
    /// makes AddModifier idempotent instead of appending a new modifier sub-asset on every run.
    private static T FindSubAsset<T>(EquipmentData owner) where T : UnityEngine.Object
    {
        string path = AssetDatabase.GetAssetPath(owner);

        if (string.IsNullOrEmpty(path)) { return null; }

        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is T typed) { return typed; }
        }

        return null;
    }

    private static void FinishRing(EquipmentData item, params EquipmentModifier[] modifiers)
    {
        SerializedObject so = new(item);
        SerializedProperty modifiersProp = so.FindProperty("<modifiers>k__BackingField");
        modifiersProp.arraySize = modifiers.Length;

        for (int i = 0; i < modifiers.Length; i++)
        {
            modifiersProp.GetArrayElementAtIndex(i).objectReferenceValue = modifiers[i];
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(item);

        Debug.Log($"Ring set: repaired {AssetDatabase.GetAssetPath(item)}.");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) { return; }

        string parent = path.Substring(0, path.LastIndexOf('/'));
        string leaf = path.Substring(path.LastIndexOf('/') + 1);

        if (!AssetDatabase.IsValidFolder(parent)) { EnsureFolder(parent); }

        AssetDatabase.CreateFolder(parent, leaf);
    }
}
