using System;
using UnityEngine;

/// <summary>
/// Which resolved GameAction subclass an AuraReaction watches for. One entry per concrete action - see
/// ActionSystem/Actions - so authoring a reaction never has to name a C# type in the Inspector.
///
/// Written into AuraSource assets/prefabs as an int, so append-only: adding a new GameAction subclass
/// later means adding a new value here, never reordering or reusing one.
/// </summary>
public enum TriggeringActionType
{
    Damage = 0,
    Heal = 1,
    Move = 2,
    Status = 3,
    Block = 4,
    Shield = 5,
    Parry = 6,
    Draw = 7,
    Summon = 8,
}

/// <summary>
/// One trigger an AuraSource watches for: when a resolved action of the given kind belongs to a
/// character standing in the aura's range, `effect` fires once against that character's own tile.
///
/// Deliberately reuses CardEffect rather than inventing a parallel "aura effect" asset type - Grant
/// Block, Apply Status and friends already express exactly "do this to whoever the action context
/// points at", which for a reaction is always the triggering character. AimsAt on the effect asset is
/// ignored here: a reaction has no played tile to distinguish it from, only the one character it fired
/// for.
/// </summary>
[Serializable]
public class AuraReaction
{
    [SerializeField] private TriggeringActionType trigger;

    [SerializeField] private CardEffect effect;

    [Tooltip("One sentence for the totem's tooltip, e.g. \"When an ally attacks nearby, they gain "
             + "Shield 3.\" Left blank, this reaction is not described.")]
    [TextArea]
    [SerializeField] private string description;

    public CardEffect Effect => effect;

    /// The totem tooltip's prose for this reaction, or blank if it should say nothing - see
    /// TotemTooltip.
    public string Description => description;

    public bool Matches(GameAction action) => trigger switch
    {
        TriggeringActionType.Damage => action is DamageAction,
        TriggeringActionType.Heal => action is HealAction,
        TriggeringActionType.Move => action is MoveAction,
        TriggeringActionType.Status => action is StatusAction,
        TriggeringActionType.Block => action is BlockAction,
        TriggeringActionType.Shield => action is ShieldAction,
        TriggeringActionType.Parry => action is ParryAction,
        TriggeringActionType.Draw => action is DrawAction,
        TriggeringActionType.Summon => action is SummonAction,
        _ => false,
    };
}
