using UnityEditor;
using UnityEngine;

/// <summary>
/// Paints an EffectPattern's two facing grids and its anchor cell.
///
/// Two grids are shown, Cardinal (authored facing Up) and Diagonal (authored facing Up-Right) - see
/// EffectPattern's own doc comment for why one grid can't cover all 8 directions by rotation alone.
/// "Pick Anchor" arms a one-shot mode where the next cell clicked becomes the anchor instead of being
/// painted, since a plain click is already spoken for by painting.
/// </summary>
[CustomEditor(typeof(EffectPattern))]
public class EffectPatternEditor : Editor
{
    private bool pickingAnchor;

    public override void OnInspectorGUI()
    {
        EffectPattern pattern = (EffectPattern)target;

        int width = pattern.Width;
        int height = pattern.Height;

        EditorGUILayout.LabelField("Grid Size");
        EditorGUILayout.BeginHorizontal();
        int newWidth = EditorGUILayout.IntField("Width", width);
        int newHeight = EditorGUILayout.IntField("Height", height);
        EditorGUILayout.EndHorizontal();

        newWidth = Mathf.Max(1, newWidth);
        newHeight = Mathf.Max(1, newHeight);

        if (newWidth != width || newHeight != height)
        {
            if (GUILayout.Button("Resize (keeps overlapping cells)"))
            {
                Undo.RecordObject(pattern, "Resize Effect Pattern");
                pattern.Resize(newWidth, newHeight);
                EditorUtility.SetDirty(pattern);
            }
        }

        GUILayout.Space(10);

        pickingAnchor = GUILayout.Toggle(pickingAnchor, "Pick Anchor (click a cell below)", "Button");

        GUILayout.Space(10);
        DrawGrid(pattern, "Cardinal - authored facing Up", isDiagonal: false);

        GUILayout.Space(10);
        DrawGrid(pattern, "Diagonal - authored facing Up-Right (optional)", isDiagonal: true);
    }

    private void DrawGrid(EffectPattern pattern, string label, bool isDiagonal)
    {
        GUILayout.Label(label, EditorStyles.boldLabel);

        // Painted rows top-to-bottom but stored with +Y up, so row 0 on screen is the highest Y.
        for (int y = pattern.Height - 1; y >= 0; y--)
        {
            GUILayout.BeginHorizontal();

            for (int x = 0; x < pattern.Width; x++)
            {
                bool active = isDiagonal ? pattern.GetDiagonalCell(x, y) : pattern.GetCardinalCell(x, y);
                bool isAnchor = pattern.Anchor.x == x && pattern.Anchor.y == y;

                GUI.backgroundColor = isAnchor ? Color.yellow : active ? Color.green : Color.gray;

                if (GUILayout.Button("", GUILayout.Width(24), GUILayout.Height(24)))
                {
                    Undo.RecordObject(pattern, "Edit Effect Pattern");

                    if (pickingAnchor)
                    {
                        pattern.SetAnchor(new Vector2Int(x, y));
                        pickingAnchor = false;
                    }
                    else if (isDiagonal)
                    {
                        pattern.SetDiagonalCell(x, y, !active);
                    }
                    else
                    {
                        pattern.SetCardinalCell(x, y, !active);
                    }

                    EditorUtility.SetDirty(pattern);
                }
            }

            GUILayout.EndHorizontal();
        }

        GUI.backgroundColor = Color.white;
    }
}
