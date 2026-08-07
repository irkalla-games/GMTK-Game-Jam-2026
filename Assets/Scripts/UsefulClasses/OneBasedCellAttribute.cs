using System;
using UnityEngine;

/// <summary>
/// Shows a Vector2Int grid cell one-based in the Inspector: the bottom-left tile of a 5x6 board reads
/// (1,1) and the top-right reads (5,6), instead of (0,0) and (4,5). Authoring a level against a board
/// you can count is the whole point - "the third column" should be 3.
///
/// Display only. The stored value, GridTile.Coordinates, IsoToWorld, every range calculation and every
/// Debug.Log stay zero-based, so a coordinate printed at runtime reads one lower than the same
/// coordinate in the Inspector. Converting for real would mean touching the grid maths everywhere and
/// getting one call site wrong; converting at the drawer touches nothing.
///
/// Runtime-side on purpose - the attribute has to live in Assembly-CSharp because the fields wearing it
/// do. The drawer that does the shifting is Editor-only, in Scripts/Editor. Same split as
/// ReadOnlyFieldAttribute.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class OneBasedCellAttribute : PropertyAttribute { }
