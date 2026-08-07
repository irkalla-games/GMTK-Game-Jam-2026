using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws a [OneBasedCell] Vector2Int with 1 added, and subtracts it again on the way back in. The
/// asset on disk never sees the shifted number.
///
/// Clamped at zero on write-back so typing 0 - or clearing the field - cannot store -1 and produce a
/// cell that GetTile will never find.
///
/// This file must stay under an Editor folder: it references UnityEditor, which does not exist in a
/// player build.
/// </summary>
[CustomPropertyDrawer(typeof(OneBasedCellAttribute))]
public class OneBasedCellDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // The attribute on anything but a Vector2Int degrades to the normal field rather than drawing
        // nothing - a silently blank Inspector row is far harder to diagnose than a row that ignores
        // the attribute.
        if (property.propertyType != SerializedPropertyType.Vector2Int)
        {
            EditorGUI.PropertyField(position, property, label, true);
            return;
        }

        EditorGUI.BeginProperty(position, label, property);
        EditorGUI.BeginChangeCheck();

        Vector2Int shown = EditorGUI.Vector2IntField(position, label, property.vector2IntValue + Vector2Int.one);

        // Only write on an actual edit. Assigning unconditionally would mark every object whose
        // Inspector merely got drawn as dirty, and stomp multi-object editing's mixed values.
        if (EditorGUI.EndChangeCheck())
        {
            property.vector2IntValue = Vector2Int.Max(shown - Vector2Int.one, Vector2Int.zero);
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUI.GetPropertyHeight(property, label, true);
    }
}
