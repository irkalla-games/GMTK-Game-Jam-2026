using UnityEngine;

/// <summary>
/// What a player gets for declining all offered cards. Mirrors the CardEffect pattern exactly - one
/// subclass per behaviour, one CreateAssetMenu entry each - so a second skip option (energy next turn,
/// remove a card from the deck) is a new subclass and a new asset, and LootManager's
/// List&lt;SkipReward&gt; renders one button per entry with no branching of its own.
/// </summary>
public abstract class SkipReward : ScriptableObject
{
    /// Button text on the reward panel, e.g. "Skip — Heal 5".
    public abstract string Label { get; }

    public abstract void Grant(Character picker);
}
