using UnityEngine;

/// <summary>
/// A pool of extra health sitting on top of Health. Absorbs damage ahead of it and is wiped at the
/// start of every turn. `stacks` is the pool.
///
/// Pooled rather than per-hit, which is the whole difference from Block: it does not care whether the
/// 8 arrives as one hit or four, so it is the answer to being swarmed.
///
/// Wiped each turn on purpose. Persistent shield would not survive this card set - Steely Attack
/// grants 5 for 1 energy while also dealing damage, so at three plays a turn the Knight would bank 15
/// a turn and stop being killable. Decaying is what makes "armor up or push damage?" a real question
/// every turn rather than a pile you accumulate.
/// </summary>
public class ShieldStatus : StatusEffect
{
    public ShieldStatus(int stacks, int turnsRemaining)
        : base(StatusType.Shield, stacks, turnsRemaining) { }

    public override DamageInfo OnTakeDamage(DamageInfo info)
    {
        int absorbed = Mathf.Min(stacks, info.amount);

        stacks -= absorbed;

        return info.AbsorbedByShield(absorbed);
    }

    /// The one status that clears itself at the top of the round. Zeroing stacks makes it IsExpired,
    /// so Character.PruneExpired drops it without needing a special case.
    public override void OnTurnStart(Character carrier)
    {
        stacks = 0;
    }

    public override string Describe() => $"Shield {stacks}";
}
