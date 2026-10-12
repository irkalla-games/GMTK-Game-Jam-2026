"""End-of-lab cleanup. BotRuns/ grew to 43 GB because nothing ever removed anything; most of it is per-run logs.

Kept:    every batch's report.md, CSVs, job.json and every run.json; per batch, the full logs of its best and
         worst run (watchable with Tools/Bot/Bot Runner -> Replay run...); freeze.patch, ledger, status.
Deleted: every other run's turns.log and events.jsonl; the frozen source copy (~1.8 GB); the lab's players.

The lab can be rebuilt from its committed definition, lab.json's freeze record and freeze.patch. Cleanup runs
after the report is built - analysis that reads events.jsonl must be finished first - so it refuses until
lab.json has the published report's url, unless force=True.
"""
import glob
import json
import os
import shutil


def _size(path):
    if os.path.isfile(path):
        return os.path.getsize(path)
    return sum(os.path.getsize(os.path.join(d, f)) for d, _, fs in os.walk(path) for f in fs)


def cleanup(lab, dry_run=True, force=False):
    if not lab.meta.get('url') and not force:
        raise SystemExit(f'{lab.id}: no published report url in lab.json yet - publish first, or force=True')
    doomed, kept = [], []
    for batch in sorted(glob.glob(os.path.join(lab.data_dir, 'batches', '*'))):
        runs = []
        for rj in glob.glob(os.path.join(batch, 'runs', '*', 'run.json')):
            try:
                with open(rj, encoding='utf-8-sig') as f:
                    runs.append((json.load(f).get('levelsCleared', 0), os.path.dirname(rj)))
            except (OSError, ValueError):
                continue
        runs.sort()
        keep = {runs[0][1], runs[-1][1]} if runs else set()
        kept += sorted(keep)
        for _, d in runs:
            if d in keep:
                continue
            doomed += [p for p in (os.path.join(d, 'turns.log'), os.path.join(d, 'events.jsonl')) if os.path.exists(p)]
    for big in (lab.source, os.path.join(lab.data_dir, 'players')):
        if os.path.exists(big):
            doomed.append(big)
    freed = sum(_size(p) for p in doomed)
    if not dry_run:
        for p in doomed:
            shutil.rmtree(p) if os.path.isdir(p) else os.remove(p)
        os.makedirs(os.path.join(lab.data_dir, 'players'), exist_ok=True)
    return {'files': len(doomed), 'freedGB': round(freed / 1024 ** 3, 2), 'keptExampleRuns': kept, 'dryRun': dry_run}
