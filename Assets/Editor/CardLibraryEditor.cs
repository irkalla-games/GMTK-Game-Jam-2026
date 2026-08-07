using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps CardLibrary assets in sync with whatever CardData assets actually exist under
/// Assets/Data/CardData, without anyone having to remember to update them by hand. With many more cards
/// planned, a library that must be refreshed manually is a library that silently stops offering new
/// cards - RescanAll runs automatically on import, and the Inspector button is there for a manual
/// nudge (moving a card out of Assets/Data/CardData, say, which no import event fires for).
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 - that script drives
/// Assembly-CSharp.csproj, which never lists Assets/Editor. Verify this file by focusing the Unity
/// Editor and checking the console, per CLAUDE.md.
/// </summary>
[CustomEditor(typeof(CardLibrary))]
public class CardLibraryEditor : Editor
{
    private const string CardDataFolder = "Assets/Data/CardData";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        if (GUILayout.Button("Rescan"))
        {
            Rescan((CardLibrary)target);
        }
    }

    private static void Rescan(CardLibrary library)
    {
        List<CardData> found = FindAllCardData();

        library.SetCards(found);
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();

        Debug.Log($"{library.name}: rescanned, {found.Count} card(s) found under {CardDataFolder}");
    }

    private static List<CardData> FindAllCardData()
    {
        List<CardData> found = new();

        foreach (string guid in AssetDatabase.FindAssets("t:CardData", new[] { CardDataFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(path);

            if (card != null) { found.Add(card); }
        }

        return found;
    }

    /// <summary>
    /// Re-runs Rescan on every CardLibrary asset whenever a CardData asset is imported - covers the
    /// common case (a new card added, an existing one renamed or re-tagged) without anyone opening this
    /// Inspector at all. Deleting or moving a card out of the watched folder does not raise an import
    /// event, which is what the manual button above is for.
    /// </summary>
    private class CardDataPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool touchedCardData = false;

            foreach (string path in importedAssets)
            {
                if (AssetDatabase.LoadAssetAtPath<CardData>(path) != null) { touchedCardData = true; break; }
            }

            if (!touchedCardData) { return; }

            foreach (string guid in AssetDatabase.FindAssets("t:CardLibrary"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CardLibrary library = AssetDatabase.LoadAssetAtPath<CardLibrary>(path);

                if (library != null) { Rescan(library); }
            }
        }
    }
}
