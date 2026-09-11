using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One rung of the difficulty ladder: how much harder this tier is than Normal, and what clearing a
/// run on it earns. The authoring half of a difficulty, the way LevelData is the authoring half of a
/// level - RunManager snapshots which rung a run is being played on and asks this for the numbers.
///
/// Both scales are stored as a *bonus added to 1*, not as a raw multiplier: extraEncounterPower of
/// 0.25 means x1.25. That is not a style choice. A float in a struct authored before the field
/// existed deserializes to 0, and a raw multiplier would then read x0 and silently delete every
/// encounter in the game. As a bonus, the all-zero default means "exactly like Normal", which is the
/// only safe thing it can mean - the same reasoning that keeps RangeShape.Anywhere at 0 and
/// DamageEffect.canHitAllies off.
///
/// Public fields rather than the private-plus-property shape CardData uses, matching every other
/// [System.Serializable] struct authored into a list here - EnemyPlacement, PartyEntry,
/// EncounterBudget.
/// </summary>
[System.Serializable]
public struct DifficultyTier
{
    [Tooltip("Shown on the difficulty selector - \"Normal\", \"Hard I\". Blank falls back to the "
             + "rung's index, so an unnamed tier is still pickable.")]
    public string displayName;

    [Tooltip("Added to 1 before multiplying EncounterBudget's spend budgets. 0 is unchanged, 0.25 is "
             + "x1.25. See the struct's own summary for why this is a bonus and not a multiplier.")]
    public float extraEncounterPower;

    [Tooltip("Added to 1 before multiplying a hostile body's max health as it joins the battle. 0 is "
             + "unchanged, 0.25 is x1.25.")]
    public float extraEnemyHealth;

    [Tooltip("Characters unlocked the first time a run is cleared on this tier. Empty for most rungs "
             + "- only the ones that gate a hero carry anything.")]
    public List<CharacterOption> unlocksOnClear;

    /// <summary>
    /// This tier's version of `budget`, for BattleManager.RollEncounter to spend.
    ///
    /// Only the six *spend* budgets scale. maxPowerPerEnemy and maxPowerPerWave are per-body and
    /// per-wave ceilings, and holding them fixed is what makes a higher tier field more enemies
    /// rather than quietly swapping the same count for bigger ones - a ramp a player can read.
    /// waveInterval, waveIntervalJitter and poolFilter are not power at all.
    ///
    /// Returns a copy; EncounterBudget is a struct and LevelData hands out its own by value, so the
    /// level asset is never touched.
    /// </summary>
    public EncounterBudget Scale(EncounterBudget budget)
    {
        float scale = 1f + extraEncounterPower;

        budget.frontlinePower *= scale;
        budget.backlinePower *= scale;
        budget.anyPower *= scale;
        budget.bossFrontlinePower *= scale;
        budget.bossBacklinePower *= scale;
        budget.reinforcementPower *= scale;

        return budget;
    }

    /// <summary>
    /// Extra hit points a body with `maxHealth` gets on this tier, for Character.AddMaxHealth.
    ///
    /// A bonus rather than a new total because AddMaxHealth is what already exists and already does
    /// the right thing - it raises the ceiling and current health together, so a body does not join
    /// the battle pre-damaged.
    /// </summary>
    public int ExtraHealthFor(int maxHealth) => Mathf.RoundToInt(maxHealth * extraEnemyHealth);
}

/// <summary>
/// The difficulty rungs a campaign offers, in the order they are unlocked. Index 0 is Normal and is
/// the only one available on a fresh save; clearing tier N opens tier N+1 - see DifficultyProgress,
/// which is where how-far-you-got is remembered.
///
/// An asset rather than a table in code for the same reason RunData is one: the numbers are content,
/// and a designer retuning Hard III should not need a recompile.
///
/// Referenced from RunData rather than held by RunManager, because RunManager has no prefab and lives
/// in no scene, so it has nothing to serialize a reference on. A campaign naming its own ladder also
/// makes a debug RunData with no ladder mean "never scales anything" with no special case anywhere.
/// </summary>
[CreateAssetMenu(menuName = "Difficulty Ladder")]
public class DifficultyLadder : ScriptableObject
{
    [Tooltip("Rungs in unlock order. Index 0 must be the unscaled one - it is what a fresh save "
             + "plays, and what every debug and tutorial run is pinned to.")]
    [SerializeField] private List<DifficultyTier> tiers = new();

    public IReadOnlyList<DifficultyTier> Tiers => tiers;

    /// <summary>
    /// The rung at `index`, or an all-zero tier when there is none.
    ///
    /// Out of range returns the default rather than clamping into the list or throwing, and the
    /// default is harmless by construction: every scale is a bonus, so an all-zero tier is exactly
    /// Normal and unlocks nothing. A save naming a tier this ladder no longer has therefore plays
    /// the game rather than breaking it.
    /// </summary>
    public DifficultyTier TierAt(int index) =>
        index >= 0 && index < tiers.Count ? tiers[index] : default;

    /// The label for `index`, falling back to a one-based number for an unnamed rung.
    public string NameAt(int index)
    {
        string authored = TierAt(index).displayName;

        return string.IsNullOrWhiteSpace(authored) ? $"Tier {index + 1}" : authored;
    }
}
