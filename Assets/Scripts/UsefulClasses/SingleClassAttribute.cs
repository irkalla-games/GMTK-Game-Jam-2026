using System;
using UnityEngine;

/// <summary>
/// Marks a CharacterClass field as one class, never a combination. CharacterClass became a [Flags]
/// mask so a CardData can require more than one class at once, but a Character's own class is not a
/// mask - it is exactly one class, and Unity's default drawer for a [Flags] enum is a multi-select
/// dropdown that would let two get ticked at once. That would silently widen which cards a character
/// may hold (CardData.CanBeUsedBy tests with &amp;), so this restricts the Inspector to the single-bit
/// values only (Any, Knight, Mage, Rogue, ...), never a combination like Knight | Mage.
///
/// Runtime-side on purpose - the attribute has to live in Assembly-CSharp because the field wearing it
/// does. The drawer that does the restricting is Editor-only, in Scripts/Editor. Same split as
/// ReadOnlyFieldAttribute and OneBasedCellAttribute.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class SingleClassAttribute : PropertyAttribute { }
