using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds the boss identity components the design sheet cannot express - a Totem's aura, a ward cycle, a
/// split, an escape, a permanent status.
///
/// The sheet owns health, brain, targeting and decks, because those are one value per boss and merge
/// cleanly. None of that is true here: these are components with their own serialized shapes, so they
/// are wired once by this command instead. Same division the project already draws between
/// RosterSheetSync and the hand-authored wiring commands.
///
/// Idempotent, and deliberately so - it re-writes every field it owns on every run rather than
/// skipping a prefab that already has the component, so a half-wired boss is fixed by running it again
/// rather than by hand. See CLAUDE.md's "repair, not skip".
/// </summary>
public static class BossIdentityWiring
{
    private const string BossFolder = "Assets/Prefabs/Bosses/";

    [MenuItem("Tools/Bosses/Wire Boss Identities")]
    public static void WireAll()
    {
        int wired = 0;

        wired += WireEvilKing() ? 1 : 0;
        wired += WireEvilKnight() ? 1 : 0;
        wired += WireEvilWizard() ? 1 : 0;
        wired += WireDreadSorcerer() ? 1 : 0;
        wired += WireReinforcedGolem() ? 1 : 0;

        AssetDatabase.SaveAssets();

        Debug.Log($"Boss identity wiring: {wired} of 5 prefabs written.");
    }

    /// Permanent Thorns - attackers take 3. His whole defence, in place of the Shield the sheet pass
    /// stripped off Royal Guard.
    private static bool WireEvilKing()
    {
        return Edit("EvilKing", root =>
        {
            StartingStatuses seeder = Ensure<StartingStatuses>(root);
            SerializedObject so = new(seeder);
            SerializedProperty list = so.FindProperty("statuses");

            list.arraySize = 1;
            SerializedProperty entry = list.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("type").intValue = (int)StatusType.Thorns;
            entry.FindPropertyRelative("stacks").intValue = 3;

            so.ApplyModifiedPropertiesWithoutUndo();
        });
    }

    /// An allies-only aura buffing whatever his Rally line summons - Strength and a small Shield, out
    /// to two tiles.
    ///
    /// Worth knowing while tuning: an aura's statuses are rebuilt per query, so charge-spending types
    /// never deplete while a minion stands in range (see Totem's class comment). Strength here is a
    /// standing bonus rather than a few swings, and the Shield refills rather than being chipped down.
    private static bool WireEvilKnight()
    {
        return Edit("EvilKnight", root =>
        {
            Totem totem = Ensure<Totem>(root);
            SerializedObject so = new(totem);

            so.FindProperty("affects").intValue = (int)AuraAudience.Allies;

            SerializedProperty range = so.FindProperty("range");
            range.FindPropertyRelative("shape").intValue = (int)RangeShape.Chebyshev;
            range.FindPropertyRelative("minDistance").intValue = 0;
            range.FindPropertyRelative("maxDistance").intValue = 2;

            SerializedProperty auras = so.FindProperty("auras");
            auras.arraySize = 2;
            SetAura(auras.GetArrayElementAtIndex(0), StatusType.Strength, 2);
            SetAura(auras.GetArrayElementAtIndex(1), StatusType.Shield, 3);

            so.ApplyModifiedPropertiesWithoutUndo();
        });
    }

    /// Blinks clear at each third of health lost - two escapes across the fight.
    private static bool WireEvilWizard()
    {
        return Edit("EvilWizard", root =>
        {
            EscapeWhenHurt escape = Ensure<EscapeWhenHurt>(root);
            SerializedObject so = new(escape);

            so.FindProperty("escapes").intValue = 2;

            so.ApplyModifiedPropertiesWithoutUndo();
        });
    }

    /// Immune to one status at a time, rotating each turn. The cycle is the three a player most wants
    /// to land on him - a denied turn, a poison clock, and blunted damage - so reading the icon before
    /// committing a card is the whole interaction.
    private static bool WireDreadSorcerer()
    {
        return Edit("EvilWizard2", root =>
        {
            WardCycle ward = Ensure<WardCycle>(root);
            SerializedObject so = new(ward);
            SerializedProperty cycle = so.FindProperty("cycle");

            StatusType[] wards = { StatusType.Frozen, StatusType.Poison, StatusType.Weaken };

            cycle.arraySize = wards.Length;
            for (int i = 0; i < wards.Length; i++)
            {
                cycle.GetArrayElementAtIndex(i).intValue = (int)wards[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        });
    }

    /// Splits into two of itself at half health. halfPrefab points at the Golem's own asset, so both
    /// bodies are genuinely identical - SplitStatus disarms the copy so it cannot cascade.
    private static bool WireReinforcedGolem()
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(BossFolder + "ReinforcedGolem.prefab");

        if (asset == null)
        {
            Debug.LogError("Boss identity wiring: ReinforcedGolem.prefab not found.");
            return false;
        }

        return Edit("ReinforcedGolem", root =>
        {
            SplitWhenBloodied split = Ensure<SplitWhenBloodied>(root);
            SerializedObject so = new(split);

            so.FindProperty("halfPrefab").objectReferenceValue = asset;
            so.FindProperty("armed").boolValue = true;

            so.ApplyModifiedPropertiesWithoutUndo();
        });
    }

    // ----------------------------------------------------------------------------------------------

    private static void SetAura(SerializedProperty entry, StatusType type, int stacks)
    {
        entry.FindPropertyRelative("type").intValue = (int)type;
        entry.FindPropertyRelative("stacks").intValue = stacks;
    }

    /// GetComponent-or-AddComponent, written the two-line way on purpose: GetComponent returns a
    /// fake-null, so `GetComponent<T>() ?? AddComponent<T>()` would never add anything. See CLAUDE.md.
    private static T Ensure<T>(GameObject root) where T : Component
    {
        T existing = root.GetComponent<T>();

        return existing != null ? existing : root.AddComponent<T>();
    }

    /// Opens one boss prefab, runs `write` against its root, and saves. EditPrefabContentsScope rather
    /// than touching the asset directly, for the same reason RosterSheetSync uses it: the prefab is
    /// loaded, modified and written back through Unity's own serializer rather than by hand.
    private static bool Edit(string prefabName, System.Action<GameObject> write)
    {
        string path = BossFolder + prefabName + ".prefab";

        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogError($"Boss identity wiring: {path} not found.");
            return false;
        }

        using PrefabUtility.EditPrefabContentsScope scope = new(path);

        write(scope.prefabContentsRoot);

        return true;
    }
}
