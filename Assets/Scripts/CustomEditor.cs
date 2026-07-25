using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EffectPattern))]
public class EffectPatternEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EffectPattern pattern = (EffectPattern)target;

        DrawDefaultInspector();

        GUILayout.Space(20);

        GUILayout.Label("Pattern");

        for (int y = 0; y < pattern.Height; y++)
        {
            GUILayout.BeginHorizontal();

            for (int x = 0; x < pattern.Width; x++)
            {
                bool active = pattern.GetCell(x, y);

                GUI.backgroundColor =
                    active ? Color.green : Color.gray;


                if (GUILayout.Button(
                    "",
                    GUILayout.Width(30),
                    GUILayout.Height(30)))
                {
                    pattern.SetCell(
                        x,
                        y,
                        !active);

                    EditorUtility.SetDirty(pattern);
                }
            }

            GUILayout.EndHorizontal();
        }

        GUI.backgroundColor = Color.white;
    }
}