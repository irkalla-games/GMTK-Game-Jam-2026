using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot authoring for the character-select screen's starting content: three DeckData assets, three
/// CharacterOption assets wrapping Knight/Mage/Rogue, the CharacterRoster the select screen reads
/// (DefaultRoster.asset), and a placeholder PlayerRogue prefab duplicated from PlayerKnight. Also
/// retags the two existing Knight cards a Rogue deck needs onto the Knight | Rogue mask - see
/// CharacterClass's [Flags] doc comment for why that mask is 5, never a new standalone value.
///
/// A menu command rather than hand-authored .asset YAML, for the same reason every other *Wiring
/// script in this project is one - see CharacterSelectWiring's header. ScriptableObject
/// cross-references (a CharacterOption pointing at a DeckData, a DeckData pointing at CardData) are
/// GUID + fileID pairs Unity's serializer owns; AssetDatabase.CreateAsset/CopyAsset and SerializedObject
/// let Unity generate and wire all of that itself instead of a hand-typed reference silently pointing
/// at nothing.
///
/// Idempotent: every asset is created only if nothing already exists at its path, and PlayerRogue.prefab
/// is only ever duplicated once - re-running after hand-tuning a deck's card list will not stomp it.
///
/// Editor-only, not covered by Tools/compile-check.ps1 - see CharacterSelectWiring's header for why.
/// Run this before Tools/Main Menu/Wire Character Select: that script reads DefaultRoster.asset by path
/// and only warns, never fails, if it is missing.
/// </summary>
public static class StarterCharacterAuthoring
{
    private const string CharactersFolder = "Assets/Data/Characters";
    private const string DecksFolder = "Assets/Data/DeckData";

    private const string KnightPrefabPath = "Assets/Prefabs/PlayerKnight.prefab";
    private const string MagePrefabPath = "Assets/Prefabs/PlayerMage.prefab";
    private const string RoguePrefabPath = "Assets/Prefabs/PlayerRogue.prefab";

    private const string MovePath = "Assets/Data/CardData/Generic/Move.asset";
    private const string SlashPath = "Assets/Data/CardData/Knight/Melee Attack/Slash.asset";
    private const string ShieldPath = "Assets/Data/CardData/Knight/Defensive Buff/Shield.asset";
    private const string QuickAttackPath = "Assets/Data/CardData/Knight/Melee Attack/Quick Attack.asset";
    private const string BlockPath = "Assets/Data/CardData/Knight/Defensive Buff/Block.asset";
    private const string TeleportPath = "Assets/Data/CardData/Mage/Movement/Teleport.asset";
    private const string CureWoundsPath = "Assets/Data/CardData/Mage/Heal/Cure Wounds.asset";
    private const string HealWordPath = "Assets/Data/CardData/Mage/Heal/Heal Word.asset";
    private const string FireballPath = "Assets/Data/CardData/Mage/RangedAttack/Fireball.asset";
    private const string SummonTotemPath = "Assets/Data/CardData/Mage/Summon/SummonTotem.asset";

    /// Cards a Rogue's deck (both RogueStarter.asset and PlayerRogue.prefab's own inline deck) is
    /// built from. Quick Attack and Block need retagging first - see RetagForRogue - or BuildDeck would
    /// filter both straight back out the moment a Rogue is spawned.
    private static readonly (string path, int count)[] RogueDeck =
    {
        (MovePath, 4), (QuickAttackPath, 3), (BlockPath, 3),
    };

    [MenuItem("Tools/Main Menu/Author Starter Character Data")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Starter character authoring: exit Play Mode first - asset edits made in play are not reliable.");
            return;
        }

        EnsureFolder(CharactersFolder);
        EnsureFolder(DecksFolder);

        RetagForRogue(QuickAttackPath);
        RetagForRogue(BlockPath);

        EnsureRoguePrefab();

        DeckData knightStarter = EnsureDeck("KnightStarter", "Knight Starter", CharacterClass.Knight,
            new[] { (MovePath, 4), (SlashPath, 2), (ShieldPath, 4) });

        DeckData mageStarter = EnsureDeck("MageStarter", "Mage Starter", CharacterClass.Mage,
            new[]
            {
                (TeleportPath, 3), (CureWoundsPath, 2), (HealWordPath, 3), (FireballPath, 3), (SummonTotemPath, 1),
            });

        DeckData rogueStarter = EnsureDeck("RogueStarter", "Rogue Starter", CharacterClass.Rogue, RogueDeck);

        CharacterOption knight = EnsureCharacterOption("Knight", KnightPrefabPath, knightStarter);
        CharacterOption mage = EnsureCharacterOption("Mage", MagePrefabPath, mageStarter);
        CharacterOption rogue = EnsureCharacterOption("Rogue", RoguePrefabPath, rogueStarter);

        EnsureRoster(knight, mage, rogue);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Starter character authoring: done - Knight/Mage/Rogue options, starter decks, "
                 + "DefaultRoster.asset and PlayerRogue.prefab are ready. Run Tools/Main Menu/Wire "
                 + "Character Select next.");
    }

    /// <summary>
    /// Widens `cardPath`'s requiredClass to include Rogue, leaving whatever it already allowed intact -
    /// Quick Attack goes from Knight-only to Knight | Rogue, never to Rogue-only. A no-op if the card
    /// already includes Rogue (idempotent) or is Any (nothing to widen).
    /// </summary>
    private static void RetagForRogue(string cardPath)
    {
        CardData card = AssetDatabase.LoadAssetAtPath<CardData>(cardPath);

        if (card == null)
        {
            Debug.LogWarning($"Starter character authoring: {cardPath} not found - not retagged for Rogue.");
            return;
        }

        SerializedObject so = new(card);

        // requiredClass is `[field: SerializeField]`, so Unity serializes it under its auto-property
        // backing field name, not "requiredClass" - see CardData.cs.
        SerializedProperty requiredClassProp = so.FindProperty("<requiredClass>k__BackingField");

        CharacterClass current = (CharacterClass)requiredClassProp.intValue;

        if (current == CharacterClass.Any || (current & CharacterClass.Rogue) != 0) { return; }

        requiredClassProp.intValue = (int)(current | CharacterClass.Rogue);
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(card);

        Debug.Log($"Starter character authoring: {card.name} requiredClass -> "
                 + $"{(CharacterClass)requiredClassProp.intValue}.");
    }

    /// <summary>
    /// Duplicates PlayerKnight.prefab - CopyAsset gives the new file its own fresh GUID via the .meta
    /// Unity generates for it, so PlayerKnight and PlayerRogue are never confused for the same prefab by
    /// anything that references one by GUID. Art (sprites, animator controller) is left exactly as
    /// copied - a placeholder until the Rogue gets its own rig, per the plan this shipped under.
    /// </summary>
    private static void EnsureRoguePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(RoguePrefabPath) != null) { return; }

        if (!AssetDatabase.CopyAsset(KnightPrefabPath, RoguePrefabPath))
        {
            Debug.LogError($"Starter character authoring: failed to duplicate {KnightPrefabPath} -> {RoguePrefabPath}.");
            return;
        }

        using (PrefabUtility.EditPrefabContentsScope editScope = new(RoguePrefabPath))
        {
            GameObject root = editScope.prefabContentsRoot;
            root.name = "PlayerRogue";

            Character character = root.GetComponent<Character>();

            if (character == null)
            {
                Debug.LogError($"Starter character authoring: {RoguePrefabPath} has no Character "
                               + $"component after duplicating {KnightPrefabPath} - something else is wrong.");
            }
            else
            {
                SerializedObject so = new(character);

                // Plain [SerializeField] fields, not [field: SerializeField] properties like CardData's
                // - so these are their literal field names, no backing-field mangling needed.
                so.FindProperty("characterClass").intValue = (int)CharacterClass.Rogue;
                so.FindProperty("displayName").stringValue = "Rogue";
                so.FindProperty("maxHealth").intValue = 20;

                List<CardData> cards = BuildCardList(RogueDeck);
                SerializedProperty deck = so.FindProperty("deck");
                deck.arraySize = cards.Count;

                for (int i = 0; i < cards.Count; i++)
                {
                    deck.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
                }

                so.ApplyModifiedProperties();
            }
        }

        Debug.Log($"Starter character authoring: created {RoguePrefabPath}.");
    }

    private static DeckData EnsureDeck(
        string assetName, string displayName, CharacterClass forClass, (string path, int count)[] entries)
    {
        string path = $"{DecksFolder}/{assetName}.asset";
        DeckData existing = AssetDatabase.LoadAssetAtPath<DeckData>(path);

        if (existing != null) { return existing; }

        DeckData deck = ScriptableObject.CreateInstance<DeckData>();

        SerializedObject so = new(deck);
        so.FindProperty("displayName").stringValue = displayName;
        so.FindProperty("forClass").intValue = (int)forClass;

        List<CardData> cards = BuildCardList(entries);
        SerializedProperty cardsProp = so.FindProperty("cards");
        cardsProp.arraySize = cards.Count;

        for (int i = 0; i < cards.Count; i++)
        {
            cardsProp.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
        }

        so.ApplyModifiedProperties();

        AssetDatabase.CreateAsset(deck, path);

        Debug.Log($"Starter character authoring: created {path} ({cards.Count} cards).");

        return deck;
    }

    private static CharacterOption EnsureCharacterOption(string characterName, string prefabPath, DeckData starterDeck)
    {
        string path = $"{CharactersFolder}/{characterName}.asset";
        CharacterOption existing = AssetDatabase.LoadAssetAtPath<CharacterOption>(path);

        if (existing != null) { return existing; }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        if (prefab == null)
        {
            Debug.LogWarning($"Starter character authoring: {prefabPath} not found - {characterName} "
                             + "CharacterOption not created.");
            return null;
        }

        CharacterOption option = ScriptableObject.CreateInstance<CharacterOption>();

        SerializedObject so = new(option);
        so.FindProperty("displayName").stringValue = characterName;
        so.FindProperty("prefab").objectReferenceValue = prefab;

        if (starterDeck != null)
        {
            SerializedProperty decks = so.FindProperty("decks");
            decks.arraySize = 1;
            decks.GetArrayElementAtIndex(0).objectReferenceValue = starterDeck;
        }

        so.ApplyModifiedProperties();

        AssetDatabase.CreateAsset(option, path);

        Debug.Log($"Starter character authoring: created {path}.");

        return option;
    }

    private static void EnsureRoster(params CharacterOption[] characters)
    {
        string path = $"{CharactersFolder}/DefaultRoster.asset";

        if (AssetDatabase.LoadAssetAtPath<CharacterRoster>(path) != null) { return; }

        List<CharacterOption> valid = new();

        foreach (CharacterOption option in characters)
        {
            if (option != null) { valid.Add(option); }
        }

        CharacterRoster roster = ScriptableObject.CreateInstance<CharacterRoster>();

        SerializedObject so = new(roster);
        SerializedProperty list = so.FindProperty("characters");
        list.arraySize = valid.Count;

        for (int i = 0; i < valid.Count; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = valid[i];
        }

        // minPartySize/maxPartySize keep the field initializers' 2/4 default - nothing to set.
        so.ApplyModifiedProperties();

        AssetDatabase.CreateAsset(roster, path);

        Debug.Log($"Starter character authoring: created {path} with {valid.Count} character(s).");
    }

    private static List<CardData> BuildCardList((string path, int count)[] entries)
    {
        List<CardData> cards = new();

        foreach ((string path, int count) in entries)
        {
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(path);

            if (card == null)
            {
                Debug.LogWarning($"Starter character authoring: {path} not found - skipped.");
                continue;
            }

            for (int i = 0; i < count; i++) { cards.Add(card); }
        }

        return cards;
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
