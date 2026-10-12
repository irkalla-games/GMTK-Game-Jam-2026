"""Spire Lab NN, round <name>: <the one question this round answers>.

Copy to BotRuns/labs/NN-slug/scripts/<round>.py and launch in the background:
    python BotRuns/labs/NN-slug/scripts/<round>.py > BotRuns/labs/NN-slug/scripts/<round>.out 2>&1
Prints one timestamped line per step. The last line is ROUND DONE, or STUCK (exit 1, status.md says why).
"""
import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', '..', '..', '..', 'Tools', 'BotRunner', 'lab'))
sys.stdout.reconfigure(encoding='utf-8')
from labkit import *  # noqa: E402,F401,F403

LAB = Lab.open(os.path.basename(os.path.dirname(HERE)))
PHASE = '<phase name for status.md>'


def say(*a):
    print(now().strftime('%H:%M'), *a, flush=True)


def batch(label, profs, runs, block, tier=0, player=None, **kw):
    """One batch, only if it can finish before the reserve. Returns its folder, or None when out of time."""
    ok, eta = can_start(LAB, runs * len(profs), tier)
    if not ok:
        say(f'NO TIME for {label}: it would end {eta:%H:%M}, the reserve starts {LAB.reserve_start:%H:%M}')
        return None
    LAB.status(PHASE, f'batch {label}: {runs * len(profs)} runs', eta=eta)
    out = run(LAB, label, profs, runs=runs, seed=LAB.seed(block), tier=tier, player=player, **kw)
    say(label, 'done')
    summary(out)
    return out


try:
    vet = profiles.stock(LAB.source, 'Veteran')
    # exe = build(LAB, 'r1', 'NN-slug', alone='3')
    # out = batch('r1-t0', [profiles.explore(vet)], runs=1200, block='ratings', player=exe)
    # if out is None: say('ROUND DONE (out of time)'); sys.exit(0)
    say('ROUND DONE')
except Exception as e:
    LAB.status(PHASE, 'stopped', stuck=f'{type(e).__name__}: {e}')
    traceback.print_exc()
    say('STUCK')
    sys.exit(1)
