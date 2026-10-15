using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes every Block the wearer holds take more off each hit - Rimeguard Band's "your Block blocks 4
/// instead of 3". Grants no Block itself; projects a BlockPerHitStatus, read through
/// BlockStatus.PerHitFor.
/// </summary>
[CreateAssetMenu(menuName = "Equipment Modifiers/Block Per Hit")]
public class BlockPerHitModifier : EquipmentModifier
{
    [Tooltip("Added to what each Block takes off a hit (BlockStatus.AmountPerHit, 3).")]
    [SerializeField] private int bonus = 1;

    public override void Project(Character carrier, List<Status> into) =>
        into.Add(new BlockPerHitStatus(bonus));

    public override string Describe() => $"Your Block blocks {BlockStatus.AmountPerHit + bonus} per hit";
}
