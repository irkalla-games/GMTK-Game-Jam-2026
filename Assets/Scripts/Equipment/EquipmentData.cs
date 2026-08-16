using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One piece of equipment's authored type - the CardData/CardEffect split again: this is shared across
/// every character who ends up carrying a copy, so nothing here is written to at runtime. A character's
/// List&lt;EquipmentData&gt; (see Character.Equipment) just holds references into this asset; two heroes
/// both wearing Tower Shield point at the same object, exactly like two Bash cards in different decks.
///
/// Deliberately not a ScriptableObject holding a single behaviour - modifiers is a list of
/// EquipmentModifier so one item can carry more than one rule (a relic that both raises damage and
/// widens Fireball), the same reason CardData holds a list of CardEffectEntry rather than one CardEffect.
/// </summary>
[CreateAssetMenu(menuName = "Equipment")]
public class EquipmentData : ScriptableObject
{
    [field: SerializeField] public string equipmentName { get; private set; }

    [field: SerializeField, TextArea] public string description { get; private set; }

    [field: SerializeField] public Sprite icon { get; private set; }

    [field: Tooltip("How good this item is as a reward. NotOffered keeps it out of every LootTable and "
                    + "reward panel.")]
    [field: SerializeField] public Rarity rarity { get; private set; }

    [field: Tooltip("Which character may hold this item. Any means everyone.")]
    [field: SerializeField] public CharacterClass requiredClass { get; private set; }

    [field: Tooltip("Keeps this item out of every reward pool while leaving its rarity alone - mirrors "
                    + "CardData.excludeFromRewards.")]
    [field: SerializeField] public bool excludeFromRewards { get; private set; }

    [field: Tooltip("What this item actually does - one or more rules, each either a permanent combat "
                    + "modifier or a rewrite applied to matching cards. See EquipmentModifier.")]
    [field: SerializeField] public List<EquipmentModifier> modifiers { get; private set; } = new();

    /// Mirrors CardData.CanBeUsedBy exactly - same mask, same Any-on-either-side escape hatch.
    public bool CanBeUsedBy(Character character) =>
        requiredClass == CharacterClass.Any
        || character == null
        || character.Class == CharacterClass.Any
        || (requiredClass & character.Class) != 0;
}
