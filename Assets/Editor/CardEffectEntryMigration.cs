using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot migration from CardData's old `effects` list (a bare List&lt;CardEffect&gt;) to the new
/// `effectEntries` list (CardEffectEntry: effect + aimsAt + area) - see CardData.cs and CardEffect.cs.
///
/// aimsAt used to live on the shared CardEffect asset. It has already been removed from that class, so
/// it can no longer be read through SerializedObject - the field simply isn't part of the type anymore.
/// What survives is the raw value still sitting in each effect .asset's YAML on disk (nothing has
/// re-saved those files yet), so this reads it back with a plain text search instead. That is only
/// safe as a one-shot recovery for data written before the field was removed - nothing else in the
/// project should ever parse a .asset by hand.
///
/// Idempotent: skips any CardData whose effectEntries is already populated, so re-running after
/// authoring a new AoE card by hand will not duplicate or overwrite it.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 - that script drives
/// Assembly-CSharp.csproj, which never lists Assets/Editor. Verify by focusing the Editor and checking
/// the console, per CLAUDE.md.
/// </summary>
public static class CardEffectEntryMigration
{
    [MenuItem("Tools/Cards/Migrate Effects To Entries")]
    public static void Migrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Card effect migration: exit Play Mode first.");
            return;
        }

        int migrated = 0;
        int skipped = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:CardData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(path);

            if (card == null) { continue; }

            SerializedObject so = new(card);
            SerializedProperty legacyEffects = so.FindProperty("<effects>k__BackingField");
            SerializedProperty entries = so.FindProperty("<effectEntries>k__BackingField");

            if (legacyEffects == null || entries == null)
            {
                Debug.LogWarning($"Card effect migration: {card.name} - expected fields not found, skipped.");
                continue;
            }

            if (entries.arraySize > 0)
            {
                skipped++;
                continue;
            }

            entries.arraySize = legacyEffects.arraySize;

            for (int i = 0; i < legacyEffects.arraySize; i++)
            {
                CardEffect effect = legacyEffects.GetArrayElementAtIndex(i).objectReferenceValue as CardEffect;

                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("effect").objectReferenceValue = effect;
                entry.FindPropertyRelative("aimsAt").intValue = (int)ReadLegacyAimsAt(effect);
                // area is left at its freshly-grown default - AreaKind.Single, exactly today's
                // single-tile behaviour, the same zero-default reasoning as RangeShape.Anywhere.
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(card);

            Debug.Log($"Card effect migration: {card.name} - {legacyEffects.arraySize} effect(s) migrated.");
            migrated++;
        }

        AssetDatabase.SaveAssets();

        Debug.Log($"Card effect migration: done - {migrated} card(s) migrated, {skipped} already had entries.");
    }

    private static EffectTarget ReadLegacyAimsAt(CardEffect effect)
    {
        if (effect == null) { return EffectTarget.PlayedTile; }

        string path = AssetDatabase.GetAssetPath(effect);

        if (string.IsNullOrEmpty(path)) { return EffectTarget.PlayedTile; }

        foreach (string line in File.ReadAllLines(path))
        {
            string trimmed = line.Trim();

            if (!trimmed.StartsWith("aimsAt:")) { continue; }

            if (int.TryParse(trimmed.Substring("aimsAt:".Length).Trim(), out int value))
            {
                return (EffectTarget)value;
            }
        }

        // No leftover aimsAt line - either the asset was already re-saved after the field was removed,
        // or it never had one. PlayedTile is the same zero-default the field itself used to have.
        return EffectTarget.PlayedTile;
    }
}
