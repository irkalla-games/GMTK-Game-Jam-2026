using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the goblin encounter in the open scene: drops the Priest, spawns warriors and archers from
/// the Goblin prefab, and rebuilds BattleManager's roster.
///
/// An Editor command rather than hand-written scene YAML on purpose. Knight, Priest and Thief are
/// prefab *instances*, and editing those by hand is how references get silently dropped - the scene
/// is also usually open, so anything written to disk loses to the Editor's in-memory copy the moment
/// it saves. Going through Unity's own APIs means the result is correct by construction, shows up in
/// the Inspector immediately, and is one Ctrl+Z away from being undone.
///
/// Idempotent: run it twice and you get the same encounter, not two of them.
/// </summary>
public static class EncounterSetup
{
    private const string GoblinPrefabPath =
        "Assets/Extra Assets/Dark fantasy - popular enemies- Free Sample/Goblin/Goblin_Thief.prefab";

    /// <summary>
    /// Warriors outnumber archers, and both together outnumber the party. Six enemies against two
    /// heroes is roughly 12 enemy actions against 6 player plays - deliberately lopsided, which is
    /// what makes armor and position matter rather than raw damage.
    /// </summary>
    private static readonly (string name, BrainType brain, int health, int damage, int move, int reach,
                             Vector2Int cell)[] Encounter =
    {
        ("Goblin Warrior 1", BrainType.Warrior, 34, 6, 3, 1, new Vector2Int(0, 5)),
        ("Goblin Warrior 2", BrainType.Warrior, 34, 6, 3, 1, new Vector2Int(2, 5)),
        ("Goblin Warrior 3", BrainType.Warrior, 34, 6, 3, 1, new Vector2Int(4, 5)),
        ("Goblin Archer 1",  BrainType.Archer,  24, 5, 2, 4, new Vector2Int(1, 4)),
        ("Goblin Archer 2",  BrainType.Archer,  24, 5, 2, 4, new Vector2Int(3, 4)),
    };

    [MenuItem("Tools/Battle/Set Up Goblin Encounter")]
    public static void SetUp()
    {
        BattleManager battle = Object.FindFirstObjectByType<BattleManager>();

        if (battle == null)
        {
            Debug.LogError("No BattleManager in the open scene - nothing to build an encounter for.");
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GoblinPrefabPath);

        if (prefab == null)
        {
            Debug.LogError($"Goblin prefab not found at {GoblinPrefabPath}");
            return;
        }

        Transform parent = GameObject.Find("---CHARACTERS")?.transform;

        RemovePriest();

        List<Character> roster = new();

        // Surviving player characters keep their place at the front of the roster - FirstPlayableCharacter
        // walks it in order, so whoever is listed first is who you start the battle as.
        foreach (Character existing in Object.FindObjectsByType<Character>(FindObjectsSortMode.None)
                                             .Where(c => c.IsPlayerControlled)
                                             .OrderBy(c => c.name))
        {
            roster.Add(existing);
        }

        foreach ((string name, BrainType brain, int health, int damage, int move, int reach,
                  Vector2Int cell) spec in Encounter)
        {
            Character goblin = Spawn(prefab, parent, spec.name);

            Undo.RecordObject(goblin, "Configure goblin");

            SerializedObject so = new(goblin);
            so.FindProperty("maxHealth").intValue = spec.health;
            so.FindProperty("isPlayerControlled").boolValue = false;
            so.FindProperty("brain").enumValueIndex = (int)spec.brain;
            so.FindProperty("attackDamage").intValue = spec.damage;
            so.FindProperty("moveRange").intValue = spec.move;
            so.FindProperty("attackRange").intValue = spec.reach;
            so.FindProperty("actionPoints").intValue = 2;
            so.FindProperty("startCoordinates").vector2IntValue = spec.cell;
            so.FindProperty("deck").ClearArray();      // enemies do not play cards
            so.ApplyModifiedPropertiesWithoutUndo();

            roster.Add(goblin);
        }

        SerializedObject battleSo = new(battle);
        SerializedProperty characters = battleSo.FindProperty("characters");
        characters.ClearArray();

        for (int i = 0; i < roster.Count; i++)
        {
            characters.InsertArrayElementAtIndex(i);
            characters.GetArrayElementAtIndex(i).objectReferenceValue = roster[i];
        }

        battleSo.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(battle.gameObject.scene);

        Debug.Log($"Encounter ready: {roster.Count(c => c.IsPlayerControlled)} heroes, "
                  + $"{Encounter.Length} goblins. Save the scene to keep it.");
    }

    /// <summary>
    /// Spawns one goblin, reusing an existing GameObject of the same name so running this twice does
    /// not stack duplicates on top of each other.
    /// </summary>
    private static Character Spawn(GameObject prefab, Transform parent, string name)
    {
        GameObject existing = GameObject.Find(name);

        if (existing != null)
        {
            Character had = existing.GetComponent<Character>();
            return had != null ? had : Undo.AddComponent<Character>(existing);
        }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;

        Undo.RegisterCreatedObjectUndo(go, "Spawn goblin");

        Character character = go.GetComponent<Character>();

        // The art prefab knows nothing about this game - it is a sprite and an animator, so the
        // Character component is ours to add.
        return character != null ? character : Undo.AddComponent<Character>(go);
    }

    private static void RemovePriest()
    {
        GameObject priest = GameObject.Find("Priest");

        if (priest == null) { return; }

        Debug.Log("Removing Priest - the party is Knight and Thief.");
        Undo.DestroyObjectImmediate(priest);
    }
}
