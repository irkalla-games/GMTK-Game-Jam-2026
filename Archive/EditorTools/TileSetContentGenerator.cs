using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Authors the starting TileSetData and hands it to every level that has none.
///
/// Same idempotent, repair-not-skip contract as TutorialContentGenerator: an asset that already
/// exists is reused rather than recreated, and every field this tool owns is re-written on every run,
/// so a half-built set is fixed by running the command again.
///
/// **No AssetDatabase.StartAssetEditing.** Batching defers the import of anything created inside the
/// block, so LoadAssetAtPath returns null for an asset this same run just made - which writes
/// {fileID: 0} into every cross-reference and produces a complete-looking set of assets pointing at
/// nothing. In this feature that surfaces as a board with no floor at all. The set is created first
/// and passed *down as a live object* rather than re-loaded by path.
///
/// The one thing it deliberately does not overwrite is a level's existing `tileSets` list: that is
/// authored content, and a designer who added a second mood to Level 1 should not lose it to a re-run.
/// The default set is appended when missing and left alone when already there.
/// </summary>
static class TileSetContentGenerator
{
    const string BlocksFolder = "Assets/Extra Assets/Isometric_Tiles_Pixel_Art/Blocks";
    const string OutputFolder = "Assets/Data/TileSetData";
    const string DefaultSetPath = OutputFolder + "/StoneDungeon.asset";

    /// The two blocks this board was designed around. Both are 64x64 with a 64x32 top face and a 33px
    /// extrusion - as is every other block in the pack, so swapping either for a neighbour needs no
    /// geometry change at all.
    const string GroundSprite = BlocksFolder + "/blocks_63.png";
    const string WallSprite = BlocksFolder + "/blocks_19.png";

    [MenuItem("Tools/Board/4 - Author Default Tile Set")]
    static void AuthorDefaultTileSet()
    {
        Sprite ground = AssetDatabase.LoadAssetAtPath<Sprite>(GroundSprite);
        Sprite wall = AssetDatabase.LoadAssetAtPath<Sprite>(WallSprite);

        if (ground == null || wall == null)
        {
            Debug.LogError("TileSetContentGenerator: the block PNGs have no Sprite sub-asset yet. Run "
                           + "Tools/Board/1 - Import Block Sprites first - they ship as plain textures, "
                           + "which nothing can reference from a SpriteRenderer.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(OutputFolder))
        {
            AssetDatabase.CreateFolder("Assets/Data", "TileSetData");
        }

        TileSetData set = AssetDatabase.LoadAssetAtPath<TileSetData>(DefaultSetPath);

        if (set == null)
        {
            set = ScriptableObject.CreateInstance<TileSetData>();
            AssetDatabase.CreateAsset(set, DefaultSetPath);
        }

        // Driven through SerializedProperty by name so this works on TileSetData's private
        // [SerializeField]s without the class growing public setters purely for a tool.
        SerializedObject so = new(set);

        SetSpriteList(so.FindProperty("groundSprites"), new List<Sprite> { ground });
        SetSpriteList(so.FindProperty("wallSprites"), new List<Sprite> { wall });
        so.FindProperty("cubeHeightPixels").intValue = 33;
        so.FindProperty("wallHeightInBlocks").intValue = 2;

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(set);

        int adopted = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:LevelData"))
        {
            // `set` is passed down as the live object rather than re-loaded from DefaultSetPath, so
            // the reference never depends on the import having caught up.
            if (AdoptTileSet(AssetDatabase.GUIDToAssetPath(guid), set)) { adopted++; }
        }

        AssetDatabase.SaveAssets();

        Debug.Log($"TileSetContentGenerator: {DefaultSetPath} authored (floor blocks_63, wall "
                  + $"blocks_19, 2 blocks tall) and added to {adopted} level(s) that had none.");
    }

    private static void SetSpriteList(SerializedProperty list, List<Sprite> sprites)
    {
        list.ClearArray();

        for (int i = 0; i < sprites.Count; i++)
        {
            list.InsertArrayElementAtIndex(i);
            list.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        }
    }

    /// Appends `set` to one level's tileSets unless it is already in there. Returns whether it added
    /// anything, so the summary can say how many levels were actually short of art.
    private static bool AdoptTileSet(string levelPath, TileSetData set)
    {
        LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(levelPath);

        if (level == null) { return false; }

        SerializedObject so = new(level);
        SerializedProperty list = so.FindProperty("tileSets");

        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == set) { return false; }
        }

        int index = list.arraySize;
        list.InsertArrayElementAtIndex(index);
        list.GetArrayElementAtIndex(index).objectReferenceValue = set;

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(level);

        return true;
    }
}
