using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws a [ReadOnlyField] field normally, then disables it so it renders greyed out and rejects
/// input. DisabledScope restores the previous GUI state on dispose, so it cannot leak out and grey
/// the rest of the Inspector if PropertyField throws.
///
/// This file must stay under an Editor folder: it references UnityEditor, which does not exist in a
/// player build.
/// </summary>
[CustomPropertyDrawer(typeof(ReadOnlyFieldAttribute))]
public class ReadOnlyFieldDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        using (new EditorGUI.DisabledScope(true))
        {
            // `true` includes children, so this also works on structs and lists, not just ints.
            EditorGUI.PropertyField(position, property, label, true);
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUI.GetPropertyHeight(property, label, true);
    }
}
