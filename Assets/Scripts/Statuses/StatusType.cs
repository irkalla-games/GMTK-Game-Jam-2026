/// <summary>
/// Every temporary modifier a character can carry. Buffs and curses are the same machinery - they
/// differ only in their numbers and in which side wants them.
///
/// These values are written into .asset files by ApplyStatusEffect, so they are load-bearing: append
/// new statuses at the end, never reorder. None = 0 so an effect asset whose dropdown was never set does
/// nothing loudly rather than silently granting whatever happened to be listed first. (That is the
/// opposite choice to RangeShape.Anywhere = 0, which had to keep pre-existing assets working - there
/// are no status assets yet, so 0 is free to mean "unset".)
///
/// The rule each one carries lives in its Status subclass, not in a switch somewhere - see Status.
/// </summary>
public enum StatusType
{
    None = 0,

    /// Buff. Adds StrengthStatus.AmountPerHit to each of the next few attacks. The counter is the
    /// charge count, spent one per swing - the same shape as Block on the incoming side.
    Strength = 1,

    /// Buff. Doubles outgoing damage, then spends a charge. No duration - it waits.
    DoubleNextAttack = 2,

    /// Curse. Deals its stack count in damage at the end of the carrier's own phase, bypassing every
    /// defense - see Character.TakeUnblockableDamage.
    Poison = 3,

    /// Curse. The carrier loses its whole turn - no cards, no attack, no movement.
    Frozen = 4,

    /// A pool of extra health that absorbs damage ahead of Health, wiped at the start of every turn.
    /// stacks is the pool.
    Shield = 5,

    /// A flat reduction applied to each of the next few hits. The counter is the charge count; how
    /// much comes off each hit is BlockStatus.AmountPerHit, the same for every Block in the game.
    Block = 6,

    /// Negates each of the next few hits outright and reflects them at the attacker. stacks is the
    /// charge count.
    Parry = 7,

    /// Curse. The carrier cannot move. Everything else - attacking, playing cards - still works.
    /// Frozen's smaller sibling; the two are separate so they compose.
    Rooted = 8,

    DoubleShield = 9,

    /// Negates each of the next few hits outright and sidesteps to a nearby tile, no reflection.
    /// stacks is the charge count - see DodgeStatus and GridManager.StepAwayFrom.
    Dodge = 10,

    Weaken = 11,

    /// Curse. The carrier goes after whoever applied it - both the victim it attacks and the direction
    /// it walks - instead of consulting its own TargetingPattern, and takes no attack at all while that
    /// character is out of reach. See TauntStatus and TargetSelector.
    ///
    /// The one status that cannot be authored through the generic Apply Status asset: it carries a
    /// reference to the taunter, which StatusEffect.Create's type/stacks signature has nowhere to put.
    /// TauntEffect is its authoring half.
    Taunt = 12,

    /// Curse, in the sense that it ends its carrier - a summoned body's own lifetime clock. Counts down
    /// each of the carrier's own turns and, at zero, deals it its own remaining Health as unblockable
    /// damage, which routes through the ordinary CheckDeath -> Died path so loot, roster removal and
    /// death animation all behave exactly as they would for any other death. Not authored through the
    /// generic Apply Status asset - see SummonEffect.lifetimeTurns and SummonAction.
    Summoned = 13,

    /// Buff, and the template for on-hit riders - see OnHitStatus. For as long as it lasts, every hit
    /// the carrier lands also applies Poison to the victim. stacks is remaining turns, not charges - an
    /// AoE that catches three enemies poisons all three without spending anything extra.
    PoisonBlade = 14,

    /// Buff. The carrier cannot be picked by enemy target selection - neither attacked nor walked
    /// toward - for as long as this lasts. stacks is remaining turns. See TargetSelector.TryPick.
    Stealth = 15,

    /// Curse. Adds VulnerableStatus.AmountPerHit to each of the next few hits taken, then spends a
    /// charge. The incoming-damage mirror of Strength - same charge-spent shape, opposite side of the
    /// swing.
    Vulnerable = 16,

    /// Aura-only: StatusEffect.Create returns null for this, same as Taunt. Doubles (or otherwise
    /// scales) the stack count of whatever status type it names whenever that type is applied to a
    /// character standing in the totem's range - see GainMultiplierStatus, AuraData.subject/magnitude
    /// and Character.AddStatus's OnGainStatus pipeline.
    GainMultiplier = 17,

    /// Aura-only: StatusEffect.Create returns null for this, same as Taunt. Adds a flat bonus to the
    /// magnitude of whatever status type it names - more damage per Strength swing, more reduction per
    /// Block charge - without touching the stack count itself. See PotencyStatus and
    /// AuraData.subject/magnitude.
    Potency = 18,

    /// Aura-only: StatusEffect.Create returns null for this, same as Taunt. Grants stacks of whatever
    /// status type it names to the carrier once per round, at whichever end of the round
    /// AuraData.timing says - see TurnTickStatus and TurnTiming.
    TurnTick = 19,
}
