using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot authoring for the Push effect and the two rotatable walls - the same "menu command builds
/// what a hand-authored .asset cannot safely represent" shape SummonAuthoring already uses for the wall
/// tile effects and their shared pattern. Push.asset is a plain CardEffect asset with no fields, so it
/// could in principle be created straight from the Inspector's Create menu, but Wall of Force needs a
/// second effectEntries element pointing at it inserted ahead of the existing one, and an
/// effectEntries edit only reads back correctly through SerializedObject/SerializedProperty - the same
/// reason every *Authoring script in this project goes through the Editor API rather than hand-written
/// .asset YAML. GUIDs and fileID cross-references are Unity's business.
///
/// Idempotent: Push.asset is only created if nothing already exists at its path, rotatableAim is set
/// unconditionally (it has one right answer for these two cards), and the Push entry is only inserted
/// into Wall of Force if no entry already references a PushEffect - safe to re-run.
///
/// Run this AFTER Tools/Cards/Author Summons and Walls (Wall of Force and Wall of Flames have to exist
/// first) and BEFORE Tools/Sync With Sheet, matching SummonAuthoring's own ordering note.
///
/// Editor-only, not covered by Tools/compile-check.ps1 - see StarterCharacterAuthoring's header for why.
/// </summary>
public static class PushWallAuthoring
{
    private const string PushEffectPath = "Assets/Data/EffectData/Push.asset";
    private const string WallOfForcePath = "Assets/Data/CardData/Mage/Summon/WallOfForce.asset";
    private const string WallOfFlamesPath = "Assets/Data/CardData/Mage/Summon/WallOfFlames.asset";

    [MenuItem("Tools/Cards/Author Push and Rotatable Walls")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Push authoring: exit Play Mode first - asset edits made in play are not reliable.");
            return;
        }

        PushEffect push = EnsurePushEffect();

        SetRotatable(WallOfForcePath);
        SetRotatable(WallOfFlamesPath);

        if (push != null) { EnsurePushEntry(WallOfForcePath, push); }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Push authoring: done - Push.asset is ready, Wall of Force and Wall of Flames are "
                 + "rotatable, and Wall of Force now pushes before it slams down. Pick Tools > Sync With "
                 + "Sheet to get the new Rotatable column and Push entry into the design workbook.");
    }

    private static PushEffect EnsurePushEffect()
    {
        PushEffect existing = AssetDatabase.LoadAssetAtPath<PushEffect>(PushEffectPath);
        if (existing != null) { return existing; }

        PushEffect effect = ScriptableObject.CreateInstance<PushEffect>();
        AssetDatabase.CreateAsset(effect, PushEffectPath);
        EditorUtility.SetDirty(effect);

        Debug.Log($"Push authoring: created {PushEffectPath}.");

        return effect;
    }

    private static void SetRotatable(string cardPath)
    {
        CardData card = AssetDatabase.LoadAssetAtPath<CardData>(cardPath);

        if (card == null)
        {
            Debug.LogError($"Push authoring: {cardPath} not found - run Tools > Cards > Author Summons "
                           + "and Walls first.");
            return;
        }

        SerializedObject so = new(card);
        so.FindProperty("<rotatableAim>k__BackingField").boolValue = true;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(card);
    }

    /// <summary>
    /// Inserts a Push entry ahead of Wall of Force's existing tile-effect entry, aimed and shaped
    /// identically (PlayedTile, the same Wall3 pattern) so the shove and the wall cover exactly the
    /// same footprint. Leads the list so the push reads before the wall slams down - see the plan doc's
    /// note that this is a sequencing choice, not a correctness one; the ring PlanPush shoves onto sits
    /// outside the footprint either way, so MoveRefusal never sees the new wall as a destination.
    /// </summary>
    private static void EnsurePushEntry(string cardPath, PushEffect push)
    {
        CardData card = AssetDatabase.LoadAssetAtPath<CardData>(cardPath);
        if (card == null) { return; }

        SerializedObject so = new(card);
        SerializedProperty entries = so.FindProperty("<effectEntries>k__BackingField");

        if (entries.arraySize == 0)
        {
            Debug.LogWarning($"Push authoring: {cardPath} has no effect entries to copy the area/aim "
                             + "shape from - run Tools > Cards > Author Summons and Walls first.");
            return;
        }

        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("effect").objectReferenceValue == push) { return; }
        }

        // Read every value off the wall's own entry into locals *before* touching the array - once
        // InsertArrayElementAtIndex(0) runs, a SerializedProperty captured via GetArrayElementAtIndex(0)
        // tracks that array slot, not the element it used to point at, so re-reading through the same
        // reference afterward would not reliably mean "the wall entry" any more.
        SerializedProperty wallEntry = entries.GetArrayElementAtIndex(0);
        SerializedProperty sourceArea = wallEntry.FindPropertyRelative("area");

        int aimsAt = wallEntry.FindPropertyRelative("aimsAt").intValue;
        int areaKind = sourceArea.FindPropertyRelative("kind").intValue;
        int radiusShape = sourceArea.FindPropertyRelative("radius").FindPropertyRelative("shape").intValue;
        int radiusMin = sourceArea.FindPropertyRelative("radius").FindPropertyRelative("minDistance").intValue;
        int radiusMax = sourceArea.FindPropertyRelative("radius").FindPropertyRelative("maxDistance").intValue;
        UnityEngine.Object pattern = sourceArea.FindPropertyRelative("pattern").objectReferenceValue;

        entries.InsertArrayElementAtIndex(0);
        SerializedProperty pushEntry = entries.GetArrayElementAtIndex(0);

        // Same aim and area (PlayedTile, Pattern -> Wall3) as the wall it precedes, so the push covers
        // exactly the ground the wall is about to occupy, but pointed at Push instead of the wall.
        pushEntry.FindPropertyRelative("effect").objectReferenceValue = push;
        pushEntry.FindPropertyRelative("aimsAt").intValue = aimsAt;

        SerializedProperty destArea = pushEntry.FindPropertyRelative("area");
        destArea.FindPropertyRelative("kind").intValue = areaKind;
        destArea.FindPropertyRelative("radius").FindPropertyRelative("shape").intValue = radiusShape;
        destArea.FindPropertyRelative("radius").FindPropertyRelative("minDistance").intValue = radiusMin;
        destArea.FindPropertyRelative("radius").FindPropertyRelative("maxDistance").intValue = radiusMax;
        destArea.FindPropertyRelative("pattern").objectReferenceValue = pattern;

        pushEntry.FindPropertyRelative("amountDelta").intValue = 0;
        pushEntry.FindPropertyRelative("amountPercent").intValue = 0;

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(card);

        Debug.Log($"Push authoring: added a Push entry to {cardPath}.");
    }
}
