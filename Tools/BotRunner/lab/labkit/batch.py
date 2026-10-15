"""Batches: run them, and read them only once they are finished.

    out = run(lab, 'base-t0', [vet], runs=250, seed=lab.seed('eval'), party=KC, player=lab.player('base'))
    levels(out)                                  # {profile: {n, levels, se, byRun}}
    paired(out_a, 'Veteran', out_b, 'Veteran8')  # (mean diff, se, n) run by run - same seeds required

A batch that has not finished cannot be read: runs(), levels() and everything built on them raise
IncompleteBatch. Runs finish in order of length, not seed, so the first half of a batch over-represents
short (losing) runs and any number read from it is biased - a +0.59 once became +0.16 when the batch
finished. Pass allow_partial=True only to watch progress, never to quote.
"""
import collections
import copy
import glob
import json
import os
import subprocess
import time

from .lab import now, TIME, LabBusy, active_lab
from .paths import BASE_JOB, RUN_BATCH

KC = [('Knight', ''), ('Cleric', '')]


class IncompleteBatch(Exception):
    pass


class BatchFailed(Exception):
    pass


def _job_of(batch):
    path = os.path.join(batch, 'job.json')
    if not os.path.exists(path):
        raise IncompleteBatch(f'{batch} has no job.json - not a batch, or never started')
    with open(path, encoding='utf-8-sig') as f:
        return json.load(f)


def expected_runs(batch):
    j = _job_of(batch)
    return j['runsPerProfile'] * len(j['profiles'])


def run_files(batch):
    return glob.glob(os.path.join(batch, 'runs', '*', 'run.json'))


def is_complete(batch):
    """Finished = Run-BotBatch merged report.md AND every run of every profile wrote run.json."""
    return (os.path.exists(os.path.join(batch, 'report.md'))
            and len(run_files(batch)) >= expected_runs(batch))


def progress(batch):
    """(done, total) from the shards' status files while a batch runs."""
    done = total = 0
    for f in glob.glob(os.path.join(batch, 'status-shard-*.json')):
        try:
            s = json.load(open(f, encoding='utf-8-sig'))
            done += int(s['runsDone'])
            total += int(s['runsTotal'])
        except (OSError, ValueError, KeyError):
            pass
    return done, total


def make_job(label, profiles, runs, seed, party=KC, tier=0, **fields):
    with open(BASE_JOB, encoding='utf-8') as f:
        j = json.load(f)
    j.update(label=label, profiles=copy.deepcopy(list(profiles)), runsPerProfile=runs, baseSeed=seed, tier=tier,
             party=[{'hero': h, 'deck': d} for h, d in party], writeTurnLog=True)
    j.update(fields)
    names = [p['name'] for p in j['profiles']]
    if len(set(names)) != len(names):
        raise ValueError(f'profile names must differ within a batch: {names}')
    return j


def run(lab, label, profiles, runs, seed, party=KC, tier=0, player=None, procs=15, timeout_minutes=240, **fields):
    """Runs a batch to completion (blocking) into BotRuns/labs/NN-slug/batches/<label> and returns its path.
    Re-running a finished label returns at once; an interrupted one resumes (finished runs are kept).
    A crashed or timed-out player is retried once by resuming; still incomplete raises BatchFailed."""
    held = active_lab()
    if held and held['lab'] != lab.id:
        raise LabBusy(f"{held['lab']} holds the machine")
    out = lab.batch_dir(label)
    if os.path.isdir(out) and is_complete(out):
        return out
    if player is None or not os.path.exists(player):
        raise BatchFailed(f'no player at {player} - build(lab, ...) first')
    job_path = os.path.join(lab.data_dir, 'jobs', f'{label}.job.json')
    os.makedirs(os.path.dirname(job_path), exist_ok=True)
    with open(job_path, 'w', encoding='utf-8') as f:
        json.dump(make_job(label, profiles, runs, seed, party, tier, **fields), f, indent=1, ensure_ascii=False)

    started = time.time()
    code = None
    for attempt in (1, 2):
        os.makedirs(out, exist_ok=True)
        with open(os.path.join(out, 'runner.log'), 'a', encoding='utf-8') as log:
            code = subprocess.run(['powershell', '-ExecutionPolicy', 'Bypass', '-File', RUN_BATCH, '-Job', job_path,
                                   '-Processes', str(procs), '-Out', out, '-Player', player,
                                   '-TimeoutMinutes', str(timeout_minutes)], stdout=log, stderr=log).returncode
        if code == 3:
            raise BatchFailed(f'{label}: the player rejected the job - see {out}\\logs')
        if is_complete(out):
            break
    hours = (time.time() - started) / 3600
    total = runs * len(profiles)
    lab.record('batches', {'name': label, 'runs': total, 'profiles': [p['name'] for p in profiles], 'seed': seed,
                           'tier': tier, 'party': '+'.join(h for h, _ in party), 'procs': procs,
                           'player': os.path.relpath(player, lab.data_dir), 'exit': code,
                           'complete': is_complete(out), 'hours': round(hours, 3),
                           'runsPerHour': round(total / hours, 1) if hours > 0 else None,
                           'finished': now().strftime(TIME)})
    if not is_complete(out):
        raise BatchFailed(f'{label} incomplete after a resume (exit {code}) - see {out}\\runner.log and logs\\')
    return out


# ---------------------------------------------------------------------- reading finished batches
def runs(batch, allow_partial=False):
    """Every finished-and-valid run (Won or Died; stalled/errored runs excluded, as Rate-Rewards does)."""
    if not allow_partial and not is_complete(batch):
        d, t = progress(batch)
        raise IncompleteBatch(f'{os.path.basename(batch)} is still running ({d}/{t}) - its numbers are biased '
                              f'toward short runs until it finishes')
    for path in run_files(batch):
        with open(path, encoding='utf-8-sig') as f:
            r = json.load(f)
        if r.get('baseline') or r.get('outcome') not in ('Won', 'Died'):
            continue
        r['_dir'] = os.path.dirname(path)
        yield r


def _mean_se(values):
    n = len(values)
    if n == 0:
        return None, None
    m = sum(values) / n
    se = (sum((x - m) ** 2 for x in values) / max(1, n - 1)) ** 0.5 / n ** 0.5
    return m, se


def levels(batch, field='levelsCleared'):
    """Per profile: n, mean levels cleared, its standard error, and the value by run index (for pairing)."""
    by = collections.defaultdict(dict)
    for r in runs(batch):
        by[r['profile']][r['runIndex']] = r[field]
    out = {}
    for prof, m in by.items():
        mean, se = _mean_se(list(m.values()))
        out[prof] = {'n': len(m), 'levels': round(mean, 3), 'se': round(se, 3), 'byRun': m}
    return out


def paired(batch_a, prof_a, batch_b, prof_b):
    """Mean of (a - b) levels over runs both finished, matched by run index. Both batches must share a base
    seed - pairing different seeds is meaningless, so that raises."""
    sa, sb = _job_of(batch_a)['baseSeed'], _job_of(batch_b)['baseSeed']
    if sa != sb:
        raise ValueError(f'cannot pair base seed {sa} with {sb} - compare with a shared lab.seed(block)')
    A, B = levels(batch_a)[prof_a]['byRun'], levels(batch_b)[prof_b]['byRun']
    d = [A[k] - B[k] for k in A if k in B]
    m, se = _mean_se(d)
    return (round(m, 3), round(se, 3), len(d)) if d else (None, None, 0)


def pool(*estimates):
    """Inverse-variance pool of (mean, se) estimates - e.g. the selection seeds and the fresh-seed replication,
    which the report must show separately as well as pooled."""
    est = [(m, s) for m, s in estimates if m is not None and s]
    w = [1 / s ** 2 for _, s in est]
    m = sum(wi * mi for wi, (mi, _) in zip(w, est)) / sum(w)
    return round(m, 3), round((1 / sum(w)) ** 0.5, 3)


def summary(batch):
    """Prints and returns one row per profile: runs, won, levels ± se, falls, stalls, ms per decision."""
    raw = collections.defaultdict(list)
    for path in run_files(batch) if is_complete(batch) else []:
        with open(path, encoding='utf-8-sig') as f:
            r = json.load(f)
        raw[r['profile']].append(r)
    if not raw:
        d, t = progress(batch)
        print(f'{os.path.basename(batch)}: not finished ({d}/{t}) - no numbers until it is')
        return []
    rows = []
    for prof, rs in sorted(raw.items()):
        ok = [r for r in rs if r['outcome'] in ('Won', 'Died')]
        m, se = _mean_se([r['levelsCleared'] for r in ok])
        rows.append({'profile': prof, 'n': len(ok), 'won': sum(r['outcome'] == 'Won' for r in ok),
                     'levels': round(m, 3), 'se': round(se, 3),
                     'falls': round(sum(r['falls'] for r in ok) / len(ok), 2),
                     'stalls': len(rs) - len(ok), 'ms': round(sum(r['plannerMsAvg'] for r in ok) / len(ok), 1)})
    print(f"{'profile':24s} {'n':>4s} {'won':>4s} {'levels':>7s} {'±se':>5s} {'falls':>6s} {'stall':>5s} {'ms':>6s}")
    for r in sorted(rows, key=lambda x: -x['levels']):
        print(f"{r['profile']:24s} {r['n']:4d} {r['won']:4d} {r['levels']:7.2f} {r['se']:5.2f} {r['falls']:6.2f} "
              f"{r['stalls']:5d} {r['ms']:6.1f}")
    return rows
