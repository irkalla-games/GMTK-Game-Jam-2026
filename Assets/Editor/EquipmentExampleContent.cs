using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot authoring of starter equipment and one upgraded card variant, so the feature has something
/// real to playtest rather than just the machinery - the equipment counterpart to
/// AreaOfEffectExampleContent. Covers every EquipmentModifier type at least once: StatusGrantModifier,
/// GrantBonusModifier, DamageBonusModifier, SummonHealthModifier, and CardTuningModifier wrapping each
/// of AreaModifier/RangeModifier/CostModifier. More items following the same shape can be authored by
/// hand in the Inspector, or by extending this file.
///
/// Idempotent: each asset is only created if it does not already exist, and Slash.upgradedForm is only
/// set if it is not already pointing somewhere, so re-running after hand-tuning one of these will not
/// stomp it. Editor-only, and deliberately not covered by Tools/compile-check.ps1 without -IncludeEditor
/// - verify by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class EquipmentExampleContent
{
    private const string Folder = "Assets/Data/Equipment";

    [MenuItem("Tools/Equipment/Author Equipment Examples")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Equipment examples: exit Play Mode first.");
            return;
        }

        EnsureFolder(Folder);

        AuthorWhetstone();
        AuthorTowerShield();
        AuthorIronboundVambrace();
        AuthorHexbindersCord();
        AuthorAdrenalCharm();
        AuthorTotemAnchor();
        AuthorBlastingCap();
        AuthorLongLens();
        AuthorFeatherweightGrips();

        AuthorSlashUpgrade();

        TuneLootTable("Assets/Data/LootTable/EarlyGame.asset", 0.15f);
        TuneLootTable("Assets/Data/LootTable/EarlyLevelReward.asset", 0.25f);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Equipment examples: done.");
    }

    private static void AuthorWhetstone()
    {
        EquipmentData item = BeginEquipment(
            Folder + "/Whetstone.asset", "Whetstone", "+1 damage on every attack.",
            Rarity.Common, CharacterClass.Any);

        if (item == null) { return; }

        DamageBonusModifier bonus = AddModifier<DamageBonusModifier>(item, so =>
        {
            so.FindProperty("outgoing").intValue = 1;
        });

        FinishEquipment(item, bonus);
    }

    private static void AuthorTowerShield()
    {
        EquipmentData item = BeginEquipment(
            Folder + "/Tower Shield.asset", "Tower Shield", "+2 Shield whenever you gain Shield.",
            Rarity.Common, CharacterClass.Any);

        if (item == null) { return; }

        GrantBonusModifier bonus = AddModifier<GrantBonusModifier>(item, so =>
        {
            so.FindProperty("subject").intValue = (int)StatusType.Shield;
            so.FindProperty("bonus").intValue = 2;
        });

        FinishEquipment(item, bonus);
    }

    private static void AuthorIronboundVambrace()
    {
        EquipmentData item = BeginEquipment(
            Folder + "/Ironbound Vambrace.asset", "Ironbound Vambrace", "+1 Block whenever you gain Block.",
            Rarity.Uncommon, CharacterClass.Any);

        if (item == null) { return; }

        GrantBonusModifier bonus = AddModifier<GrantBonusModifier>(item, so =>
        {
            so.FindProperty("subject").intValue = (int)StatusType.Block;
            so.FindProperty("bonus").intValue = 1;
        });

        FinishEquipment(item, bonus);
    }

    private static void AuthorHexbindersCord()
    {
        EquipmentData item = BeginEquipment(
            Folder + "/Hexbinder's Cord.asset", "Hexbinder's Cord", "+1 Weaken whenever you apply Weaken.",
            Rarity.Uncommon, CharacterClass.Any);

        if (item == null) { return; }

        GrantBonusModifier bonus = AddModifier<GrantBonusModifier>(item, so =>
        {
            so.FindProperty("subject").intValue = (int)StatusType.Weaken;
            so.FindProperty("bonus").intValue = 1;
        });

        FinishEquipment(item, bonus);
    }

    private static void AuthorAdrenalCharm()
    {
        EquipmentData item = BeginEquipment(
            Folder + "/Adrenal Charm.asset", "Adrenal Charm", "Permanently under the effect of Strength 1.",
            Rarity.Common, CharacterClass.Any);

        if (item == null) { return; }

        StatusGrantModifier grant = AddModifier<StatusGrantModifier>(item, so =>
        {
            so.FindProperty("type").intValue = (int)StatusType.Strength;
            so.FindProperty("stacks").intValue = 1;
        });

        FinishEquipment(item, grant);
    }

    private static void AuthorTotemAnchor()
    {
        EquipmentData item = BeginEquipment(
            Folder + "/Totem Anchor.asset", "Totem Anchor", "Totems you summon have +7 max health.",
            Rarity.Uncommon, CharacterClass.Any);

        if (item == null) { return; }

        SummonHealthModifier bonus = AddModifier<SummonHealthModifier>(item, so =>
        {
            so.FindProperty("bonus").intValue = 7;
            so.FindProperty("totemsOnly").boolValue = true;
        });

        FinishEquipment(item, bonus);
    }

    private static void AuthorBlastingCap()
    {
        EquipmentData item = BeginEquipment(
            Folder + "/Blasting Cap.asset", "Blasting Cap", "Fire cards gain a 3x3 splash.",
            Rarity.Rare, CharacterClass.Any);

        if (item == null) { return; }

        AreaModifier splash = AddModifier<AreaModifier>(item, so =>
        {
            SerializedProperty on = so.FindProperty("on");
            on.FindPropertyRelative("allEntries").boolValue = true;

            SerializedProperty area = so.FindProperty("area");
            area.FindPropertyRelative("kind").intValue = (int)AreaKind.Radius;

            SerializedProperty radius = area.FindPropertyRelative("radius");
            radius.FindPropertyRelative("shape").intValue = (int)RangeShape.Chebyshev;
            radius.FindPropertyRelative("minDistance").intValue = 0;
            radius.FindPropertyRelative("maxDistance").intValue = 1;
        });

        CardTuningModifier tuning = AddModifier<CardTuningModifier>(item, so =>
        {
            SerializedProperty tags = so.FindProperty("filter").FindPropertyRelative("tags");
            tags.arraySize = 1;
            tags.GetArrayElementAtIndex(0).intValue = (int)CardTag.Fire;

            SerializedProperty mods = so.FindProperty("modifiers");
            mods.arraySize = 1;
            mods.GetArrayElementAtIndex(0).objectReferenceValue = splash;
        });

        FinishEquipment(item, tuning);
    }

    private static void AuthorLongLens()
    {
        EquipmentData item = BeginEquipment(
            Folder + "/Long Lens.asset", "Long Lens", "Attack cards gain +1 range.",
            Rarity.Rare, CharacterClass.Any);

        if (item == null) { return; }

        RangeModifier reach = AddModifier<RangeModifier>(item, so =>
        {
            so.FindProperty("maxDelta").intValue = 1;
        });

        CardTuningModifier tuning = AddModifier<CardTuningModifier>(item, so =>
        {
            SerializedProperty tags = so.FindProperty("filter").FindPropertyRelative("tags");
            tags.arraySize = 1;
            tags.GetArrayElementAtIndex(0).intValue = (int)CardTag.Attack;

            SerializedProperty mods = so.FindProperty("modifiers");
            mods.arraySize = 1;
            mods.GetArrayElementAtIndex(0).objectReferenceValue = reach;
        });

        FinishEquipment(item, tuning);
    }

    private static void AuthorFeatherweightGrips()
    {
        EquipmentData item = BeginEquipment(
            Folder + "/Featherweight Grips.asset", "Featherweight Grips", "Movement cards cost 1 less.",
            Rarity.Rare, CharacterClass.Any);

        if (item == null) { return; }

        CostModifier cheaper = AddModifier<CostModifier>(item, so =>
        {
            so.FindProperty("delta").intValue = -1;
        });

        CardTuningModifier tuning = AddModifier<CardTuningModifier>(item, so =>
        {
            SerializedProperty tags = so.FindProperty("filter").FindPropertyRelative("tags");
            tags.arraySize = 1;
            tags.GetArrayElementAtIndex(0).intValue = (int)CardTag.Movement;

            SerializedProperty mods = so.FindProperty("modifiers");
            mods.arraySize = 1;
            mods.GetArrayElementAtIndex(0).objectReferenceValue = cheaper;
        });

        FinishEquipment(item, tuning);
    }

    /// <summary>
    /// Slash+ - an ordinary CardData asset built by copying Slash's own authored fields rather than
    /// guessing at them, then adding +3 to its one entry's amountDelta so it deals more without needing
    /// its own Damage asset - see CardEffectEntry.amountDelta. Proves the same mechanism Part 1 built
    /// for equipment-driven magnitude changes also drives hand-authored upgrade variants.
    /// </summary>
    private static void AuthorSlashUpgrade()
    {
        const string slashPath = "Assets/Data/CardData/Knight/Melee Attack/Slash.asset";
        const string slashPlusPath = "Assets/Data/CardData/Knight/Melee Attack/Slash+.asset";

        CardData slash = AssetDatabase.LoadAssetAtPath<CardData>(slashPath);

        if (slash == null)
        {
            Debug.LogWarning($"Equipment examples: {slashPath} not found - Slash+ upgrade skipped.");
            return;
        }

        SerializedObject slashSo = new(slash);
        SerializedProperty upgradedForm = slashSo.FindProperty("<upgradedForm>k__BackingField");

        if (upgradedForm.objectReferenceValue != null) { return; }

        CardData plus = AssetDatabase.LoadAssetAtPath<CardData>(slashPlusPath);

        if (plus == null)
        {
            plus = ScriptableObject.CreateInstance<CardData>();
            AssetDatabase.CreateAsset(plus, slashPlusPath);

            SerializedObject plusSo = new(plus);

            plusSo.FindProperty("<cardName>k__BackingField").stringValue = "Slash+";
            plusSo.FindProperty("<cost>k__BackingField").intValue =
                slashSo.FindProperty("<cost>k__BackingField").intValue;

            CopyRange(slashSo.FindProperty("<range>k__BackingField"), plusSo.FindProperty("<range>k__BackingField"));

            plusSo.FindProperty("<requiredClass>k__BackingField").intValue =
                slashSo.FindProperty("<requiredClass>k__BackingField").intValue;
            plusSo.FindProperty("<rarity>k__BackingField").intValue = (int)Rarity.NotOffered;
            plusSo.FindProperty("<description>k__BackingField").stringValue = "Deals 3 more damage than Slash.";

            SerializedProperty srcEntries = slashSo.FindProperty("<effectEntries>k__BackingField");
            SerializedProperty dstEntries = plusSo.FindProperty("<effectEntries>k__BackingField");
            dstEntries.arraySize = srcEntries.arraySize;

            for (int i = 0; i < srcEntries.arraySize; i++)
            {
                SerializedProperty src = srcEntries.GetArrayElementAtIndex(i);
                SerializedProperty dst = dstEntries.GetArrayElementAtIndex(i);

                dst.FindPropertyRelative("effect").objectReferenceValue =
                    src.FindPropertyRelative("effect").objectReferenceValue;
                dst.FindPropertyRelative("aimsAt").intValue = src.FindPropertyRelative("aimsAt").intValue;

                CopyArea(src.FindPropertyRelative("area"), dst.FindPropertyRelative("area"));

                dst.FindPropertyRelative("amountDelta").intValue =
                    src.FindPropertyRelative("amountDelta").intValue + 3;
                dst.FindPropertyRelative("amountPercent").intValue =
                    src.FindPropertyRelative("amountPercent").intValue;
            }

            plusSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(plus);

            Debug.Log($"Equipment examples: created {slashPlusPath}.");
        }

        upgradedForm.objectReferenceValue = plus;
        slashSo.ApplyModifiedProperties();
        EditorUtility.SetDirty(slash);

        Debug.Log("Equipment examples: Slash.upgradedForm -> Slash+.");
    }

    private static void CopyRange(SerializedProperty src, SerializedProperty dst)
    {
        dst.FindPropertyRelative("shape").intValue = src.FindPropertyRelative("shape").intValue;
        dst.FindPropertyRelative("minDistance").intValue = src.FindPropertyRelative("minDistance").intValue;
        dst.FindPropertyRelative("maxDistance").intValue = src.FindPropertyRelative("maxDistance").intValue;
    }

    private static void CopyArea(SerializedProperty src, SerializedProperty dst)
    {
        dst.FindPropertyRelative("kind").intValue = src.FindPropertyRelative("kind").intValue;
        CopyRange(src.FindPropertyRelative("radius"), dst.FindPropertyRelative("radius"));
        dst.FindPropertyRelative("pattern").objectReferenceValue = src.FindPropertyRelative("pattern").objectReferenceValue;
    }

    private static void TuneLootTable(string path, float chance)
    {
        LootTable table = AssetDatabase.LoadAssetAtPath<LootTable>(path);

        if (table == null)
        {
            Debug.LogWarning($"Equipment examples: {path} not found - equipmentChance left untouched.");
            return;
        }

        SerializedObject so = new(table);
        SerializedProperty chanceProp = so.FindProperty("equipmentChance");

        if (chanceProp.floatValue > 0f) { return; }

        chanceProp.floatValue = chance;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(table);

        Debug.Log($"Equipment examples: {table.name} equipmentChance -> {chance}.");
    }

    /// Creates the asset and its shared fields, or returns null if it already exists - the signal every
    /// AuthorX method uses to skip the rest of its own work.
    private static EquipmentData BeginEquipment(
        string path, string equipmentName, string description, Rarity rarity, CharacterClass requiredClass)
    {
        if (AssetDatabase.LoadAssetAtPath<EquipmentData>(path) != null) { return null; }

        EquipmentData item = ScriptableObject.CreateInstance<EquipmentData>();
        AssetDatabase.CreateAsset(item, path);

        SerializedObject so = new(item);
        so.FindProperty("<equipmentName>k__BackingField").stringValue = equipmentName;
        so.FindProperty("<description>k__BackingField").stringValue = description;
        so.FindProperty("<rarity>k__BackingField").intValue = (int)rarity;
        so.FindProperty("<requiredClass>k__BackingField").intValue = (int)requiredClass;
        so.ApplyModifiedProperties();

        return item;
    }

    /// Builds one modifier, configures it, and attaches it as a sub-asset of `owner` - `owner` must
    /// already exist on disk (BeginEquipment's CreateAsset) before a sub-asset can attach to it.
    private static T AddModifier<T>(EquipmentData owner, Action<SerializedObject> configure) where T : ScriptableObject
    {
        T instance = ScriptableObject.CreateInstance<T>();
        instance.name = typeof(T).Name;

        SerializedObject so = new(instance);
        configure(so);
        so.ApplyModifiedProperties();

        AssetDatabase.AddObjectToAsset(instance, owner);
        EditorUtility.SetDirty(instance);

        return instance;
    }

    private static void FinishEquipment(EquipmentData item, params EquipmentModifier[] modifiers)
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

        Debug.Log($"Equipment examples: created {AssetDatabase.GetAssetPath(item)}.");
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
