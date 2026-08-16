using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every EquipmentData asset in the game, in one place - the equipment counterpart to CardLibrary, and
/// the pool LootManager offers an equipment reward from.
///
/// Kept as an explicit, inspectable list rather than a runtime Resources.LoadAll scan, same reasoning
/// as CardLibrary - a Rescan button plus an AssetPostprocessor (see EquipmentLibraryEditor) is what
/// keeps this honest without anyone remembering to touch it by hand.
/// </summary>
[CreateAssetMenu(menuName = "Loot/Equipment Library")]
public class EquipmentLibrary : ScriptableObject
{
    [SerializeField] private List<EquipmentData> items = new();

    public IReadOnlyList<EquipmentData> Items => items;

#if UNITY_EDITOR
    /// Editor-only write path for EquipmentLibraryEditor's Rescan button and its AssetPostprocessor.
    public void SetItems(List<EquipmentData> found)
    {
        items = found;
    }
#endif

    /// Every item at this exact tier that `picker` is allowed to hold, NotOffered already excluded -
    /// mirrors CardLibrary.Offerable exactly.
    public List<EquipmentData> Offerable(Rarity tier, Character picker)
    {
        List<EquipmentData> results = new();

        foreach (EquipmentData item in items)
        {
            if (item == null || item.excludeFromRewards) { continue; }
            if (item.rarity != tier || item.rarity == Rarity.NotOffered) { continue; }
            if (!item.CanBeUsedBy(picker)) { continue; }

            results.Add(item);
        }

        return results;
    }
}
