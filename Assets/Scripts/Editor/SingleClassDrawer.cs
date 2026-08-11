using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws a [SingleClass] enum field as an ordinary single-select popup, never Unity's default mask
/// field. Unity switches a [Flags]-marked enum's default PropertyField to a multi-select mask
/// dropdown automatically, which would let a Character be authored as Knight | Mage - a combination
/// CardData.CanBeUsedBy has no concept of a character actually being. EditorGUI.EnumPopup always
/// renders single-select regardless of [Flags], listing every named value (Any, Knight, Mage,
/// Rogue, ...) as a mutually exclusive choice, which is what a character's own class actually is.
///
/// Generic over the enum type via fieldInfo rather than hardcoded to CharacterClass, the same reason
/// OneBasedCellDrawer checks propertyType instead of assuming Vector2Int - a second [Flags] enum
/// wanting the same treatment needs no second drawer.
///
/// This file must stay under an Editor folder: it references UnityEditor, which does not exist in a
/// player build.
/// </summary>
[CustomPropertyDrawer(typeof(SingleClassAttribute))]
public class SingleClassDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // Degrades to the normal field on anything but an enum, same reasoning as
        // OneBasedCellDrawer - a silently blank Inspector row is far harder to diagnose than a row
        // that ignores the attribute.
        if (property.propertyType != SerializedPropertyType.Enum)
        {
            EditorGUI.PropertyField(position, property, label, true);
            return;
        }

        EditorGUI.BeginProperty(position, label, property);
        EditorGUI.BeginChangeCheck();

        Type enumType = fieldInfo.FieldType;
        Enum current = Enum.ToObject(enumType, property.intValue) as Enum;
        Enum chosen = EditorGUI.EnumPopup(position, label, current);

        // Only write on an actual edit - see OneBasedCellDrawer for why (dirtying every object an
        // Inspector merely draws, and stomping multi-object editing's mixed values).
        if (EditorGUI.EndChangeCheck())
        {
            property.intValue = Convert.ToInt32(chosen);
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUI.GetPropertyHeight(property, label, true);
    }
}
