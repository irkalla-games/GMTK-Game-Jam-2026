using System;

/// <summary>
/// Where on the board a Character prefers to stand - which spawn rows it is eligible for when
/// BattleManager.SpawnParty or EncounterRoller assigns cells.
///
/// [Flags] because a character can be eligible for both rows at once (a Rogue that works either way),
/// unlike PlayableCharacter or BrainType where exactly one value ever applies.
///
/// None = 0 so a Character authored before this field existed - every prefab in the project today -
/// deserializes to "no constraint" rather than silently becoming Frontline-only. A None character is
/// placed wherever a role-constrained character did not need the cell, same as a Both character. This
/// is what lets SpawnParty's role-aware placement ship without re-tagging every existing prefab first.
/// </summary>
[Flags]
public enum BattleRole
{
    /// Unconstrained - eligible for either row, placed only after every Frontline-only and
    /// Backline-only character has claimed its cell. The default for every prefab authored so far.
    None = 0,

    /// Prefers the row closest to the opposing side - see BattleManager.SpawnParty and
    /// EncounterRoller for how "closest" is derived from board geometry.
    Frontline = 1,

    /// Prefers the row farthest from the opposing side.
    Backline = 2,

    /// Eligible for either row, tagged deliberately - as opposed to None, which means "not tagged
    /// yet". The two are treated identically at placement time; this one only says the choice was
    /// made on purpose. Named rather than left to the designer to tick both boxes because the
    /// [Flags] Inspector's other route to both bits is "Everything", which serialises as -1 instead
    /// of 3 and reaches the enemy sheet as Unknown(-1) - see ConvertTo-BattleRoleName's mask.
    Both = Frontline | Backline,
}
