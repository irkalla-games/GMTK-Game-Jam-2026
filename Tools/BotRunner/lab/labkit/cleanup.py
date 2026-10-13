"""End-of-lab cleanup. BotRuns/ grew to 43 GB because nothing ever removed anything; most of it is per-run logs.

Kept:    every batch's report.md, CSVs, job.json and every run.json; per batch, the full logs of its best and
         worst run (watchable with Tools/Bot/Bot Runner -> Replay run...); freeze.patch, ledger, status.
Deleted: every other run's turns.log and events.jsonl; the frozen source copy (~1.8 GB); the lab's players.
Gzipped: every batch's logs/shard-*.log (the headless players' stdout, ~35 MB a shard, ~10:1) - kept because a
         lab may re-read LABPLAY lines from them, but never left at full size: in Lab 08 they were 1.2 of the
         1.5 GB cleanup left behind. Read one back with gzip.open(path, 'rt').
Every artefact class a lab writes belongs in one of these three lists; one in none survives silently.

The lab can be rebuilt from its committed definition, lab.json's freeze record and freeze.patch. Cleanup runs
after the report is built - analysis that reads events.jsonl must be finished first - so it refuses until
lab.json has the published report's url, unless force=True.
"""
import glob
import gzip
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
    shards = sorted(glob.glob(os.path.join(lab.data_dir, 'batches', '*', 'logs', 'shard-*.log')))
    freed = sum(_size(p) for p in doomed)
    shard_bytes = sum(os.path.getsize(p) for p in shards)
    if dry_run:
        freed += int(shard_bytes * 0.9)  # estimate: shard logs compress about 10:1
    else:
        for p in doomed:
            shutil.rmtree(p) if os.path.isdir(p) else os.remove(p)
        os.makedirs(os.path.join(lab.data_dir, 'players'), exist_ok=True)
        for p in shards:
            with open(p, 'rb') as src, gzip.open(p + '.gz', 'wb') as dst:
                shutil.copyfileobj(src, dst)
            freed += os.path.getsize(p) - os.path.getsize(p + '.gz')
            os.remove(p)
    return {'files': len(doomed), 'shardLogsGzipped': len(shards), 'freedGB': round(freed / 1024 ** 3, 2),
            'keptExampleRuns': kept, 'dryRun': dry_run}
