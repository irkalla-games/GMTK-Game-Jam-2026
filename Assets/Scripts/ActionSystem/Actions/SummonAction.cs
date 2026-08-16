using System.Collections;
using UnityEngine;

public class SummonAction : GameAction
{
    private readonly GameObject summonObject;
    private readonly int lifetimeTurns;

    public SummonAction(GameObject summonObject, int lifetimeTurns = 0)
    {
        this.summonObject = summonObject;
        this.lifetimeTurns = lifetimeTurns;
    }

    protected override AnimationCue Cue(ActionContext ctx) => AnimationCue.Summon;

    public override IEnumerator Execute(ActionContext ctx)
    {
        // Caster's cast plus the ritual VFX on the destination tile (CueOverride.targetSpawnPrefab)
        // play first, so the body does not simply pop into existence mid-swing.
        yield return Perform(ctx);

        foreach (GridTile target in ctx.targets)
        {
            Character summoned = target.SummonObject(summonObject);

            if (summoned == null) { continue; }

            // Equipment reacting to a summon (a totem-health relic, say) gets first look, before the
            // Summoned lifetime status below - both are per-summon setup, and there is no ordering
            // requirement between them since neither reads the other.
            if (ctx.source != null)
            {
                foreach (Status status in ctx.source.ActiveStatuses()) { status.OnSummoned(ctx.source, summoned); }
            }

            // 0 means permanent - the default for every summon authored before this field existed,
            // Shield Totem and the enemy Skeleton Warrior included.
            if (lifetimeTurns > 0) { summoned.AddStatus(StatusEffect.Create(StatusType.Summoned, lifetimeTurns)); }

            if (summoned.Animation != null) { yield return summoned.Animation.SpawnIn(); }
        }

        yield return new WaitForSeconds(ResolveDelay);
    }
}
