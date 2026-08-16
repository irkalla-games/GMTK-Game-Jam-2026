using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps EquipmentLibrary assets in sync with whatever EquipmentData assets actually exist under
/// Assets/Data/Equipment, without anyone having to remember to update them by hand - the equipment
/// counterpart to CardLibraryEditor, same Rescan-button-plus-AssetPostprocessor shape.
///
/// Editor-only, and deliberately not covered by Tools/compile-check.ps1 - see CardLibraryEditor's
/// identical note on why.
/// </summary>
[CustomEditor(typeof(EquipmentLibrary))]
public class EquipmentLibraryEditor : Editor
{
    private const string EquipmentFolder = "Assets/Data/Equipment";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        if (GUILayout.Button("Rescan"))
        {
            Rescan((EquipmentLibrary)target);
        }
    }

    private static void Rescan(EquipmentLibrary library)
    {
        List<EquipmentData> found = FindAllEquipmentData();

        library.SetItems(found);
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();

        Debug.Log($"{library.name}: rescanned, {found.Count} item(s) found under {EquipmentFolder}");
    }

    private static List<EquipmentData> FindAllEquipmentData()
    {
        List<EquipmentData> found = new();

        foreach (string guid in AssetDatabase.FindAssets("t:EquipmentData", new[] { EquipmentFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EquipmentData item = AssetDatabase.LoadAssetAtPath<EquipmentData>(path);

            if (item != null) { found.Add(item); }
        }

        return found;
    }

    /// <summary>
    /// Re-runs Rescan on every EquipmentLibrary asset whenever an EquipmentData asset is imported - see
    /// CardLibraryEditor.CardDataPostprocessor's identical reasoning.
    /// </summary>
    private class EquipmentDataPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool touchedEquipmentData = false;

            foreach (string path in importedAssets)
            {
                if (AssetDatabase.LoadAssetAtPath<EquipmentData>(path) != null) { touchedEquipmentData = true; break; }
            }

            if (!touchedEquipmentData) { return; }

            foreach (string guid in AssetDatabase.FindAssets("t:EquipmentLibrary"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                EquipmentLibrary library = AssetDatabase.LoadAssetAtPath<EquipmentLibrary>(path);

                if (library != null) { Rescan(library); }
            }
        }
    }
}
