using System;
using UnityEngine;

/// <summary>
/// Marks a serialized field as display-only: it shows up in the Inspector but is greyed out and
/// cannot be typed into. For runtime state you want to watch during Play Mode without giving anyone
/// the chance to hand-edit it into an illegal value.
///
/// Runtime-side on purpose - the attribute has to live in Assembly-CSharp because the fields wearing
/// it do. The drawer that actually greys it out is Editor-only, in Scripts/Editor.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class ReadOnlyFieldAttribute : PropertyAttribute { }