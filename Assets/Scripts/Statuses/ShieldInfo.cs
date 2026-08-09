using UnityEngine;

/// <summary>
/// One damage event on its way through the status hooks.
///
/// Immutable, and passed *through* the hooks rather than handed to them: each status receives one of
/// these and returns the next, so `info = status.OnTakeDamage(info)` is the whole pipeline. A status
/// therefore cannot quietly half-modify a shared object, and a hook that forgets to account for
/// something returns the value it was given rather than leaving a partly-written one behind.
///
/// A readonly struct rather than a class because these are produced one per status per hit and
/// discarded immediately - by value there is nothing to collect. Same reasoning TargetRange documents
/// for being a value type.
///
/// Used in both directions. On the way out (Character.ComputeOutgoingDamage) `attacker` is the carrier
/// and `target` is null - nobody has been picked yet, because an AoE resolves this once for every tile
/// it is about to hit. On the way in (Character.TakeDamage) `target` is the carrier and `attacker` is
/// whoever swung, or null for damage with no author.
/// </summary>
public readonly struct ShieldInfo   
{
  
}
