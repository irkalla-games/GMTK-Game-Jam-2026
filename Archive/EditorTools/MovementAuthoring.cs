using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot authoring for the Wall of Force route-blocking change: flips TargetRange.RequiresRoute on
/// the three walking move cards, creates the enemy Teleport innate, and swaps the wizard bosses' basic
/// movement for it - the same "menu command builds what a hand-authored .asset cannot safely
/// represent" shape SummonAuthoring and PushWallAuthoring already use, for the same reason: the Editor
/// holds Temp/UnityLockfile while it is open, and every *Authoring script in this project goes through
/// SerializedObject/PrefabUtility rather than hand-written .asset or .prefab YAML.
///
/// Idempotent and repairing rather than skipping: every field this script owns is re-written on every
/// run, and TeleportInnate is only created if it does not already exist at its path, matching every
/// other *Authoring script's own idempotence note.
///
/// Editor-only, not covered by Tools/compile-check.ps1 - see StarterCharacterAuthoring's header for
/// why. Verify by focusing the Editor and reading the console.
/// </summary>
public static class MovementAuthoring
{
    private const string MovePath = "Assets/Data/CardData/Generic/Move.asset";
    private const string MoveInnatePath = "Assets/Data/CardData/Enemy/MoveInnate.asset";
    private const string MoveInnate2Path = "Assets/Data/CardData/Enemy/MoveInnate2.asset";
    private const string TeleportPath = "Assets/Data/CardData/Mage/Movement/Teleport.asset";
    private const string TeleportInnatePath = "Assets/Data/CardData/Enemy/TeleportInnate.asset";

    // The single MoveEffect asset every move and teleport card already shares - see MoveEffect.cs's
    // own header note that Card.range, not the effect, is what tells them apart.
    private const string MoveEffectPath = "Assets/Data/EffectData/Move.asset";

    private static readonly string[] WizardPrefabPaths =
    {
        "Assets/Prefabs/Bosses/EvilWizard.prefab",
        "Assets/Prefabs/Bosses/EvilWizard2.prefab",
        "Assets/Prefabs/Bosses/EvilWizard3.prefab",
    };

    [MenuItem("Tools/Cards/Author Movement and Teleport")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Movement authoring: exit Play Mode first - asset edits made in play are not reliable.");
            return;
        }

        SetRequiresRoute(MovePath, true);
        SetRequiresRoute(MoveInnatePath, true);
        SetRequiresRoute(MoveInnate2Path, true);

        // Defensive: Teleport's RangeShape.Anywhere already makes Card.Refusal skip the route check
        // outright, but an explicit false keeps the asset honest about what it does.
        SetRequiresRoute(TeleportPath, false);

        CardData teleportInnate = EnsureTeleportInnate();

        if (teleportInnate != null)
        {
            CardData moveInnate = AssetDatabase.LoadAssetAtPath<CardData>(MoveInnatePath);

            foreach (string prefabPath in WizardPrefabPaths)
            {
                SwapDeckCard(prefabPath, moveInnate, teleportInnate);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Movement authoring: done - Move, MoveInnate and MoveInnate2 now require a walking "
                 + "route (a Wall of Force blocks them), Teleport still ignores it, TeleportInnate "
                 + "exists for enemies, and the three Evil Wizard bosses now carry it instead of "
                 + "MoveInnate. Pick Tools > Sync With Sheet to get TeleportInnate into the design "
                 + "workbook, and Tools > Sync Bosses With Sheet for the deck change.");
    }

    private static void SetRequiresRoute(string cardPath, bool value)
    {
        CardData card = AssetDatabase.LoadAssetAtPath<CardData>(cardPath);

        if (card == null)
        {
            Debug.LogError($"Movement authoring: {cardPath} not found - skipped.");
            return;
        }

        SerializedObject so = new(card);
        SerializedProperty range = so.FindProperty("<range>k__BackingField");
        range.FindPropertyRelative("requiresRoute").boolValue = value;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(card);
    }

    /// <summary>
    /// Creates (or repairs) the enemy Teleport card - Chebyshev 1-3, RequiresRoute off, so it passes
    /// straight through a Wall of Force the way the Mage's own Teleport does. Reuses the shared
    /// MoveEffect asset rather than a bespoke TeleportEffect, exactly like every other move/teleport
    /// card in the project.
    /// </summary>
    private static CardData EnsureTeleportInnate()
    {
        CardEffect moveEffect = AssetDatabase.LoadAssetAtPath<CardEffect>(MoveEffectPath);

        if (moveEffect == null)
        {
            Debug.LogError($"Movement authoring: {MoveEffectPath} not found - cannot create {TeleportInnatePath}.");
            return null;
        }

        CardData card = AssetDatabase.LoadAssetAtPath<CardData>(TeleportInnatePath);
        bool created = card == null;

        if (created)
        {
            card = ScriptableObject.CreateInstance<CardData>();
            AssetDatabase.CreateAsset(card, TeleportInnatePath);
        }

        SerializedObject so = new(card);

        so.FindProperty("<cardName>k__BackingField").stringValue = "Teleport";
        so.FindProperty("<cost>k__BackingField").intValue = 1;
        so.FindProperty("<requiredClass>k__BackingField").intValue = (int)CharacterClass.Any;
        so.FindProperty("<rarity>k__BackingField").intValue = (int)Rarity.NotOffered;
        so.FindProperty("<excludeFromRewards>k__BackingField").boolValue = true;
        so.FindProperty("<rotatableAim>k__BackingField").boolValue = false;
        so.FindProperty("<description>k__BackingField").stringValue =
            "Move up to three tiles in any direction, ignoring walls.";

        SerializedProperty range = so.FindProperty("<range>k__BackingField");
        range.FindPropertyRelative("shape").intValue = (int)RangeShape.Chebyshev;
        range.FindPropertyRelative("minDistance").intValue = 1;
        range.FindPropertyRelative("maxDistance").intValue = 3;
        range.FindPropertyRelative("requiresRoute").boolValue = false;

        SerializedProperty keywords = so.FindProperty("<keywords>k__BackingField");
        keywords.arraySize = 1;
        SerializedProperty innate = keywords.GetArrayElementAtIndex(0);
        innate.FindPropertyRelative("type").intValue = (int)CardKeywordType.Innate;
        innate.FindPropertyRelative("magnitude").intValue = 0;

        SerializedProperty entries = so.FindProperty("<effectEntries>k__BackingField");
        entries.arraySize = 1;
        SerializedProperty entry = entries.GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("effect").objectReferenceValue = moveEffect;
        entry.FindPropertyRelative("aimsAt").intValue = (int)EffectTarget.PlayedTile;

        SerializedProperty area = entry.FindPropertyRelative("area");
        area.FindPropertyRelative("kind").intValue = (int)AreaKind.Single;

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(card);

        Debug.Log($"Movement authoring: {(created ? "created" : "repaired")} {TeleportInnatePath}.");

        return card;
    }

    /// <summary>
    /// Replaces every occurrence of `from` in a prefab's deck with `to`, through
    /// PrefabUtility.EditPrefabContentsScope + SerializedObject - the same API
    /// Assets/Editor/RosterSheetSync.cs already uses to write a prefab's deck.
    /// </summary>
    private static void SwapDeckCard(string prefabPath, CardData from, CardData to)
    {
        if (from == null || to == null) { return; }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
        {
            Debug.LogError($"Movement authoring: no prefab at {prefabPath} - skipped.");
            return;
        }

        using PrefabUtility.EditPrefabContentsScope scope = new(prefabPath);
        GameObject root = scope.prefabContentsRoot;
        Character character = root.GetComponent<Character>();

        if (character == null)
        {
            Debug.LogError($"Movement authoring: {prefabPath} has no Character component - skipped.");
            return;
        }

        SerializedObject so = new(character);
        SerializedProperty deck = so.FindProperty("deck");

        int swapped = 0;

        for (int i = 0; i < deck.arraySize; i++)
        {
            SerializedProperty element = deck.GetArrayElementAtIndex(i);

            if (element.objectReferenceValue == from)
            {
                element.objectReferenceValue = to;
                swapped++;
            }
        }

        so.ApplyModifiedProperties();

        Debug.Log(swapped > 0
            ? $"Movement authoring: swapped {swapped} MoveInnate -> TeleportInnate in {prefabPath}."
            : $"Movement authoring: {prefabPath} already carries no MoveInnate - nothing to swap.");
    }
}
