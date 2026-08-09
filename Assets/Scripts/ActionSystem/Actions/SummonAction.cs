using System.Collections;
using UnityEngine;

public class SummonAction : GameAction
{
    private readonly GameObject summonObject;

    public SummonAction(GameObject summonObject)
    {
        this.summonObject = summonObject;
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

            if (summoned != null && summoned.Animation != null)
            {
                yield return summoned.Animation.SpawnIn();
            }
        }

        yield return new WaitForSeconds(ResolveDelay);
    }
}
