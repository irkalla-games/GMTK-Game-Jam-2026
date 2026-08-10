using System.Collections;
using UnityEngine;

/// <summary>
/// Everything a skip reward may act on.
///
/// A class, not a readonly struct like DamageInfo, for Reoffer: a cancelled card removal has to put
/// the offer back up, and LootManager's panel loop reads the flag back after Grant returns - a struct
/// copy would leave that write invisible to the caller.
/// </summary>
public class RewardContext
{
    /// The live hero being offered a reward. Null once the battle that offered it has ended - nothing
    /// currently does that, but a skip reward that needs a live Character (Heal) should check.
    public Character character;

    /// The run record whose deck actually persists to the next level. Null for a summon or an enemy,
    /// which have no PartyMember - see BattleManager.RecordRunCard for the same null-is-a-no-op rule.
    public PartyMember record;

    /// Set by a skip that backed out of what it was doing (cancelling a card removal, say) rather than
    /// resolving anything - LootManager's offer loop reads this back and shows the same choices again.
    public bool Reoffer;
}

/// <summary>
/// What a player gets for declining all offered cards. Mirrors the CardEffect pattern exactly - one
/// subclass per behaviour, one CreateAssetMenu entry each - so a second skip option (energy next turn,
/// remove a card from the deck) is a new subclass and a new asset, and LootManager's
/// List&lt;SkipReward&gt; renders one button per entry with no branching of its own.
///
/// Grant is a coroutine rather than a plain method because a skip can need to show its own UI and wait
/// on it - RemoveCardSkipReward opens a second panel and blocks until the player picks a card or
/// cancels. A skip with nothing to wait on, like Heal, just yield breaks immediately.
/// </summary>
public abstract class SkipReward : ScriptableObject
{
    /// Button text on the reward panel, e.g. "Skip — Heal 5".
    public abstract string Label { get; }

    public abstract IEnumerator Grant(RewardContext context);
}
