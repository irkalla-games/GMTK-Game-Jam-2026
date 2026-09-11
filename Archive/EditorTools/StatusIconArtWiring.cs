using UnityEditor;
using UnityEngine;

/// <summary>
/// Fills in every StatusType StatusIcons.asset has no art for yet - Dodge through TurnTick, the ten
/// added since the original nine (Strength through DoubleShield) were authored. Best-guess art from the
/// same Dark UI icon pack the first nine already draw from, picked to fit each status's own mechanic
/// (see the per-entry comments below) rather than to be final - the user can swap any of these in the
/// Inspector later with no code change, same as every other StatusIcons entry.
///
/// Idempotent and repair-not-skip: an existing entry for a type is overwritten with the mapping below
/// rather than left alone, so re-running this after changing a mapping actually applies the change.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 by default - verify with the
/// -IncludeEditor switch, or by focusing the Editor and checking the console, per CLAUDE.md.
/// </summary>
public static class StatusIconArtWiring
{
    private const string StatusIconsAssetPath = "Assets/Scripts/Statuses/StatusIconData/StatusIcons.asset";
    private const string IconFolder = "Assets/Extra Assets/Dark UI/New Icons/";

    private static readonly (StatusType type, string spritePath)[] Mappings =
    {
        (StatusType.Dodge, IconFolder + "White Boots 1.png"),          // footwork / evasion
        (StatusType.Weaken, IconFolder + "White Down.png"),            // stat decrease
        (StatusType.Taunt, IconFolder + "White Alert.png"),            // provokes / draws attention
        (StatusType.Summoned, IconFolder + "White Alarm.png"),         // counts down each of its turns
        (StatusType.PoisonBlade, IconFolder + "White Blade 2.png"),    // on-hit blade rider
        (StatusType.Stealth, IconFolder + "White Wind.png"),           // slips away unseen
        (StatusType.Vulnerable, IconFolder + "White Star Hollow.png"), // exposed / weakened guard
        (StatusType.GainMultiplier, IconFolder + "White Up.png"),      // scales another status up
        (StatusType.Potency, IconFolder + "White Energy.png"),         // flat magnitude bonus
        (StatusType.TurnTick, IconFolder + "White Cycle.png"),         // repeats every round
    };

    [MenuItem("Tools/Battle HUD/Fill Missing Status Icons")]
    public static void Fill()
    {
        StatusIcons icons = AssetDatabase.LoadAssetAtPath<StatusIcons>(StatusIconsAssetPath);

        if (icons == null)
        {
            Debug.LogError($"Status icon art wiring: {StatusIconsAssetPath} not found.");
            return;
        }

        SerializedObject so = new(icons);
        SerializedProperty entries = so.FindProperty("entries");
        int added = 0;
        int updated = 0;

        foreach ((StatusType type, string spritePath) in Mappings)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);

            if (sprite == null)
            {
                Debug.LogError($"Status icon art wiring: sprite not found at {spritePath} - skipping {type}.");
                continue;
            }

            int existingIndex = FindEntry(entries, type);

            if (existingIndex >= 0)
            {
                entries.GetArrayElementAtIndex(existingIndex).FindPropertyRelative("icon").objectReferenceValue = sprite;
                updated++;
            }
            else
            {
                int index = entries.arraySize;
                entries.InsertArrayElementAtIndex(index);
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("type").intValue = (int)type;
                entry.FindPropertyRelative("icon").objectReferenceValue = sprite;
                added++;
            }
        }

        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();

        Debug.Log($"Status icon art wiring: done - {added} entr{(added == 1 ? "y" : "ies")} added, {updated} updated.");
    }

    private static int FindEntry(SerializedProperty entries, StatusType type)
    {
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);

            if (entry.FindPropertyRelative("type").intValue == (int)type) { return i; }
        }

        return -1;
    }
}
